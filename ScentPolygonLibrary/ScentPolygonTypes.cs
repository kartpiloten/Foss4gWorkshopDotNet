/*
 The functionallity in this file is:
 - Define minimal types used by the scent polygon library and frontends.
 - Keep properties focused on learning needs; avoid extra fields to reduce complexity.
 - Mention where external packages are involved (NTS for geometry).
*/

using NetTopologySuite.Geometries; // NTS: geometry types (Polygon)

namespace ScentPolygonLibrary;

/// <summary>
/// Configuration for scent polygon calculation (simple model)
/// </summary>
public class ScentPolygonConfiguration
{
    /// <summary>
    /// Radius in meters for omnidirectional detection around the dog
    /// </summary>
    public double OmnidirectionalRadiusMeters { get; init; } = 30.0;

    /// <summary>
    /// Number of points used when creating the upwind fan polygon (resolution)
    /// </summary>
    public int FanPolygonPoints { get; init; } = 15;

    /// <summary>
    /// Minimum distance multiplier for fan edges (prevents zero-width fans)
    /// </summary>
    public double MinimumDistanceMultiplier { get; init; } = 0.4;
}

/// <summary>
/// Represents a scent polygon with metadata for the originating rover measurement
/// </summary>
public class ScentPolygonResult
{
    public Polygon Polygon { get; init; } = default!; // NTS Polygon (EPSG:4326)

    /// <summary>
    /// Unique identifier for the rover that produced this measurement
    /// </summary>
    public Guid RoverId { get; init; }

    /// <summary>
  /// Name of the rover dog
  /// </summary>
    public string RoverName { get; init; } = string.Empty;

    public Guid SessionId { get; init; }
    public int Sequence { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
  public double WindDirectionDeg { get; init; }
    public double WindSpeedMps { get; init; }
    public double ScentAreaM2 { get; init; }

    // Convenience validity flag from NTS
  public bool IsValid => Polygon.IsValid;
}

/// <summary>
/// Represents a unified scent polygon combining multiple individual scent polygons
/// </summary>
public class UnifiedScentPolygon
{
    /// <summary>
    /// The combined polygon geometry representing the total coverage area (NTS Polygon)
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
    /// Rovers included in this unified polygon
    /// </summary>
    public List<Guid> RoverIds { get; init; } = new();

    /// <summary>
    /// Rover names included in this unified polygon
  /// </summary>
    public List<string> RoverNames { get; init; } = new();

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

    /// <summary>
/// Number of distinct rovers represented
    /// </summary>
    public int RoverCount => RoverIds.Count;
}

/// <summary>
/// Event arguments for scent polygon updates (used by console/Blazor front-ends)
/// </summary>
public class ScentPolygonUpdateEventArgs : EventArgs
{
    public List<ScentPolygonResult> NewPolygons { get; init; } = new();
    public int TotalPolygonCount { get; init; }
    public ScentPolygonResult? LatestPolygon { get; init; }

    /// <summary>
    /// Rovers that contributed new polygons in this update (for UI filtering/grouping)
    /// </summary>
    public List<Guid> AffectedRoverIds { get; init; } = new();
}

/// <summary>
/// Event arguments for periodic status updates (lightweight)
/// </summary>
public class ScentPolygonStatusEventArgs : EventArgs
{
    public int TotalPolygonCount { get; init; }
    public ScentPolygonResult? LatestPolygon { get; init; }
    public string DataSource { get; init; } = string.Empty;

    /// <summary>
    /// Number of rovers currently producing data (as known to the host)
    /// </summary>
    public int ActiveRoverCount { get; init; }
}

/// <summary>
/// Event arguments for forest coverage updates (real-time UI updates)
/// </summary>
public class ForestCoverageEventArgs : EventArgs
{
    public int SearchedAreaM2 { get; init; }
    public int ForestAreaM2 { get; init; }
    public double PercentageSearched => ForestAreaM2 > 0 ? (SearchedAreaM2 / (double)ForestAreaM2) * 100.0 : 0;
}