/*
 The functionallity in this file is:
 - Provide a simple "Null Object" implementation of IRoverDataReader so apps can run even when no data source is configured.
 - Useful in workshops and during UI/DI wiring (e.g., Blazor) to keep the app functional without a database.
 - Methods are async-friendly (Task/Task<T>) and accept CancellationToken but return empty results/null without throwing or logging.
*/

namespace ReadRoverDBStubLibrary;

/// <summary>
/// Null rover data reader for when no data is available (silent library version).
/// Notes:
/// - Async-friendly no-ops; keeps samples simple and predictable.
/// - Handy default for Dependency Injection registrations.
/// </summary>
public class NullRoverDataReader : IRoverDataReader
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; // no-op init
    public Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L); // empty data
    public Task<List<RoverMeasurement>> GetAllMeasurementsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new List<RoverMeasurement>()); // empty list
    public Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<RoverMeasurement>()); // empty list
    public Task<List<RoverMeasurement>> GetNewMeasurementsSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<RoverMeasurement>()); // empty list
    public Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<RoverMeasurement?>(null); // no latest measurement
    public void Dispose() { } // nothing to dispose
}