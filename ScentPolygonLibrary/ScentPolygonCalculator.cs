using NetTopologySuite.Geometries;
using System.Globalization;

namespace ScentPolygonLibrary;

/// <summary>
/// Utility class for creating scent detection polygons from rover measurements
/// </summary>
public static class ScentPolygonCalculator
{
    private static readonly GeometryFactory DefaultGeometryFactory = new(new PrecisionModel(), 4326);

    /// <summary>
    /// Creates a scent polygon representing upwind scent detection and omnidirectional detection around the dog.
    /// The polygon extends in the upwind direction (where scent sources would be detected from).
    /// For dog scent detection: scents are carried FROM upwind sources TO the dog by the wind.
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

        // Calculate maximum scent distance based on wind speed
        var maxDistance = CalculateMaxScentDistance(windSpeedMps);

        // Convert to meters (approximate for the latitude)
        var metersPerDegLat = 111320.0;
        var metersPerDegLon = 111320.0 * Math.Cos(latitude * Math.PI / 180.0);

        // Convert wind direction to radians
        // Wind direction indicates where wind is coming FROM
        // For DOG scent detection: polygon should extend UPWIND (where scent sources are)
        // The dog detects scents that are carried TO the dog by the wind
        // So we use the wind direction directly (no +180°)
        var windRadians = (windDirectionDeg % 360) * Math.PI / 180.0;

        // Dog's position
        var dogPoint = geometryFactory.CreatePoint(new Coordinate(longitude, latitude));
        dogPoint.SRID = 4326;

        try
        {
            // 1. Create the upwind fan polygon (scent detection area)
            var fanCoordinates = new List<Coordinate>();
            fanCoordinates.Add(new Coordinate(longitude, latitude));

            var fanHalfAngle = CalculateFanAngle(windSpeedMps);

            for (int i = 0; i <= config.FanPolygonPoints; i++)
            {
                var angle = windRadians - fanHalfAngle + (2 * fanHalfAngle * i / config.FanPolygonPoints);
                var angleFromCenter = Math.Abs(angle - windRadians);
                var distanceMultiplier = Math.Cos(angleFromCenter);
                var distance = maxDistance * Math.Max(config.MinimumDistanceMultiplier, distanceMultiplier);

                var deltaLat = distance * Math.Cos(angle) / metersPerDegLat;
                var deltaLon = distance * Math.Sin(angle) / metersPerDegLon;

                fanCoordinates.Add(new Coordinate(longitude + deltaLon, latitude + deltaLat));
            }
            fanCoordinates.Add(new Coordinate(longitude, latitude));

            var fanRing = geometryFactory.CreateLinearRing(fanCoordinates.ToArray());
            var fanPolygon = geometryFactory.CreatePolygon(fanRing);
            fanPolygon.SRID = 4326;

            // 2. Create circular buffer around dog (omnidirectional detection)
            var bufferRadiusDegrees = config.OmnidirectionalRadiusMeters / metersPerDegLat;
            var circularBuffer = dogPoint.Buffer(bufferRadiusDegrees);

            // 3. Combine fan and circular buffer using union
            var combinedGeometry = fanPolygon.Union(circularBuffer);

            // Handle different result types from union
            Polygon finalPolygon;
            if (combinedGeometry is Polygon singlePolygon)
            {
                finalPolygon = singlePolygon;
            }
            else if (combinedGeometry is MultiPolygon multiPolygon)
            {
                // Take the largest polygon
                finalPolygon = multiPolygon.Geometries.OfType<Polygon>().OrderByDescending(p => p.Area).First();
            }
            else
            {
                // Fallback to circular buffer only
                finalPolygon = circularBuffer as Polygon ?? fanPolygon;
            }

            finalPolygon.SRID = 4326;

            // Validate and fix if necessary
            if (!finalPolygon.IsValid)
            {
                var buffered = finalPolygon.Buffer(0.0);
                if (buffered is Polygon fixedPolygon && fixedPolygon.IsValid)
                {
                    fixedPolygon.SRID = 4326;
                    return fixedPolygon;
                }
            }

            return finalPolygon;
        }
        catch (Exception)
        {
            // Fallback: simple circular buffer
            var fallbackRadius = 40.0 / metersPerDegLat;
            var fallbackBuffer = dogPoint.Buffer(fallbackRadius);

            if (fallbackBuffer is Polygon fallbackPolygon)
            {
                fallbackPolygon.SRID = 4326;
                return fallbackPolygon;
            }

            // Final fallback - minimal square polygon
            var finalFallback = geometryFactory.CreatePolygon(geometryFactory.CreateLinearRing(new[]
            {
                new Coordinate(longitude, latitude),
                new Coordinate(longitude + 0.001, latitude),
                new Coordinate(longitude + 0.001, latitude + 0.001),
                new Coordinate(longitude, latitude + 0.001),
                new Coordinate(longitude, latitude)
            }));
            finalFallback.SRID = 4326;
            return finalFallback;
        }
    }

    /// <summary>
    /// Creates a unified scent polygon by combining multiple individual scent polygons using Union operation.
    /// This represents the total coverage area combining all individual scent detections.
    /// </summary>
    public static UnifiedScentPolygon CreateUnifiedPolygon(
        List<ScentPolygonResult> polygons,
        GeometryFactory? geometryFactory = null)
    {
        if (!polygons.Any())
        {
            throw new ArgumentException("Cannot create unified polygon from empty polygon list", nameof(polygons));
        }

        geometryFactory ??= DefaultGeometryFactory;

        try
        {
            // Filter valid polygons
            var validPolygons = polygons.Where(p => p.IsValid).ToList();
            
            if (!validPolygons.Any())
            {
                throw new InvalidOperationException("No valid polygons found to create unified polygon");
            }

            // Calculate statistics before union
            var individualAreasSum = validPolygons.Sum(p => p.ScentAreaM2);
            var avgWindSpeed = validPolygons.Average(p => p.WindSpeedMps);
            var windSpeedRange = (validPolygons.Min(p => p.WindSpeedMps), validPolygons.Max(p => p.WindSpeedMps));
            var sessionIds = validPolygons.Select(p => p.SessionId).Distinct().ToList();
            var earliestTime = validPolygons.Min(p => p.RecordedAt);
            var latestTime = validPolygons.Max(p => p.RecordedAt);

            // Create unified geometry using progressive union for better performance
            var geometries = validPolygons.Select(p => p.Polygon).ToList();
            Geometry unifiedGeometry = geometries.First();

            // Progressive union in batches to avoid memory issues with large datasets
            const int batchSize = 50;
            for (int i = 1; i < geometries.Count; i += batchSize)
            {
                var batch = geometries.Skip(i).Take(batchSize);
                
                foreach (var geometry in batch)
                {
                    try
                    {
                        unifiedGeometry = unifiedGeometry.Union(geometry);
                    }
                    catch (Exception)
                    {
                        // Skip problematic geometries and continue
                        continue;
                    }
                }
            }

            // Handle result type from union operation
            Polygon finalPolygon;
            if (unifiedGeometry is Polygon singlePolygon)
            {
                finalPolygon = singlePolygon;
            }
            else if (unifiedGeometry is MultiPolygon multiPolygon)
            {
                // Take the largest polygon from the MultiPolygon
                finalPolygon = multiPolygon.Geometries
                    .OfType<Polygon>()
                    .OrderByDescending(p => p.Area)
                    .FirstOrDefault() ?? CreateFallbackPolygon(validPolygons, geometryFactory);
            }
            else
            {
                // Create convex hull as fallback
                var convexHull = unifiedGeometry.ConvexHull();
                finalPolygon = convexHull as Polygon ?? CreateFallbackPolygon(validPolygons, geometryFactory);
            }

            // Ensure SRID is set
            finalPolygon.SRID = 4326;

            // Smooth the polygon for better readability
            finalPolygon = SmoothPolygon(finalPolygon, geometryFactory);

            // Validate and fix if necessary
            if (!finalPolygon.IsValid)
            {
                var buffered = finalPolygon.Buffer(0.0);
                if (buffered is Polygon fixedPolygon && fixedPolygon.IsValid)
                {
                    finalPolygon = fixedPolygon;
                    finalPolygon.SRID = 4326;
                }
            }

            // Calculate final area - use average latitude for area calculation
            var avgLatitude = validPolygons.Average(p => p.Latitude);
            var unifiedAreaM2 = CalculateScentAreaM2(finalPolygon, avgLatitude);

            return new UnifiedScentPolygon
            {
                Polygon = finalPolygon,
                PolygonCount = validPolygons.Count,
                TotalAreaM2 = unifiedAreaM2,
                IndividualAreasSum = individualAreasSum,
                EarliestMeasurement = earliestTime,
                LatestMeasurement = latestTime,
                AverageWindSpeedMps = avgWindSpeed,
                WindSpeedRange = windSpeedRange,
                SessionIds = sessionIds
            };
        }
        catch (Exception ex)
        {
            // Fallback: create convex hull of all polygon centroids
            var centroids = polygons.Where(p => p.IsValid)
                .Select(p => p.Polygon.Centroid.Coordinate)
                .ToArray();

            if (centroids.Length >= 3)
            {
                var convexHull = geometryFactory.CreateMultiPointFromCoords(centroids).ConvexHull();
                if (convexHull is Polygon fallbackPolygon)
                {
                    fallbackPolygon.SRID = 4326;
                    var avgLatitude = polygons.Average(p => p.Latitude);
                    
                    return new UnifiedScentPolygon
                    {
                        Polygon = fallbackPolygon,
                        PolygonCount = polygons.Count,
                        TotalAreaM2 = CalculateScentAreaM2(fallbackPolygon, avgLatitude),
                        IndividualAreasSum = polygons.Sum(p => p.ScentAreaM2),
                        EarliestMeasurement = polygons.Min(p => p.RecordedAt),
                        LatestMeasurement = polygons.Max(p => p.RecordedAt),
                        AverageWindSpeedMps = polygons.Average(p => p.WindSpeedMps),
                        WindSpeedRange = (polygons.Min(p => p.WindSpeedMps), polygons.Max(p => p.WindSpeedMps)),
                        SessionIds = polygons.Select(p => p.SessionId).Distinct().ToList()
                    };
                }
            }

            throw new InvalidOperationException($"Failed to create unified polygon: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates a fallback polygon when union operations fail
    /// </summary>
    private static Polygon CreateFallbackPolygon(List<ScentPolygonResult> polygons, GeometryFactory geometryFactory)
    {
        // Create a simple rectangular envelope around all polygons
        var allCoords = polygons.SelectMany(p => p.Polygon.Coordinates).ToArray();
        var envelope = new Envelope();
        
        foreach (var coord in allCoords)
        {
            envelope.ExpandToInclude(coord);
        }

        // Add small buffer to envelope
        var buffer = Math.Max(envelope.Width, envelope.Height) * 0.1;
        envelope.ExpandBy(buffer);

        var fallbackPolygon = geometryFactory.ToGeometry(envelope) as Polygon;
        if (fallbackPolygon != null)
        {
            fallbackPolygon.SRID = 4326;
            return fallbackPolygon;
        }

        // Final fallback - use first polygon
        return polygons.First().Polygon;
    }

    /// <summary>
    /// Smooths a polygon to make it more readable by reducing vertex count and creating smoother edges
    /// </summary>
    private static Polygon SmoothPolygon(Polygon polygon, GeometryFactory geometryFactory)
    {
        try
        {
            // Apply Douglas-Peucker simplification with a small tolerance
            var envelope = polygon.EnvelopeInternal;
            var tolerance = Math.Min(envelope.Width, envelope.Height) * 0.01; // 1% of smallest dimension

            var simplified = polygon.Buffer(tolerance * 0.5).Buffer(-tolerance * 0.5); // Smooth and simplify

            if (simplified is Polygon smoothedPolygon && smoothedPolygon.IsValid)
            {
                smoothedPolygon.SRID = 4326;
                return smoothedPolygon;
            }

            // Fallback: just buffer slightly to smooth edges
            var buffered = polygon.Buffer(tolerance * 0.1);
            if (buffered is Polygon fallbackPolygon && fallbackPolygon.IsValid)
            {
                fallbackPolygon.SRID = 4326;
                return fallbackPolygon;
            }

            // Final fallback: return original polygon
            return polygon;
        }
        catch
        {
            // Return original polygon if smoothing fails
            return polygon;
        }
    }

    /// <summary>
    /// Calculates the scent area in square meters for a polygon at the given latitude
    /// </summary>
    public static double CalculateScentAreaM2(Polygon polygon, double latitude)
    {
        var metersPerDegLat = 111320.0;
        var metersPerDegLon = 111320.0 * Math.Cos(latitude * Math.PI / 180.0);
        return polygon.Area * metersPerDegLat * metersPerDegLon;
    }

    /// <summary>
    /// Calculates maximum scent detection distance based on wind speed
    /// For dog scent detection: stronger wind carries scents further from upwind sources
    /// </summary>
    public static double CalculateMaxScentDistance(double windSpeedMps)
    {
        // Scent detection model based on wind speed
        // Higher wind speed = better scent transport from upwind sources

        if (windSpeedMps < 0.5) // Very light wind
            return 60.0; // Limited scent transport
        else if (windSpeedMps < 2.0) // Light wind  
            return 100.0 + (windSpeedMps - 0.5) * 20.0; // Good scent transport
        else if (windSpeedMps < 5.0) // Moderate wind
            return 160.0 + (windSpeedMps - 2.0) * 15.0; // Optimal conditions
        else if (windSpeedMps < 8.0) // Strong wind
            return 250.0 + (windSpeedMps - 5.0) * 10.0; // Some dilution starts
        else // Very strong wind
            return Math.Max(60.0, 155.0 - (windSpeedMps - 8.0) * 5.0); // Significant dilution
    }

    /// <summary>
    /// Calculates the fan angle (half-angle) for scent detection based on wind speed
    /// For dog scent detection: stronger wind creates more focused scent cone from upwind
    /// </summary>
    public static double CalculateFanAngle(double windSpeedMps)
    {
        // Fan angle in radians - higher wind speed = narrower, more focused scent cone from upwind

        if (windSpeedMps < 1.0) // Light wind - wide dispersion
            return 30.0 * Math.PI / 180.0; // ±30 degrees
        else if (windSpeedMps < 3.0) // Moderate wind  
            return (30.0 - (windSpeedMps - 1.0) * 7.5) * Math.PI / 180.0; // ±15-30 degrees
        else if (windSpeedMps < 6.0) // Strong wind - more focused
            return (15.0 - (windSpeedMps - 3.0) * 2.0) * Math.PI / 180.0; // ±9-15 degrees  
        else // Very strong wind - very narrow cone
            return Math.Max(5.0, 9.0 - (windSpeedMps - 6.0) * 0.5) * Math.PI / 180.0; // ±5-9 degrees
    }

    /// <summary>
    /// Converts a polygon to a simple text representation for debugging
    /// </summary>
    public static string PolygonToText(Polygon polygon)
    {
        if (!polygon.IsValid)
        {
            return "INVALID POLYGON";
        }

        var coords = polygon.ExteriorRing.Coordinates;
        var coordStrings = coords.Take(Math.Min(coords.Length, 10)) // Limit to first 10 points
            .Select(c => $"({c.X:F6},{c.Y:F6})");

        var result = $"POLYGON({string.Join(" ", coordStrings)}";
        if (coords.Length > 10)
        {
            result += $" ... and {coords.Length - 10} more points";
        }
        result += ")";

        return result;
    }

    /// <summary>
    /// Converts a unified scent polygon to a detailed text representation
    /// </summary>
    public static string UnifiedPolygonToText(UnifiedScentPolygon unifiedPolygon)
    {
        var polygonText = PolygonToText(unifiedPolygon.Polygon);
        var efficiency = unifiedPolygon.CoverageEfficiency * 100;
        var duration = unifiedPolygon.LatestMeasurement - unifiedPolygon.EarliestMeasurement;
        
        return $"UNIFIED SCENT POLYGON:\n" +
               $"  Combines: {unifiedPolygon.PolygonCount} individual polygons\n" +
               $"  Total Area: {unifiedPolygon.TotalAreaM2:F0} m² ({unifiedPolygon.TotalAreaM2 / 10000:F2} hectares)\n" +
               $"  Coverage Efficiency: {efficiency:F1}% (lower = more overlap)\n" +
               $"  Time Range: {duration.TotalMinutes:F1} minutes\n" +
               $"  Wind Speed: {unifiedPolygon.AverageWindSpeedMps:F1} m/s avg (range: {unifiedPolygon.WindSpeedRange.Min:F1}-{unifiedPolygon.WindSpeedRange.Max:F1})\n" +
               $"  Sessions: {unifiedPolygon.SessionIds.Count}\n" +
               $"  Vertices: {unifiedPolygon.VertexCount}\n" +
               $"  Valid: {unifiedPolygon.IsValid}\n" +
               $"  Geometry: {polygonText}";
    }
}