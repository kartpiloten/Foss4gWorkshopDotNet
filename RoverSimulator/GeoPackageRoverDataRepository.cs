/*
 The functionallity in this file is:
 - Implement a simple GeoPackage-backed repository using MapPiloteGeopackageHelper (OGC standard, SQLite-based).
 - Create/reset the file, insert features, and provide helpers to detect file locks from other apps (QGIS, etc.).
 - Use async/await and culture-invariant formatting where needed; keep console guidance minimal.
*/

using MapPiloteGeopackageHelper; // NuGet helper for GeoPackage (async APIs)
using NetTopologySuite.Geometries; // NTS geometry types (Point) for FOSS4G interoperability
using System.Globalization; // InvariantCulture for numeric formatting
using System.Diagnostics; // Process inspection for lock hints

namespace RoverSimulator;

public class GeoPackageRoverDataRepository : RoverDataRepositoryBase
{
    private GeoPackage? _geoPackage;
    private GeoPackageLayer? _measurementsLayer;
    private string? _dbPath;
    private string? _folderPath;
    private const string LayerName = "rover_measurements";
    private const string FixedFileName = "rover_data.gpkg"; // Fixed filename, no timestamp

    public GeoPackageRoverDataRepository(string connectionString) : base(connectionString)
    {
        // Treat connection string as folder path
        _folderPath = connectionString?.TrimEnd('\\', '/');
        
        if (string.IsNullOrEmpty(_folderPath))
        {
            _folderPath = Environment.CurrentDirectory;
        }

        // Use fixed filename instead of timestamped one
        _dbPath = Path.Combine(_folderPath, FixedFileName);
    }

    /// <summary>
    /// Checks if the GeoPackage file is currently locked by another process.
    /// </summary>
    public bool IsFileLocked()
    {
        if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath))
            return false;

        try
        {
            using var stream = File.Open(_dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false; // File is not locked
        }
        catch (IOException)
        {
            return true; // File is locked
        }
        catch (Exception)
        {
            return false; // Other errors, assume not locked
        }
    }

    /// <summary>
    /// Gets information about which processes might be locking the file (best-effort).
    /// </summary>
    public List<string> GetLockingProcesses()
    {
        var lockingProcesses = new List<string>();
        
        if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath))
            return lockingProcesses;

        try
        {
            var processes = Process.GetProcesses();
            
            foreach (var process in processes)
            {
                try
                {
                    var processName = process.ProcessName.ToLower();
                    if (processName.Contains("qgis") || 
                        processName.Contains("arcgis") || 
                        processName.Contains("arcmap") || 
                        processName.Contains("arcpro") ||
                        processName.Contains("roversimulator"))
                    {
                        lockingProcesses.Add($"{process.ProcessName} (PID: {process.Id})");
                    }
                }
                catch
                {
                    // Ignore processes we can't access
                }
            }
        }
        catch (Exception ex)
        {
            lockingProcesses.Add($"Error detecting processes: {ex.Message}");
        }

        return lockingProcesses;
    }

    /// <summary>
    /// Attempts to force unlock the file by waiting and retrying.
    /// </summary>
    public async Task<bool> TryForceUnlockAsync(int maxAttempts = 5, int delayMs = 1000)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (!IsFileLocked())
                return true;

            Console.WriteLine($"Attempt {attempt}/{maxAttempts}: File still locked, waiting {delayMs}ms...");
            await Task.Delay(delayMs);
        }

        return false;
    }

    /// <summary>
    /// Checks and reports the file lock status to the user.
    /// </summary>
    public void CheckFileLockStatus()
    {
        if (string.IsNullOrEmpty(_dbPath))
            return;

        if (File.Exists(_dbPath))
        {
            Console.WriteLine($"Found existing GeoPackage file: {_dbPath}");
            
            if (IsFileLocked())
            {
                Console.WriteLine("WARNING: File is currently locked by another application!");
                
                var lockingProcesses = GetLockingProcesses();
                if (lockingProcesses.Any())
                {
                    Console.WriteLine("Potentially locking processes:");
                    foreach (var process in lockingProcesses)
                    {
                        Console.WriteLine($"  - {process}");
                    }
                }
                
                Console.WriteLine("This usually means QGIS, ArcGIS, or another RoverSimulator instance has the file open.");
                Console.WriteLine("The file must be closed before the simulation can start.");
            }
            else
            {
                Console.WriteLine("File is available and can be overwritten.");
            }
        }
        else
        {
            Console.WriteLine($"No existing GeoPackage file found. Will create: {_dbPath}");
        }
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_dbPath) || string.IsNullOrEmpty(_folderPath))
            throw new InvalidOperationException("Database path not specified");

        try
        {
            if (!Directory.Exists(_folderPath))
            {
                Directory.CreateDirectory(_folderPath);
                Console.WriteLine($"Created directory: {_folderPath}");
            }

            Console.WriteLine($"Creating GeoPackage: {_dbPath}");
            
            _geoPackage = await GeoPackage.OpenAsync(_dbPath, 4326); // WGS84 CRS

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
        if (string.IsNullOrEmpty(_dbPath))
            return;

        _geoPackage?.Dispose();
        _geoPackage = null;
        _measurementsLayer = null;

        await Task.Delay(100, cancellationToken);

        if (File.Exists(_dbPath))
        {
            try
            {
                if (IsFileLocked())
                {
                    Console.WriteLine("File is locked, attempting to wait for unlock...");
                    var unlocked = await TryForceUnlockAsync(maxAttempts: 3, delayMs: 500);
                    
                    if (!unlocked)
                    {
                        var lockingProcesses = GetLockingProcesses();
                        var processInfo = lockingProcesses.Any() ? $" Locking processes: {string.Join(", ", lockingProcesses)}" : "";
                        throw new InvalidOperationException($"Cannot delete GeoPackage file - it is locked by another application.{processInfo} Please close QGIS, ArcGIS, or other RoverSimulator instances and try again: {_dbPath}");
                    }
                }

                File.Delete(_dbPath);
                Console.WriteLine($"Deleted existing GeoPackage: {Path.GetFileName(_dbPath)}");
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Failed to delete existing GeoPackage file: {ex.Message}. The file may be locked by another application. Please close all applications using the file and try again.", ex);
            }
        }
        else
        {
            Console.WriteLine("No existing GeoPackage file to delete.");
        }
    }

    public override async Task InsertMeasurementAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Repository not initialized. Call InitializeAsync first.");

        try
        {
            var featureRecord = new FeatureRecord(
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
            );

            var features = new List<FeatureRecord> { featureRecord };
            await _measurementsLayer.BulkInsertAsync(
                features,
                new BulkInsertOptions(
                    BatchSize: 1,
                    CreateSpatialIndex: false,
                    ConflictPolicy: ConflictPolicy.Ignore
                ),
                null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to insert measurement: {ex.Message}", ex);
        }
    }

    // Extra bulk operations for demos
    public async Task BulkInsertMeasurementsAsync(IEnumerable<RoverMeasurement> measurements, IProgress<BulkProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Repository not initialized. Call InitializeAsync first.");

        try
        {
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
            var sessionId = Guid.Parse(feature.Attributes["session_id"] ?? throw new InvalidDataException("Missing session_id"));
            var sequence = int.Parse(feature.Attributes["sequence"] ?? "0", CultureInfo.InvariantCulture);
            var recordedAt = DateTimeOffset.Parse(feature.Attributes["recorded_at"] ?? throw new InvalidDataException("Missing recorded_at"));
            var latitude = double.Parse(feature.Attributes["latitude"] ?? "0", CultureInfo.InvariantCulture);
            var longitude = double.Parse(feature.Attributes["longitude"] ?? "0", CultureInfo.InvariantCulture);
            var windDirection = short.Parse(feature.Attributes["wind_direction_deg"] ?? "0", CultureInfo.InvariantCulture);
            var windSpeed = float.Parse(feature.Attributes["wind_speed_mps"] ?? "0", CultureInfo.InvariantCulture);

            if (feature.Geometry == null)
                throw new InvalidDataException("Feature missing geometry");

            if (feature.Geometry is not Point point)
                throw new InvalidDataException("Feature geometry is not a Point");

            yield return new RoverMeasurement(
                sessionId,
                sequence,
                recordedAt,
                latitude,
                longitude,
                windDirection,
                windSpeed,
                point
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
                    _measurementsLayer = null;
                    _geoPackage?.Dispose();
                    _geoPackage = null;
                    
                    Thread.Sleep(100);

                    if (!string.IsNullOrEmpty(_dbPath) && File.Exists(_dbPath))
                    {
                        var fileInfo = new FileInfo(_dbPath);
                        Console.WriteLine("Final GeoPackage stats:");
                        Console.WriteLine($"   File: {fileInfo.Name}");
                        Console.WriteLine($"   Location: {fileInfo.DirectoryName}");
                        Console.WriteLine($"   Size: {fileInfo.Length / 1024.0:F1} KB");
                        Console.WriteLine($"   Created: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine("You can open this file in QGIS, ArcGIS, or other GIS software!");
                        Console.WriteLine("The file should now be available for other applications to use.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during disposal: {ex.Message}");
                }
            }
            base.Dispose(disposing);
        }
    }
}