using NetTopologySuite.Geometries;

namespace RoverData.Repository;

/// <summary>
/// Rover measurement record with spatial data (NTS Point geometry).
/// </summary>
public record RoverMeasurement(
    Guid RoverId,
    string RoverName,
    Guid SessionId,
    int Sequence,
    DateTimeOffset RecordedAt,
    short WindDirectionDeg,
    float WindSpeedMps,
    Point Geometry);
