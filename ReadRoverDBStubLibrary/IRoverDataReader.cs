/*
 The functionallity in this file is:
 - Defines a small, async, read-only abstraction for rover measurements so apps (e.g., Blazor) can swap backends.
 - Implementations include Postgres/PostGIS (Npgsql + NetTopologySuite) and OGC GeoPackage.
 - Geometry uses NetTopologySuite types for interoperability with FOSS4G tooling.
*/

using NetTopologySuite.Geometries; // NuGet: NetTopologySuite provides GIS geometry types like Point (WGS84/EPSG:4326)

namespace ReadRoverDBStubLibrary;

/// <summary>
/// Read-only access to rover measurement data.
/// Notes:
/// - Async methods use CancellationToken (async/await) to avoid blocking.
/// - IDisposable to allow implementations to free DB/file resources.
/// - Geometry is an NTS Point (commonly WGS84/EPSG:4326, PostGIS/GeoPackage friendly).
/// </summary>
public interface IRoverDataReader : IDisposable
{
    /// <summary>
    /// Initializes the reader connection (non-blocking async).
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Returns total number of measurements.
    /// </summary>
    Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Returns all measurements, optionally filtered (provider-specific whereClause).
    /// </summary>
    Task<List<RoverMeasurement>> GetAllMeasurementsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Returns measurements with Sequence greater than lastSequence.
    /// </summary>
    Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Returns the most recent measurement or null if none.
    /// </summary>
    Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable measurement record (C# record).
/// Notes:
/// - Geometry is NetTopologySuite Point (WGS84/EPSG:4326), interoperable with PostGIS/GeoPackage.
/// - Suitable for display in Blazor and for GIS export (GeoJSON, etc.).
/// </summary>
public record RoverMeasurement(
    Guid RoverId,
    string RoverName,
    Guid SessionId,
    int Sequence,
    DateTimeOffset RecordedAt,
    double Latitude,
    double Longitude,
    short WindDirectionDeg,
    float WindSpeedMps,
    Point Geometry);