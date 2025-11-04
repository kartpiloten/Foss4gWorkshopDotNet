/*
 The functionallity in this file is:
 - Define a minimal async repository abstraction for storing rover measurements.
 - Implementations: GeoPackage (OGC, SQLite-based) and PostgreSQL/PostGIS (via Npgsql + NetTopologySuite).
 - Keep semantics consistent for easy swapping in the simulator and Blazor frontends.
 - NOW: Exposes SessionId property so the simulator can use the correct database-assigned session_id.
*/

using NetTopologySuite.Geometries; // NTS Point (WGS84/EPSG:4326) used for geometry

namespace RoverSimulator;

public interface IRoverDataRepository : IDisposable // IDisposable for releasing DB/file resources
{
    /// <summary>
    /// Gets the session ID for this repository (assigned by database or generated for GeoPackage).
    /// Available after InitializeAsync completes.
    /// </summary>
    Guid SessionId { get; }
    
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ResetDatabaseAsync(CancellationToken cancellationToken = default);
    Task InsertMeasurementAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default);
}

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