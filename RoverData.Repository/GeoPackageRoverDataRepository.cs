using MapPiloteGeopackageHelper;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using System.Globalization;

namespace RoverData.Repository;

/// <summary>
/// GeoPackage repository with per-operation connection pattern.
/// Opens/closes GeoPackage file for each operation to avoid file locking.
/// Session context determines the file name (session_<name>.gpkg).
/// </summary>
public class GeoPackageRoverDataRepository : IRoverDataRepository
{
    private readonly string _dbPath;
    private readonly ISessionContext _sessionContext;
    private bool _disposed;

    public Guid SessionId => _sessionContext.SessionId;

    public GeoPackageRoverDataRepository(IOptions<GeoPackageRepositoryOptions> options, ISessionContext sessionContext)
    {
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        var opts = options.Value;
        var folderPath = string.IsNullOrEmpty(opts.FolderPath) 
            ? Environment.CurrentDirectory 
            : opts.FolderPath;
        _dbPath = Path.Combine(folderPath, $"session_{sessionContext.SessionName}.gpkg");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var folderPath = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(folderPath) && !Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        // Create the GeoPackage file and layer
        using var geoPackage = await GeoPackage.OpenAsync(_dbPath, 4326);
        
        var schema = new Dictionary<string, string>
        {
            ["rover_id"] = "TEXT NOT NULL",
            ["rover_name"] = "TEXT NOT NULL",
            ["session_id"] = "TEXT NOT NULL",
            ["sequence"] = "INTEGER NOT NULL",
            ["recorded_at"] = "TEXT NOT NULL",
            ["wind_direction_deg"] = "INTEGER NOT NULL",
            ["wind_speed_mps"] = "REAL NOT NULL"
        };

        await geoPackage.EnsureLayerAsync("rover_measurements", schema, 4326);
    }

    public async Task InsertAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default)
    {
        // Open connection per operation to avoid file locking
        using var geoPackage = await GeoPackage.OpenAsync(_dbPath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("rover_measurements", new Dictionary<string, string>(), 4326);

        var featureRecord = new FeatureRecord(
            measurement.Geometry,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["rover_id"] = measurement.RoverId.ToString(),
                ["rover_name"] = measurement.RoverName,
                ["session_id"] = measurement.SessionId.ToString(),
                ["sequence"] = measurement.Sequence.ToString(CultureInfo.InvariantCulture),
                ["recorded_at"] = measurement.RecordedAt.ToString("O"),
                ["wind_direction_deg"] = measurement.WindDirectionDeg.ToString(CultureInfo.InvariantCulture),
                ["wind_speed_mps"] = measurement.WindSpeedMps.ToString("F2", CultureInfo.InvariantCulture)
            }
        );

        await layer.BulkInsertAsync(
            new List<FeatureRecord> { featureRecord },
            new BulkInsertOptions(BatchSize: 1, CreateSpatialIndex: false, ConflictPolicy: ConflictPolicy.Ignore),
            null,
            cancellationToken);
    }

    public async Task<List<RoverMeasurement>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_dbPath))
            return new List<RoverMeasurement>();

        using var geoPackage = await GeoPackage.OpenAsync(_dbPath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("rover_measurements", new Dictionary<string, string>(), 4326);

        var readOptions = new ReadOptions(IncludeGeometry: true, OrderBy: "sequence ASC");
        var measurements = new List<RoverMeasurement>();

        await foreach (var feature in layer.ReadFeaturesAsync(readOptions, cancellationToken))
        {
            measurements.Add(ConvertToMeasurement(feature));
        }

        return measurements;
    }

    public async Task<List<RoverMeasurement>> GetNewSinceSequenceAsync(int lastSequence, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_dbPath))
            return new List<RoverMeasurement>();

        using var geoPackage = await GeoPackage.OpenAsync(_dbPath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("rover_measurements", new Dictionary<string, string>(), 4326);

        var readOptions = new ReadOptions(
            IncludeGeometry: true,
            WhereClause: $"sequence > {lastSequence}",
            OrderBy: "sequence ASC");

        var measurements = new List<RoverMeasurement>();
        await foreach (var feature in layer.ReadFeaturesAsync(readOptions, cancellationToken))
        {
            measurements.Add(ConvertToMeasurement(feature));
        }

        return measurements;
    }

    private static RoverMeasurement ConvertToMeasurement(FeatureRecord feature)
    {
        var roverId = Guid.Parse(feature.Attributes["rover_id"] ?? throw new InvalidDataException("Missing rover_id"));
        var roverName = feature.Attributes.ContainsKey("rover_name") ? feature.Attributes["rover_name"]! : "Unknown";
        var sessionId = Guid.Parse(feature.Attributes["session_id"] ?? throw new InvalidDataException("Missing session_id"));
        var sequence = int.Parse(feature.Attributes["sequence"] ?? "0", CultureInfo.InvariantCulture);
        var recordedAt = DateTimeOffset.Parse(feature.Attributes["recorded_at"] ?? throw new InvalidDataException("Missing recorded_at"));
        var windDirection = short.Parse(feature.Attributes["wind_direction_deg"] ?? "0", CultureInfo.InvariantCulture);
        var windSpeed = float.Parse(feature.Attributes["wind_speed_mps"] ?? "0", CultureInfo.InvariantCulture);

        if (feature.Geometry is not Point point)
            throw new InvalidDataException("Feature geometry is not a Point");

        return new RoverMeasurement(
            roverId,
            roverName,
            sessionId,
            sequence,
            recordedAt,
            windDirection,
            windSpeed,
            point
        );
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Nothing to dispose - connections are opened/closed per operation
            _disposed = true;
        }
    }
}
