using NetTopologySuite.Geometries;

namespace ScentPolygonLibrary;

/// <summary>
/// Configuration for scent polygon calculation
/// </summary>
public class ScentPolygonConfiguration
{
    /// <summary>
    /// Radius in meters for omnidirectional detection around the dog
    /// </summary>
    public double OmnidirectionalRadiusMeters { get; init; } = 30.0;

    /// <summary>
    /// Number of points to use when creating the downwind fan polygon
    /// </summary>
    public int FanPolygonPoints { get; init; } = 15;

    /// <summary>
    /// Minimum distance multiplier for fan edges (prevents zero-width fans)
    /// </summary>
    public double MinimumDistanceMultiplier { get; init; } = 0.4;
}

/// <summary>
/// Represents a scent polygon with metadata
/// </summary>
public class ScentPolygonResult
{
    public Polygon Polygon { get; init; } = default!;
    public Guid SessionId { get; init; }
    public int Sequence { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double WindDirectionDeg { get; init; }
    public double WindSpeedMps { get; init; }
    public double ScentAreaM2 { get; init; }
    public double MaxDistanceM { get; init; }
    public bool IsValid => Polygon.IsValid;
}

/// <summary>
/// Represents a unified scent polygon combining multiple individual scent polygons
/// </summary>
public class UnifiedScentPolygon
{
    /// <summary>
    /// The combined polygon geometry representing the total coverage area
    /// </summary>
    public Polygon Polygon { get; init; } = default!;

    /// <summary>
    /// Number of individual polygons that were combined to create this unified polygon
    /// </summary>
    public int PolygonCount { get; init; }

    /// <summary>
    /// Total area of the unified polygon in square meters
    /// </summary>
    public double TotalAreaM2 { get; init; }

    /// <summary>
    /// Sum of the areas of all individual polygons before union (may be larger due to overlaps)
    /// </summary>
    public double IndividualAreasSum { get; init; }

    /// <summary>
    /// Time when the unified polygon was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Time range covered by the individual polygons
    /// </summary>
    public DateTimeOffset EarliestMeasurement { get; init; }

    /// <summary>
    /// Time range covered by the individual polygons
    /// </summary>
    public DateTimeOffset LatestMeasurement { get; init; }

    /// <summary>
    /// Average wind speed across all measurements
    /// </summary>
    public double AverageWindSpeedMps { get; init; }

    /// <summary>
    /// Range of wind speeds in the measurements
    /// </summary>
    public (double Min, double Max) WindSpeedRange { get; init; }

    /// <summary>
    /// Sessions included in this unified polygon
    /// </summary>
    public List<Guid> SessionIds { get; init; } = new();

    /// <summary>
    /// Number of vertices in the unified polygon (complexity indicator)
    /// </summary>
    public int VertexCount => Polygon.NumPoints;

    /// <summary>
    /// Indicates whether the unified polygon geometry is valid
    /// </summary>
    public bool IsValid => Polygon.IsValid;

    /// <summary>
    /// Coverage efficiency: ratio of unified area to sum of individual areas
    /// Higher values indicate more overlap between individual polygons
    /// </summary>
    public double CoverageEfficiency => IndividualAreasSum > 0 ? TotalAreaM2 / IndividualAreasSum : 0;
}

/// <summary>
/// Event arguments for scent polygon updates
/// </summary>
public class ScentPolygonUpdateEventArgs : EventArgs
{
    public List<ScentPolygonResult> NewPolygons { get; init; } = new();
    public int TotalPolygonCount { get; init; }
    public ScentPolygonResult? LatestPolygon { get; init; }
    public DateTimeOffset UpdateTime { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for scent polygon status updates
/// </summary>
public class ScentPolygonStatusEventArgs : EventArgs
{
    public int TotalPolygonCount { get; init; }
    public ScentPolygonResult? LatestPolygon { get; init; }
    public DateTimeOffset StatusTime { get; init; } = DateTimeOffset.UtcNow;
    public string DataSource { get; init; } = string.Empty;
}