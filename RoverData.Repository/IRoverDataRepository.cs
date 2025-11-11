namespace RoverData.Repository;

/// <summary>
/// Information about a rover session including measurement count.
/// </summary>
public record SessionInfo(string SessionName, long MeasurementCount);

/// <summary>
/// Unified repository interface for rover measurement data (read and write operations).
/// </summary>
public interface IRoverDataRepository : IDisposable
{
    /// <summary>
    /// Gets the session ID for this repository instance.
    /// </summary>
    Guid SessionId { get; }

    /// <summary>
    /// Initializes the repository (creates tables, registers session, etc.).
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a single measurement.
    /// </summary>
    Task InsertAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all measurements for the current session, ordered by sequence.
    /// </summary>
    Task<List<RoverMeasurement>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets measurements with sequence greater than the specified value.
    /// </summary>
    Task<List<RoverMeasurement>> GetNewSinceSequenceAsync(int lastSequence, CancellationToken cancellationToken = default);
}
