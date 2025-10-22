using NetTopologySuite.Geometries; // NuGet: NetTopologySuite provides GIS geometry types like Point (WGS84)

namespace ReadRoverDBStub;

/// <summary>
/// Read-only access to rover measurement data.
/// Notes:
/// - Async methods support CancellationToken (async/await).
/// - Geometry uses WGS84 (EPSG:4326), typical for FOSS4G tools (GeoPackage, PostGIS).
/// </summary>
public interface IRoverDataReader : IDisposable // IDisposable for releasing unmanaged resources (e.g., DB connections)
{
    /// <summary>
    /// Initializes the reader connection (async/await).
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets total count of measurements.
    /// </summary>
    Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all measurements with optional SQL-like filter (provider-specific).
    /// </summary>
    Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets measurements newer than the specified sequence number.
    /// </summary>
    Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the latest measurement (most recent sequence).
    /// </summary>
    Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable measurement record (C# record type).
/// - Geometry: NetTopologySuite Point (WGS84).
/// - Suitable for storage in GeoPackage (OGC) or PostGIS (FOSS4G).
/// </summary>
public record RoverMeasurement(
    Guid SessionId,
    int Sequence,
    DateTimeOffset RecordedAt,
    double Latitude,
    double Longitude,
    short WindDirectionDeg,
    float WindSpeedMps,
    Point Geometry);