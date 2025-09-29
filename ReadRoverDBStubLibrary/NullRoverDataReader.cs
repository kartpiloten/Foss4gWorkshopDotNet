namespace ReadRoverDBStubLibrary;

/// <summary>
/// Null rover data reader for when no data is available (silent library version)
/// </summary>
public class NullRoverDataReader : IRoverDataReader
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
    public Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default) 
        => Task.FromResult(new List<RoverMeasurement>());
    public Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<RoverMeasurement>());
    public Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<RoverMeasurement?>(null);
    public void Dispose() { }
}