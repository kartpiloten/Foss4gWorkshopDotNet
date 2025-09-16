using NetTopologySuite.Geometries;

namespace RoverSimulator;

public interface IRoverDataRepository : IDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ResetDatabaseAsync(CancellationToken cancellationToken = default);
    Task InsertMeasurementAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default);
}

public record RoverMeasurement(
    Guid SessionId,
    int Sequence,
    DateTimeOffset RecordedAt,
    double Latitude,
    double Longitude,
    short WindDirectionDeg,
    float WindSpeedMps,
    Point Geometry);