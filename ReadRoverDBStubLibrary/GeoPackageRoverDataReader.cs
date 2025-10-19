using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using System.Globalization;

namespace ReadRoverDBStubLibrary;

/// <summary>
/// GeoPackage rover data reader (silent library version)
/// </summary>
public class GeoPackageRoverDataReader : RoverDataReaderBase
{
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

        if (!File.Exists(_dbPath))
        {
            throw new FileNotFoundException($"GeoPackage file not found: {_dbPath}");
        }
        
        // Just verify the file exists, don't keep it open
    }

    private async Task<(GeoPackage geoPackage, GeoPackageLayer layer)> OpenGeoPackageAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_dbPath))
            throw new InvalidOperationException("Database path not specified");

        var geoPackage = await GeoPackage.OpenAsync(_dbPath, 4326);
        var layer = await geoPackage.EnsureLayerAsync(LayerName, new Dictionary<string, string>(), 4326);
        return (geoPackage, layer);
    }

    public override async Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default)
    {
        var (geoPackage, layer) = await OpenGeoPackageAsync(cancellationToken);
        try
        {
            return await layer.CountAsync();
        }
        finally
        {
            geoPackage.Dispose();
        }
    }

    public override async Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default)
    {
        var (geoPackage, layer) = await OpenGeoPackageAsync(cancellationToken);
        try
        {
            var readOptions = new ReadOptions(
                IncludeGeometry: true,
                WhereClause: whereClause,
                OrderBy: "sequence ASC"
            );

            var measurements = new List<RoverMeasurement>();

            await foreach (var feature in layer.ReadFeaturesAsync(readOptions, cancellationToken))
            {
                measurements.Add(ConvertToRoverMeasurement(feature));
            }

            return measurements;
        }
        finally
        {
            geoPackage.Dispose();
        }
    }

    public override async Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default)
    {
        var (geoPackage, layer) = await OpenGeoPackageAsync(cancellationToken);
        try
        {
            var readOptions = new ReadOptions(
                IncludeGeometry: true,
                WhereClause: $"sequence > {lastSequence}",
                OrderBy: "sequence ASC"
            );

            var measurements = new List<RoverMeasurement>();

            await foreach (var feature in layer.ReadFeaturesAsync(readOptions, cancellationToken))
            {
                measurements.Add(ConvertToRoverMeasurement(feature));
            }

            return measurements;
        }
        finally
        {
            geoPackage.Dispose();
        }
    }

    public override async Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default)
    {
        var (geoPackage, layer) = await OpenGeoPackageAsync(cancellationToken);
        try
        {
            var readOptions = new ReadOptions(
                IncludeGeometry: true,
                OrderBy: "sequence DESC",
                Limit: 1
            );

            await foreach (var feature in layer.ReadFeaturesAsync(readOptions, cancellationToken))
            {
                return ConvertToRoverMeasurement(feature);
            }

            return null;
        }
        finally
        {
            geoPackage.Dispose();
        }
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
                // No resources to dispose anymore since we open/close on each operation
            }
            base.Dispose(disposing);
        }
    }
}