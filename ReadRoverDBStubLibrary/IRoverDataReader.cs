using NetTopologySuite.Geometries;

namespace ReadRoverDBStubLibrary;

/// <summary>
/// Interface for rover data readers - read-only access to rover measurement data
/// </summary>
public interface IRoverDataReader : IDisposable
{
    /// <summary>
    /// Initializes the reader connection
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the total count of measurements in the database
    /// </summary>
    Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all measurements with optional filtering
    /// </summary>
    Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets measurements newer than the specified sequence number
    /// </summary>
    Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the latest measurement
    /// </summary>
    Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Rover measurement data record - shared with RoverSimulator
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