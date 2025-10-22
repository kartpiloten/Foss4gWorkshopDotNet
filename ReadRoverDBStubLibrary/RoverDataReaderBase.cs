/*
 The functionallity in this file is:
 - Provides a minimal abstract base for rover data readers so apps (e.g., Blazor) can swap backends via IRoverDataReader.
 - Concrete implementations: PostgresRoverDataReader (Npgsql + NetTopologySuite/PostGIS) and GeoPackageRoverDataReader (OGC GeoPackage).
 - Centralizes shared concerns (connection string handling, dispose pattern) while keeping the sample simple.
*/

namespace ReadRoverDBStubLibrary;

/// <summary>
/// Base class for rover data readers.
/// Notes:
/// - Async methods (see abstract members) use CancellationToken for cooperative cancellation.
/// - IDisposable implemented via the protected Dispose(bool) pattern (library remains silent).
/// </summary>
public abstract class RoverDataReaderBase : IRoverDataReader
{
    // Connection string or path depending on provider:
    // - Postgres: standard Npgsql connection string (FOSS4G: PostGIS)
    // - GeoPackage: folder path or .gpkg file path (OGC GeoPackage on SQLite)
    protected readonly string _connectionString;

    protected bool _disposed;

    protected RoverDataReaderBase(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    // Async initialization; implementations should avoid blocking and honor CancellationToken.
    public abstract Task InitializeAsync(CancellationToken cancellationToken = default);

    // Basic read operations (async/await). Keep semantics consistent across providers.
    public abstract Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default);
    public abstract Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default);
    public abstract Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default);
    public abstract Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default);

    // Minimal dispose pattern; concrete readers dispose their own resources.
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