/*
 The functionallity in this file is:
 - Provide minimal helpers to build scent polygons from rover measurements and combine them.
 - Keep algorithms simple for learning: a fan-shaped upwind cone unioned with a small circular buffer.
 - Use NetTopologySuite (NTS) basic operations with small comments (FOSS4G/geometry basics).
*/

using NetTopologySuite.Geometries; // NTS: geometry types and operations (Polygon, MultiPolygon, Union)

namespace ScentPolygonLibrary;

/// <summary>
/// Utility class for creating scent detection polygons from rover measurements
/// </summary>
public static class ScentPolygonCalculator
{
    private static readonly GeometryFactory DefaultGeometryFactory = new(new PrecisionModel(), 4326); // EPSG:4326 (WGS84)

    /// <summary>
    /// Creates a scent polygon representing upwind scent detection and a small omnidirectional buffer around the dog.
    /// For dog scent detection: wind carries scents FROM upwind sources TO the dog, so the cone points UPWIND.
    /// </summary>
    public static Polygon CreateScentPolygon(
        double latitude,
        double longitude,
        double windDirectionDeg,
        double windSpeedMps,
        ScentPolygonConfiguration? config = null,
        GeometryFactory? geometryFactory = null)
    {
        config ??= new ScentPolygonConfiguration();
        geometryFactory ??= DefaultGeometryFactory;

        // Simple distance/scale math (approx) for local area near the given latitude
        var metersPerDegLat = 111_320.0;
        var metersPerDegLon = 111_320.0 * Math.Cos(latitude * Math.PI / 180.0);

        // Maximum range scales with wind speed (simple model)
        var maxDistance = CalculateMaxScentDistance(windSpeedMps);

        // Wind direction (degrees-from) converted to radians. Upwind cone uses direction as-is.
        var windRadians = (windDirectionDeg % 360) * Math.PI / 180.0;

        // Center point
        var dogPoint = geometryFactory.CreatePoint(new Coordinate(longitude, latitude));
        dogPoint.SRID = 4326;

        // Build a simple upwind fan (triangle fan) with configurable resolution
        var fanCoords = new List<Coordinate> { new(longitude, latitude) };
        var fanHalfAngle = CalculateFanAngle(windSpeedMps);
        for (int i = 0; i <= config.FanPolygonPoints; i++)
        {
            var angle = windRadians - fanHalfAngle + (2 * fanHalfAngle * i / config.FanPolygonPoints);
            var angleFromCenter = Math.Abs(angle - windRadians);
            var distanceMultiplier = Math.Cos(angleFromCenter);
            var distance = maxDistance * Math.Max(config.MinimumDistanceMultiplier, distanceMultiplier);

            var dLat = distance * Math.Cos(angle) / metersPerDegLat;
            var dLon = distance * Math.Sin(angle) / metersPerDegLon;
            fanCoords.Add(new Coordinate(longitude + dLon, latitude + dLat));
        }
        fanCoords.Add(new Coordinate(longitude, latitude));

        var fan = geometryFactory.CreatePolygon(geometryFactory.CreateLinearRing(fanCoords.ToArray()));
        fan.SRID = 4326;

        // Small circular buffer around the dog to always include a local detection zone
        var bufferRadiusDegrees = config.OmnidirectionalRadiusMeters / metersPerDegLat;
        var smallBuffer = dogPoint.Buffer(bufferRadiusDegrees); // NTS Buffer: circle approximation in lat/lon degrees

        // Combine fan and buffer (union). Handle common result types simply.
        Geometry combined;
        try
        {
            combined = fan.Union(smallBuffer);
        }
        catch
        {
            // If union fails (rare), return the buffer as the safe fallback
            return smallBuffer as Polygon ?? fan;
        }

        Polygon finalPolygon = fan; // default fallback
        if (combined is Polygon p)
        {
            finalPolygon = p;
        }
        else if (combined is MultiPolygon mp)
        {
            // Choose the largest polygon part
            finalPolygon = mp.Geometries.OfType<Polygon>().OrderByDescending(g => g.Area).First();
        }
        else if (smallBuffer is Polygon pb)
        {
            finalPolygon = pb;
        }

        finalPolygon.SRID = 4326;
        return finalPolygon;
    }

    /// <summary>
    /// Creates a unified coverage polygon by unioning all valid polygons.
    /// Simple approach: sequential union via NTS; choose the largest polygon when MultiPolygon.
    /// </summary>
    public static UnifiedScentPolygon CreateUnifiedPolygon(
        List<ScentPolygonResult> polygons,
        GeometryFactory? geometryFactory = null)
    {
        if (!polygons.Any())
            throw new ArgumentException("Cannot create unified polygon from empty polygon list", nameof(polygons));

        geometryFactory ??= DefaultGeometryFactory;

        // Keep only valid polygons (NTS validity)
        var valid = polygons.Where(p => p.IsValid).ToList();
        if (!valid.Any())
            throw new InvalidOperationException("No valid polygons found to create unified polygon");

        // Basic stats (LINQ): shows small .NET concepts for learners
        var individualAreasSum = valid.Sum(p => p.ScentAreaM2);
        var avgWindSpeed = valid.Average(p => p.WindSpeedMps);
        var windSpeedRange = (valid.Min(p => p.WindSpeedMps), valid.Max(p => p.WindSpeedMps));
        var sessionIds = valid.Select(p => p.SessionId).Distinct().ToList();
        var earliestTime = valid.Min(p => p.RecordedAt);
        var latestTime = valid.Max(p => p.RecordedAt);

        // Simple union: let NTS do the heavy lifting
        var geoms = valid.Select(v => (Geometry)v.Polygon).ToList();
        var collection = geometryFactory.BuildGeometry(geoms);
        var unioned = collection.Union();

        // Normalize to a single Polygon for the library contract
        Polygon finalPolygon;
        if (unioned is Polygon poly)
        {
            finalPolygon = poly;
        }
        else if (unioned is MultiPolygon mp)
        {
            finalPolygon = mp.Geometries.OfType<Polygon>().OrderByDescending(g => g.Area).First();
        }
        else
        {
            // Fallback: convex hull of the collection (very robust)
            finalPolygon = collection.ConvexHull() as Polygon
                ?? geometryFactory.CreatePolygon();
        }
        finalPolygon.SRID = 4326;

        // Area approximation (degrees to meters) at average latitude
        var avgLat = valid.Average(p => p.Latitude);
        var totalAreaM2 = CalculateScentAreaM2(finalPolygon, avgLat);

        return new UnifiedScentPolygon
        {
            Polygon = finalPolygon,
            PolygonCount = valid.Count,
            TotalAreaM2 = totalAreaM2,
            IndividualAreasSum = individualAreasSum,
            EarliestMeasurement = earliestTime,
            LatestMeasurement = latestTime,
            AverageWindSpeedMps = avgWindSpeed,
            WindSpeedRange = windSpeedRange,
            SessionIds = sessionIds
        };
    }

    /// <summary>
    /// Calculates the scent area in square meters for a polygon at the given latitude
    /// </summary>
    public static double CalculateScentAreaM2(Polygon polygon, double latitude)
    {
        var metersPerDegLat = 111_320.0;
        var metersPerDegLon = 111_320.0 * Math.Cos(latitude * Math.PI / 180.0);
        return polygon.Area * metersPerDegLat * metersPerDegLon;
    }

    /// <summary>
    /// Calculates maximum scent detection distance based on wind speed (simple teaching model)
    /// </summary>
    public static double CalculateMaxScentDistance(double windSpeedMps)
    {
        if (windSpeedMps < 0.5) return 60.0;
        if (windSpeedMps < 2.0) return 100.0 + (windSpeedMps - 0.5) * 20.0;
        if (windSpeedMps < 5.0) return 160.0 + (windSpeedMps - 2.0) * 15.0;
        if (windSpeedMps < 8.0) return 250.0 + (windSpeedMps - 5.0) * 10.0;
        return Math.Max(60.0, 155.0 - (windSpeedMps - 8.0) * 5.0);
    }

    /// <summary>
    /// Calculates the fan half-angle in radians (stronger wind = narrower cone)
    /// </summary>
    public static double CalculateFanAngle(double windSpeedMps)
    {
        if (windSpeedMps < 1.0) return 30.0 * Math.PI / 180.0;      // ±30°
        if (windSpeedMps < 3.0) return (30.0 - (windSpeedMps - 1.0) * 7.5) * Math.PI / 180.0; // ±15–30°
        if (windSpeedMps < 6.0) return (15.0 - (windSpeedMps - 3.0) * 2.0) * Math.PI / 180.0; // ±9–15°
        return Math.Max(5.0, 9.0 - (windSpeedMps - 6.0) * 0.5) * Math.PI / 180.0;              // ±5–9°
    }

    /// <summary>
    /// Converts a polygon to a simple text representation for debugging/demo output
    /// </summary>
    public static string PolygonToText(Polygon polygon)
    {
        if (!polygon.IsValid) return "INVALID POLYGON";
        var coords = polygon.ExteriorRing.Coordinates;
        var shown = Math.Min(coords.Length, 10);
        var coordStrings = coords.Take(shown).Select(c => $"({c.X:F6},{c.Y:F6})");
        var suffix = coords.Length > shown ? $" ... and {coords.Length - shown} more points" : string.Empty;
        return $"POLYGON({string.Join(" ", coordStrings)}{suffix})";
    }

    /// <summary>
    /// Converts a unified scent polygon to a concise text representation
    /// </summary>
    public static string UnifiedPolygonToText(UnifiedScentPolygon unifiedPolygon)
    {
        var polyText = PolygonToText(unifiedPolygon.Polygon);
        var efficiency = unifiedPolygon.CoverageEfficiency * 100;
        var duration = unifiedPolygon.LatestMeasurement - unifiedPolygon.EarliestMeasurement;
        return
            $"UNIFIED SCENT POLYGON:\n" +
            $"  Polygons: {unifiedPolygon.PolygonCount}\n" +
            $"  Area: {unifiedPolygon.TotalAreaM2:F0} m² ({unifiedPolygon.TotalAreaM2 / 10000:F2} ha)\n" +
            $"  Efficiency: {efficiency:F1}%\n" +
            $"  Duration: {duration.TotalMinutes:F1} minutes\n" +
            $"  Avg wind: {unifiedPolygon.AverageWindSpeedMps:F1} m/s\n" +
            $"  Vertices: {unifiedPolygon.VertexCount}\n" +
            $"  Valid: {unifiedPolygon.IsValid}\n" +
            $"  Geometry: {polyText}";
    }
}