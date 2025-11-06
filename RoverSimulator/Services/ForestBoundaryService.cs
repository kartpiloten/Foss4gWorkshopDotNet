/*
 The functionallity in this file is:
 - Load the forest polygon from an OGC GeoPackage and expose centroid/bounds helpers.
 - Provide simple Contains checks (NetTopologySuite) for rover boundary logic.
 - Fall back to configured bounds when the GeoPackage is unavailable.
*/

using MapPiloteGeopackageHelper; // GeoPackage helper (async APIs)
using NetTopologySuite.Geometries; // NTS Polygon/Point/Envelope
using RoverSimulator.Configuration;

namespace RoverSimulator.Services;

/// <summary>
/// Service responsible for handling forest boundary geometry.
/// </summary>
public class ForestBoundaryService
{
    private readonly ForestSettings _settings;
    private Polygon? _forestBoundary;
    private Envelope? _boundingBox;

    public ForestBoundaryService(ForestSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Load forest boundary polygon from GeoPackage (EPSG:4326).
    /// </summary>
    public async Task<Polygon> GetForestBoundaryAsync()
    {
        if (_forestBoundary != null)
            return _forestBoundary;

        try
        {
            var geoPackagePath = Path.Combine(GetSolutionDirectory(), _settings.BoundaryFile);

            if (!File.Exists(geoPackagePath))
            {
                throw new FileNotFoundException($"Forest boundary file not found at: {geoPackagePath}");
            }

            Console.WriteLine($"Loading forest boundary from: {geoPackagePath}");

            using var geoPackage = await GeoPackage.OpenAsync(geoPackagePath, 4326);
            var layer = await geoPackage.EnsureLayerAsync(_settings.LayerName, new Dictionary<string, string>(), 4326);

            var readOptions = new ReadOptions(IncludeGeometry: true, Limit: 1);

            await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
            {
                if (feature.Geometry is Polygon polygon)
                {
                    _forestBoundary = polygon;
                    _boundingBox = polygon.EnvelopeInternal;

                    Console.WriteLine($"Forest boundary loaded successfully:");
                    Console.WriteLine($"  Bounding box: ({_boundingBox.MinX:F6}, {_boundingBox.MinY:F6}) to ({_boundingBox.MaxX:F6}, {_boundingBox.MaxY:F6})");
                    Console.WriteLine($"  Centroid: ({polygon.Centroid.X:F6}, {polygon.Centroid.Y:F6})");

                    return _forestBoundary;
                }
            }

            throw new InvalidDataException($"No polygon geometry found in the {_settings.LayerName} layer");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading forest boundary: {ex.Message}");
            Console.WriteLine("Falling back to configured bounds...");

            _forestBoundary = CreateFallbackPolygon();
            _boundingBox = _forestBoundary.EnvelopeInternal;

            return _forestBoundary;
        }
    }

    public async Task<bool> IsPointInForestAsync(double latitude, double longitude)
    {
        var boundary = await GetForestBoundaryAsync();
        var point = new Point(longitude, latitude) { SRID = 4326 };
        return boundary.Contains(point);
    }

    public async Task<Envelope> GetBoundingBoxAsync()
    {
        if (_boundingBox != null)
            return _boundingBox;

        var boundary = await GetForestBoundaryAsync();
        return _boundingBox ?? boundary.EnvelopeInternal;
    }

    public async Task<Point> GetForestCentroidAsync()
    {
        try
        {
            var boundary = await GetForestBoundaryAsync();
            return boundary.Centroid;
        }
        catch
        {
            return new Point(_settings.FallbackBounds.MinLongitude +
                           (_settings.FallbackBounds.MaxLongitude - _settings.FallbackBounds.MinLongitude) / 2,
                           _settings.FallbackBounds.MinLatitude +
                           (_settings.FallbackBounds.MaxLatitude - _settings.FallbackBounds.MinLatitude) / 2)
            { SRID = 4326 };
        }
    }

    private Polygon CreateFallbackPolygon()
    {
        var bounds = _settings.FallbackBounds;
        var coordinates = new[]
        {
            new Coordinate(bounds.MinLongitude, bounds.MinLatitude),
            new Coordinate(bounds.MaxLongitude, bounds.MinLatitude),
            new Coordinate(bounds.MaxLongitude, bounds.MaxLatitude),
            new Coordinate(bounds.MinLongitude, bounds.MaxLatitude),
            new Coordinate(bounds.MinLongitude, bounds.MinLatitude)
        };

        var linearRing = new LinearRing(coordinates);
        return new Polygon(linearRing) { SRID = 4326 };
    }

    private static string GetSolutionDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var directory = new DirectoryInfo(currentDir);

        while (directory != null && !directory.GetFiles("*.sln").Any())
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? currentDir;
    }
}