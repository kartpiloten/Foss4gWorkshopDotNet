using NetTopologySuite.Geometries;
using RoverData.Repository;

namespace ComponentTester;

/// <summary>
/// Stub repository for testing components without actual database.
/// Returns empty data or minimal test data.
/// </summary>
public class StubRoverDataRepository : IRoverDataRepository
{
    private readonly Guid _sessionId;

    public Guid SessionId => _sessionId;

    public StubRoverDataRepository()
    {
        _sessionId = Guid.NewGuid();
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
{
        return Task.CompletedTask;
    }

    public Task InsertAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default)
    {
      // Stub - does nothing
  return Task.CompletedTask;
    }

  public Task<List<RoverMeasurement>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // Return empty list for testing
      return Task.FromResult(new List<RoverMeasurement>());
 }

    public Task<List<RoverMeasurement>> GetNewSinceSequenceAsync(int lastSequence, CancellationToken cancellationToken = default)
 {
        // Return empty list for testing
        return Task.FromResult(new List<RoverMeasurement>());
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
