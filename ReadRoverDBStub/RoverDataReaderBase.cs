namespace ReadRoverDBStub;

/// <summary>
/// Base class for rover data readers
/// </summary>
public abstract class RoverDataReaderBase : IRoverDataReader
{
    protected readonly string _connectionString;
    protected bool _disposed;

    protected RoverDataReaderBase(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public abstract Task InitializeAsync(CancellationToken cancellationToken = default);
    public abstract Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default);
    public abstract Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default);
    public abstract Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default);
    public abstract Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default);

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}