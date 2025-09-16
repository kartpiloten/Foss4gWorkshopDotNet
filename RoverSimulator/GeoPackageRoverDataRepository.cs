using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using System.Globalization;

namespace RoverSimulator;

public class GeoPackageRoverDataRepository : RoverDataRepositoryBase
{
    private GeoPackage? _geoPackage;
    private GeoPackageLayer? _measurementsLayer;
    private string? _dbPath;
    private string? _folderPath;
    private const string LayerName = "rover_measurements";

    public GeoPackageRoverDataRepository(string connectionString) : base(connectionString)
    {
        // Treat connection string as folder path
        _folderPath = connectionString?.TrimEnd('\\', '/');
        
        if (string.IsNullOrEmpty(_folderPath))
        {
            _folderPath = Environment.CurrentDirectory;
        }

        // Create timestamped filename for this session
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"rover_data_{timestamp}.gpkg";
        _dbPath = Path.Combine(_folderPath, fileName);
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_dbPath) || string.IsNullOrEmpty(_folderPath))
            throw new InvalidOperationException("Database path not specified");

        try
        {
            // Ensure the target directory exists
            if (!Directory.Exists(_folderPath))
            {
                Directory.CreateDirectory(_folderPath);
                Console.WriteLine($"Created directory: {_folderPath}");
            }

            Console.WriteLine($"Creating GeoPackage: {_dbPath}");
            
            // Use MapPiloteGeopackageHelper to create/open the GeoPackage with WGS84 (4326)
            _geoPackage = await GeoPackage.OpenAsync(_dbPath, 4326);

            // Define schema for rover measurements
            var measurementSchema = new Dictionary<string, string>
            {
                ["session_id"] = "TEXT NOT NULL",
                ["sequence"] = "INTEGER NOT NULL",
                ["recorded_at"] = "TEXT NOT NULL", 
                ["latitude"] = "REAL NOT NULL",
                ["longitude"] = "REAL NOT NULL",
                ["wind_direction_deg"] = "INTEGER NOT NULL",
                ["wind_speed_mps"] = "REAL NOT NULL"
            };

            // Ensure the rover_measurements layer exists
            Console.WriteLine($"Creating layer '{LayerName}'...");
            _measurementsLayer = await _geoPackage.EnsureLayerAsync(LayerName, measurementSchema, 4326);

            Console.WriteLine("GeoPackage initialized successfully!");
            Console.WriteLine($"File: {Path.GetFileName(_dbPath)}");
            Console.WriteLine($"Location: {_folderPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing GeoPackage: {ex.Message}");
            throw;
        }
    }

    public override async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        // For timestamped files, we don't need to reset/delete anything
        // Each run creates a new file automatically
        Console.WriteLine($"New GeoPackage session will be created at: {_dbPath}");
    }

    public override async Task InsertMeasurementAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Repository not initialized. Call InitializeAsync first.");

        try
        {
            // Create a FeatureRecord for the measurement
            var featureRecord = new FeatureRecord(
                measurement.Geometry,
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["session_id"] = measurement.SessionId.ToString(),
                    ["sequence"] = measurement.Sequence.ToString(CultureInfo.InvariantCulture),
                    ["recorded_at"] = measurement.RecordedAt.ToString("O"), // ISO 8601 format
                    ["latitude"] = measurement.Latitude.ToString("F8", CultureInfo.InvariantCulture),
                    ["longitude"] = measurement.Longitude.ToString("F8", CultureInfo.InvariantCulture),
                    ["wind_direction_deg"] = measurement.WindDirectionDeg.ToString(CultureInfo.InvariantCulture),
                    ["wind_speed_mps"] = measurement.WindSpeedMps.ToString("F2", CultureInfo.InvariantCulture)
                }
            );

            // Use BulkInsertAsync with a single feature - this is the correct way to insert individual features
            var features = new List<FeatureRecord> { featureRecord };
            await _measurementsLayer.BulkInsertAsync(
                features,
                new BulkInsertOptions(
                    BatchSize: 1,
                    CreateSpatialIndex: false, // Don't create index for single inserts
                    ConflictPolicy: ConflictPolicy.Ignore
                ),
                null, // No progress reporting for single insert
                cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to insert measurement: {ex.Message}", ex);
        }
    }

    public async Task BulkInsertMeasurementsAsync(IEnumerable<RoverMeasurement> measurements, IProgress<BulkProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Repository not initialized. Call InitializeAsync first.");

        try
        {
            // Convert measurements to FeatureRecords
            var featureRecords = measurements.Select(measurement => new FeatureRecord(
                measurement.Geometry,
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["session_id"] = measurement.SessionId.ToString(),
                    ["sequence"] = measurement.Sequence.ToString(CultureInfo.InvariantCulture),
                    ["recorded_at"] = measurement.RecordedAt.ToString("O"),
                    ["latitude"] = measurement.Latitude.ToString("F8", CultureInfo.InvariantCulture),
                    ["longitude"] = measurement.Longitude.ToString("F8", CultureInfo.InvariantCulture),
                    ["wind_direction_deg"] = measurement.WindDirectionDeg.ToString(CultureInfo.InvariantCulture),
                    ["wind_speed_mps"] = measurement.WindSpeedMps.ToString("F2", CultureInfo.InvariantCulture)
                }
            )).ToList();

            // Use bulk insert with proper options
            await _measurementsLayer.BulkInsertAsync(
                featureRecords,
                new BulkInsertOptions(
                    BatchSize: 500,
                    CreateSpatialIndex: true,
                    ConflictPolicy: ConflictPolicy.Ignore
                ),
                progress,
                cancellationToken);

            Console.WriteLine($"Bulk inserted {featureRecords.Count} measurements successfully!");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to bulk insert measurements: {ex.Message}", ex);
        }
    }

    public async Task<long> GetMeasurementCountAsync(string? whereClause = null, CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Repository not initialized. Call InitializeAsync first.");

        return await _measurementsLayer.CountAsync(whereClause, cancellationToken);
    }

    public async IAsyncEnumerable<RoverMeasurement> QueryMeasurementsAsync(
        string? whereClause = null, 
        string? orderBy = null, 
        int? limit = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Repository not initialized. Call InitializeAsync first.");

        var readOptions = new ReadOptions(
            IncludeGeometry: true,
            WhereClause: whereClause,
            OrderBy: orderBy,
            Limit: limit
        );

        await foreach (var feature in _measurementsLayer.ReadFeaturesAsync(readOptions, cancellationToken))
        {
            // Convert back to RoverMeasurement
            var sessionId = Guid.Parse(feature.Attributes["session_id"] ?? throw new InvalidDataException("Missing session_id"));
            var sequence = int.Parse(feature.Attributes["sequence"] ?? "0", CultureInfo.InvariantCulture);
            var recordedAt = DateTimeOffset.Parse(feature.Attributes["recorded_at"] ?? throw new InvalidDataException("Missing recorded_at"));
            var latitude = double.Parse(feature.Attributes["latitude"] ?? "0", CultureInfo.InvariantCulture);
            var longitude = double.Parse(feature.Attributes["longitude"] ?? "0", CultureInfo.InvariantCulture);
            var windDirection = short.Parse(feature.Attributes["wind_direction_deg"] ?? "0", CultureInfo.InvariantCulture);
            var windSpeed = float.Parse(feature.Attributes["wind_speed_mps"] ?? "0", CultureInfo.InvariantCulture);

            yield return new RoverMeasurement(
                sessionId,
                sequence,
                recordedAt,
                latitude,
                longitude,
                windDirection,
                windSpeed,
                (Point)feature.Geometry
            );
        }
    }

    public async Task<long> DeleteMeasurementsAsync(string whereClause, CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Repository not initialized. Call InitializeAsync first.");

        return await _measurementsLayer.DeleteAsync(whereClause, cancellationToken);
    }

    public async Task CreateSpatialIndexAsync(CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Repository not initialized. Call InitializeAsync first.");

        await _measurementsLayer.CreateSpatialIndexAsync(cancellationToken);
        Console.WriteLine("Spatial index created successfully!");
    }

    public string? GetCurrentGeoPackagePath() => _dbPath;

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_dbPath) && File.Exists(_dbPath))
                    {
                        var fileInfo = new FileInfo(_dbPath);
                        Console.WriteLine("Final GeoPackage stats:");
                        Console.WriteLine($"   File: {fileInfo.Name}");
                        Console.WriteLine($"   Location: {fileInfo.DirectoryName}");
                        Console.WriteLine($"   Size: {fileInfo.Length / 1024.0:F1} KB");
                        Console.WriteLine($"   Created: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine("You can open this file in QGIS, ArcGIS, or other GIS software!");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting file stats: {ex.Message}");
                }

                _measurementsLayer = null;
                _geoPackage?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}