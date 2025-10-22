/*
 The functionallity in this file is:
 - Provide an abstract base for repositories with a shared connection string and dispose pattern.
 - Ensure all implementations support async Initialize/Reset/Insert operations.
 - Keep the base minimal to focus on learning the concrete providers.
*/

using NetTopologySuite.Geometries;

namespace RoverSimulator;

public abstract class RoverDataRepositoryBase : IRoverDataRepository
{
    protected readonly string _connectionString;
    protected bool _disposed = false;

    protected RoverDataRepositoryBase(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public abstract Task InitializeAsync(CancellationToken cancellationToken = default);
    public abstract Task ResetDatabaseAsync(CancellationToken cancellationToken = default);
    public abstract Task InsertMeasurementAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default);

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources (if any in derived types)
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}