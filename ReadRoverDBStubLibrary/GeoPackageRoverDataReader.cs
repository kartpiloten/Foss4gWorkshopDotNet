using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using System.Globalization;

namespace ReadRoverDBStubLibrary;

/// <summary>
/// GeoPackage rover data reader (silent library version)
/// </summary>
public class GeoPackageRoverDataReader : RoverDataReaderBase
{
    private GeoPackage? _geoPackage;
    private GeoPackageLayer? _measurementsLayer;
    private string? _dbPath;
    private string? _folderPath;
    private const string LayerName = "rover_measurements";
    private const string FixedFileName = "rover_data.gpkg";

    public GeoPackageRoverDataReader(string connectionString) : base(connectionString)
    {
        // Treat connection string as folder path or direct file path
        if (connectionString.EndsWith(".gpkg", StringComparison.OrdinalIgnoreCase))
        {
            _dbPath = connectionString;
            _folderPath = Path.GetDirectoryName(connectionString);
        }
        else
        {
            _folderPath = connectionString?.TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(_folderPath))
            {
                _folderPath = Environment.CurrentDirectory;
            }
            _dbPath = Path.Combine(_folderPath, FixedFileName);
        }
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_dbPath))
            throw new InvalidOperationException("Database path not specified");

        try
        {
            if (!File.Exists(_dbPath))
            {
                throw new FileNotFoundException($"GeoPackage file not found: {_dbPath}");
            }
            
            // Open the GeoPackage in read-only mode
            _geoPackage = await GeoPackage.OpenAsync(_dbPath, 4326);

            // Get the rover_measurements layer (schema is empty since we're just reading)
            _measurementsLayer = await _geoPackage.EnsureLayerAsync(LayerName, new Dictionary<string, string>(), 4326);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error opening GeoPackage: {ex.Message}", ex);
        }
    }

    public override async Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Reader not initialized. Call InitializeAsync first.");

        return await _measurementsLayer.CountAsync();
    }

    public override async Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Reader not initialized. Call InitializeAsync first.");

        var readOptions = new ReadOptions(
            IncludeGeometry: true,
            WhereClause: whereClause,
            OrderBy: "sequence ASC"
        );

        var measurements = new List<RoverMeasurement>();

        await foreach (var feature in _measurementsLayer.ReadFeaturesAsync(readOptions, cancellationToken))
        {
            measurements.Add(ConvertToRoverMeasurement(feature));
        }

        return measurements;
    }

    public override async Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Reader not initialized. Call InitializeAsync first.");

        var readOptions = new ReadOptions(
            IncludeGeometry: true,
            WhereClause: $"sequence > {lastSequence}",
            OrderBy: "sequence ASC"
        );

        var measurements = new List<RoverMeasurement>();

        await foreach (var feature in _measurementsLayer.ReadFeaturesAsync(readOptions, cancellationToken))
        {
            measurements.Add(ConvertToRoverMeasurement(feature));
        }

        return measurements;
    }

    public override async Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Reader not initialized. Call InitializeAsync first.");

        var readOptions = new ReadOptions(
            IncludeGeometry: true,
            OrderBy: "sequence DESC",
            Limit: 1
        );

        await foreach (var feature in _measurementsLayer.ReadFeaturesAsync(readOptions, cancellationToken))
        {
            return ConvertToRoverMeasurement(feature);
        }

        return null;
    }

    private static RoverMeasurement ConvertToRoverMeasurement(FeatureRecord feature)
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

        return new RoverMeasurement(
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

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _measurementsLayer = null;
                _geoPackage?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}