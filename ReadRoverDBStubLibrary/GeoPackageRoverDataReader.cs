/*
 The functionallity in this file is:
 - Implement a GeoPackage reader using MapPiloteGeopackageHelper and NetTopologySuite (NTS Point geometry).
 - Provide async read operations (count, list, new since seq, latest) while opening/disposing per operation.
 - Keep the sample simple and silent; suitable for FOSS4G workflows (OGC GeoPackage, EPSG:4326/WGS84).
*/

using MapPiloteGeopackageHelper; // NuGet: helpers for reading/writing OGC GeoPackage with async APIs
using NetTopologySuite.Geometries; // NuGet: NetTopologySuite geometry types (e.g., Point) used by FOSS4G tools
using System.Globalization; // For culture-invariant numeric/date parsing

namespace ReadRoverDBStubLibrary;

/// <summary>
/// GeoPackage rover data reader (silent library version).
/// Notes:
/// - GeoPackage is an OGC FOSS4G standard (SQLite-based) commonly used for spatial data exchange.
/// - Uses async/await to avoid blocking; opens the file per operation to keep the sample simple.
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

    public override Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_dbPath))
            throw new InvalidOperationException("Database path not specified");

        if (!File.Exists(_dbPath))
        {
            throw new FileNotFoundException($"GeoPackage file not found: {_dbPath}");
        }

        // Just verify the file exists; do not keep it open (keeps the sample minimal)
        return Task.CompletedTask;
    }

    private async Task<(GeoPackage geoPackage, GeoPackageLayer layer)> OpenGeoPackageAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_dbPath))
            throw new InvalidOperationException("Database path not specified");

        // EPSG:4326 (WGS84) is the common coordinate reference system used by FOSS4G tools
        var geoPackage = await GeoPackage.OpenAsync(_dbPath, 4326);
        // Ensure the layer reference (creates if missing) to keep the workshop flow smooth
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
            geoPackage.Dispose(); // Dispose after each operation to avoid keeping the file locked
        }
    }

    public override async Task<List<RoverMeasurement>> GetAllMeasurementsAsync(CancellationToken cancellationToken = default)
    {
        var (geoPackage, layer) = await OpenGeoPackageAsync(cancellationToken);
        try
        {
            var readOptions = new ReadOptions(
                IncludeGeometry: true,
                WhereClause: null,
                OrderBy: "sequence ASC"
            );

            var measurements = new List<RoverMeasurement>();

            // await foreach consumes IAsyncEnumerable from the GeoPackage layer (async stream)
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

    public override async Task<List<RoverMeasurement>> GetNewMeasurementsSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
    {
        var (geoPackage, layer) = await OpenGeoPackageAsync(cancellationToken);
        try
        {
            var iso = sinceUtc.ToString("O");
            var readOptions = new ReadOptions(
                IncludeGeometry: true,
                WhereClause: $"recorded_at > '{iso}'",
                OrderBy: "recorded_at ASC"
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
        // Attributes are strings; parse using InvariantCulture for consistent numeric formats
        var roverId = Guid.Parse(feature.Attributes.ContainsKey("rover_id") ? feature.Attributes["rover_id"]! : Guid.Empty.ToString());
        var roverName = feature.Attributes.ContainsKey("rover_name") ? feature.Attributes["rover_name"]! : "Unknown";
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
            throw new InvalidDataException("Feature geometry is not a Point"); // NTS Point required

        return new RoverMeasurement(
            roverId,
            roverName,
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
                // Nothing to dispose: GeoPackage is opened/closed per operation to keep sample simple
            }
            base.Dispose(disposing);
        }
    }
}