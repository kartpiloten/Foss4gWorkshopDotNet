/*
 The functionallity in this file is:
 - Load and cache a forest boundary polygon from an OGC GeoPackage file.
 - Provide simple Contains checks and geometry access for boundary validation.
 - Keep the implementation minimal and silent (library-level component).
*/

using MapPiloteGeopackageHelper; // NuGet: GeoPackage helper (OGC standard, async APIs)
using NetTopologySuite.Geometries; // NuGet: NTS for FOSS4G geometry types (e.g., Polygon, Point)

namespace RoverData.Repository;

/// <summary>
/// Reads and caches a forest boundary polygon from a GeoPackage file.
/// Notes:
/// - Loads once on first access and caches the result.
/// - Useful for validating rover positions against operational boundaries.
/// - Uses NetTopologySuite Polygon (EPSG:4326) for spatial operations.
/// </summary>
public class ForestBoundaryReader : IDisposable
{
    private readonly string _geoPackagePath;
    private readonly string _layerName;
    private Polygon? _boundaryPolygon;
    private Envelope? _boundingBox;
    private bool _disposed;

    /// <summary>
    /// Creates a forest boundary reader for the specified GeoPackage file.
    /// </summary>
    /// <param name="geoPackagePath">Path to the GeoPackage file containing the forest boundary</param>
    /// <param name="layerName">Name of the layer containing the boundary polygon (default: "riverheadforest")</param>
    public ForestBoundaryReader(string geoPackagePath, string layerName = "riverheadforest")
    {
        _geoPackagePath = geoPackagePath ?? throw new ArgumentNullException(nameof(geoPackagePath));
        _layerName = layerName ?? throw new ArgumentNullException(nameof(layerName));
    }

    /// <summary>
    /// Gets the boundary polygon, loading it from the GeoPackage if not already cached.
    /// </summary>
    public async Task<Polygon?> GetBoundaryPolygonAsync(CancellationToken cancellationToken = default)
    {
        if (_boundaryPolygon != null)
            return _boundaryPolygon;

        if (!File.Exists(_geoPackagePath))
            return null;

        try
        {
            using var geoPackage = await GeoPackage.OpenAsync(_geoPackagePath, 4326, cancellationToken);
            var layer = await geoPackage.EnsureLayerAsync(_layerName, new Dictionary<string, string>(), 4326);

            var readOptions = new ReadOptions(IncludeGeometry: true, Limit: 1);

            await foreach (var feature in layer.ReadFeaturesAsync(readOptions, cancellationToken))
            {
                if (feature.Geometry is Polygon polygon)
                {
                    _boundaryPolygon = polygon;
                    _boundingBox = polygon.EnvelopeInternal;
                    return _boundaryPolygon;
                }
            }
        }
        catch
        {
            // Silent failure - returns null
        }

        return null;
    }

    /// <summary>
    /// Gets the bounding box of the forest boundary.
    /// </summary>
    public async Task<Envelope?> GetBoundingBoxAsync(CancellationToken cancellationToken = default)
    {
        if (_boundingBox != null)
            return _boundingBox;

        var boundary = await GetBoundaryPolygonAsync(cancellationToken);
        return boundary?.EnvelopeInternal;
    }

    /// <summary>
    /// Gets the centroid of the forest boundary.
    /// </summary>
    public async Task<Point?> GetCentroidAsync(CancellationToken cancellationToken = default)
    {
        var boundary = await GetBoundaryPolygonAsync(cancellationToken);
        return boundary?.Centroid;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _boundaryPolygon = null;
            _boundingBox = null;
            _disposed = true;
        }
    }
}
