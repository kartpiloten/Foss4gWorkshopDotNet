using Microsoft.Extensions.Caching.Memory;
using RoverData.Repository;
using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;

namespace ScentPolygonLibrary;

/// <summary>
/// On-demand generator for scent polygons with memory caching.
/// Calculates unified polygon only when requested and caches result for 1 second.
/// </summary>
public class ScentPolygonGenerator
{
    private readonly IRoverDataRepository _dataRepository;
    private readonly ScentPolygonConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly string? _forestGeoPackagePath;

    // Cache keys
    private const string UnifiedPolygonCacheKey = "unified-polygon";
    private const string ForestIntersectionCacheKey = "forest-intersection";

    public ScentPolygonGenerator(
        IRoverDataRepository dataRepository,
        IMemoryCache cache,
        ScentPolygonConfiguration? configuration = null,
        string? forestGeoPackagePath = null)
    {
        _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _configuration = configuration ?? new ScentPolygonConfiguration();
        _forestGeoPackagePath = forestGeoPackagePath;
    }

    /// <summary>
    /// Gets the unified scent polygon (cached for 1 second).
    /// </summary>
    public async Task<UnifiedScentPolygon?> GetUnifiedPolygonAsync(CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(UnifiedPolygonCacheKey, async entry =>
        {
            // Cache expires after 1 second
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
            
            // Fetch all measurements from repository
            var measurements = await _dataRepository.GetAllAsync(cancellationToken);
            
            if (!measurements.Any())
                return null;

            // Generate individual polygons for each measurement
            var polygons = measurements.Select(m => GeneratePolygonForMeasurement(m)).ToList();

            // Create unified polygon using calculator
            return ScentPolygonCalculator.CreateUnifiedPolygon(polygons);
        });
    }

    /// <summary>
    /// Gets rover-specific unified polygons (cached for 1 second).
    /// Groups measurements by rover and creates a unified polygon per rover.
    /// </summary>
    public async Task<List<RoverUnifiedPolygon>> GetRoverUnifiedPolygonsAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = "rover-unified-polygons";
        
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
            
            var measurements = await _dataRepository.GetAllAsync(cancellationToken);
            
            if (!measurements.Any())
                return new List<RoverUnifiedPolygon>();

            // Group by rover
            var roverGroups = measurements.GroupBy(m => new { m.RoverId, m.RoverName });
            
            var roverPolygons = new List<RoverUnifiedPolygon>();
            
            foreach (var group in roverGroups)
            {
                var polygons = group.Select(m => GeneratePolygonForMeasurement(m)).ToList();
                var unified = ScentPolygonCalculator.CreateUnifiedPolygon(polygons);
                
                if (unified != null && unified.IsValid)
                {
                    roverPolygons.Add(new RoverUnifiedPolygon
                    {
                        RoverId = group.Key.RoverId,
                        RoverName = group.Key.RoverName,
                        UnifiedPolygon = unified.Polygon,
                        PolygonCount = unified.PolygonCount,
                        TotalAreaM2 = unified.TotalAreaM2
                    });
                }
            }
            
            return roverPolygons;
        }) ?? new List<RoverUnifiedPolygon>();
    }

    /// <summary>
    /// Calculates forest intersection areas (cached for 1 second).
    /// </summary>
    public async Task<(int searchedAreaM2, int forestAreaM2)> GetForestIntersectionAreasAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_forestGeoPackagePath) || !File.Exists(_forestGeoPackagePath))
            return (0, 0);

        return await _cache.GetOrCreateAsync(ForestIntersectionCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
            
            var unified = await GetUnifiedPolygonAsync(cancellationToken);
            if (unified == null || !unified.IsValid)
                return (0, 0);

            using var gp = await GeoPackage.OpenAsync(_forestGeoPackagePath, 4326);
            var layer = await gp.EnsureLayerAsync("riverheadforest", new Dictionary<string, string>(), 4326);

            await foreach (var feature in layer.ReadFeaturesAsync(new ReadOptions(IncludeGeometry: true, Limit: 1)))
            {
                if (feature.Geometry is Polygon forest)
                {
                    // Convert degrees² to m² using latitude scale
                    var avgLat = forest.Centroid.Y;
                    var metersPerDegLat = 111_320.0;
                    var metersPerDegLon = 111_320.0 * Math.Cos(avgLat * Math.PI / 180.0);

                    var forestArea = forest.Area * metersPerDegLat * metersPerDegLon;
                    var searchedArea = unified.Polygon.Area * metersPerDegLat * metersPerDegLon;

                    return ((int)Math.Max(0, Math.Round(searchedArea)), (int)Math.Max(0, Math.Round(forestArea)));
                }
            }
            
            return (0, 0);
        });
    }

    /// <summary>
    /// Generates a scent polygon for a specific measurement.
    /// </summary>
    private ScentPolygonResult GeneratePolygonForMeasurement(RoverMeasurement measurement)
    {
        var polygon = ScentPolygonCalculator.CreateScentPolygon(
            measurement.Geometry.Centroid.Y,
            measurement.Geometry.Centroid.X,
            measurement.WindDirectionDeg,
            measurement.WindSpeedMps,
            _configuration);

        var scentArea = ScentPolygonCalculator.CalculateScentAreaM2(polygon, measurement.Geometry.Centroid.Y);

        return new ScentPolygonResult
        {
            Polygon = polygon,
            RoverId = measurement.RoverId,
            RoverName = measurement.RoverName,
            SessionId = measurement.SessionId,
            Sequence = measurement.Sequence,
            WindDirectionDeg = measurement.WindDirectionDeg,
            WindSpeedMps = measurement.WindSpeedMps,
            ScentAreaM2 = scentArea
        };
    }

    /// <summary>
    /// Clears all cached data (useful for testing or manual refresh).
    /// </summary>
    public void ClearCache()
    {
        _cache.Remove(UnifiedPolygonCacheKey);
        _cache.Remove(ForestIntersectionCacheKey);
        _cache.Remove("rover-unified-polygons");
    }
}
