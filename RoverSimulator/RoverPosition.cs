/*
 The functionallity in this file is:
 - Represent rover movement and geometry creation, with gentle boundary handling.
 - Demonstrate simple kinematics and NetTopologySuite Point creation (EPSG:4326).
 - Keep calculations approximate (sufficient for short steps and workshop demos).
*/

using NetTopologySuite.Geometries; // NTS Point for geometry output

namespace RoverSimulator;

/// <summary>
/// Represents a rover's position and movement state.
/// </summary>
public class RoverPosition
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double BearingDegrees { get; set; }
    public double WalkSpeedMps { get; set; }
    public double StepMeters { get; set; }

    // Boundary constraints (bounding box for quick checks - kept for fallback)
    public double MinLatitude { get; init; }
    public double MaxLatitude { get; init; }
    public double MinLongitude { get; init; }
    public double MaxLongitude { get; init; }

    public RoverPosition(double initialLat, double initialLon, double initialBearing, double walkSpeed,
                        double minLat, double maxLat, double minLon, double maxLon)
    {
        Latitude = initialLat;
        Longitude = initialLon;
        BearingDegrees = initialBearing;
        WalkSpeedMps = walkSpeed;
        MinLatitude = minLat;
        MaxLatitude = maxLat;
        MinLongitude = minLon;
        MaxLongitude = maxLon;
    }

    /// <summary>
    /// Update position using a simple local tangent-plane approximation.
    /// </summary>
    public void UpdatePosition(TimeSpan interval)
    {
        StepMeters = WalkSpeedMps * interval.TotalSeconds;

        double bearingRadians = BearingDegrees * Math.PI / 180.0;
        double dNorth = StepMeters * Math.Cos(bearingRadians);
        double dEast = StepMeters * Math.Sin(bearingRadians);

        const double metersPerDegLat = 111_320.0; // rough average for small moves
        double metersPerDegLon = 111_320.0 * Math.Cos(Latitude * Math.PI / 180.0);

        Latitude += dNorth / metersPerDegLat;
        Longitude += dEast / metersPerDegLon;
    }

    /// <summary>
    /// Update bearing with small random variation and momentum.
    /// </summary>
    public void UpdateBearing(Random random, double meanChange = 0, double stdDev = 3)
    {
        double moderateStdDev = stdDev * 0.6;
        double bearingChange = NextGaussian(random, meanChange, moderateStdDev);
        if (Math.Abs(bearingChange) > 8.0)
        {
            bearingChange *= 0.7;
        }
        BearingDegrees = NormalizeDegrees(BearingDegrees + bearingChange);
    }

    public bool IsInBounds()
    {
        return Latitude >= MinLatitude && Latitude <= MaxLatitude &&
               Longitude >= MinLongitude && Longitude <= MaxLongitude;
    }

    /// <summary>
    /// Checks if current position is inside the given polygon (NTS Contains).
    /// </summary>
    public bool IsInForestBoundary(Polygon forestPolygon)
    {
        var currentPoint = new Point(Longitude, Latitude) { SRID = 4326 };
        return forestPolygon.Contains(currentPoint);
    }

    /// <summary>
    /// DEPRECATED: Simple bounding box constraint (fallback only).
    /// </summary>
    public void ConstrainToBounds()
    {
        if (!IsInBounds())
        {
            // Reflect bearing (turn around) and take a small step back inside bounds
            BearingDegrees = NormalizeDegrees(BearingDegrees + 180);
            UpdatePosition(TimeSpan.FromSeconds(2));
            Latitude = Math.Clamp(Latitude, MinLatitude, MaxLatitude);
            Longitude = Math.Clamp(Longitude, MinLongitude, MaxLongitude);
        }
    }

    /// <summary>
    /// Constrain to polygon boundary with small corrective steps (no large jumps).
    /// </summary>
    public void ConstrainToForestBoundary(Polygon forestPolygon)
    {
        try
        {
            if (!IsInForestBoundary(forestPolygon))
            {
                Console.WriteLine($"Rover outside forest boundary at ({Latitude:F6}, {Longitude:F6}) - reflecting bearing");
                double metersPerDegLat = 111_320.0;
                double metersPerDegLon = 111_320.0 * Math.Cos(Latitude * Math.PI / 180.0);
                BearingDegrees = NormalizeDegrees(BearingDegrees + 180);
                double correctionStepMeters = Math.Min(1.0, StepMeters * 0.1);
                int attempts = 0;
                while (!IsInForestBoundary(forestPolygon) && attempts < 10)
                {
                    double bearingRadians = BearingDegrees * Math.PI / 180.0;
                    double dNorth = correctionStepMeters * Math.Cos(bearingRadians);
                    double dEast = correctionStepMeters * Math.Sin(bearingRadians);
                    Latitude += dNorth / metersPerDegLat;
                    Longitude += dEast / metersPerDegLon;
                    attempts++;
                }
                if (!IsInForestBoundary(forestPolygon))
                {
                    Console.WriteLine($"Warning: Still outside after {attempts} correction steps, continuing with new bearing");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in polygon constraint: {ex.Message}");
            ConstrainToBounds();
        }
    }

    public Point ToGeometry() => new Point(Longitude, Latitude) { SRID = 4326 };

    private static double NormalizeDegrees(double degrees)
    {
        degrees %= 360.0;
        if (degrees < 0) degrees += 360.0;
        return degrees;
    }

    private static double NextGaussian(Random random, double mean, double stdDev)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = 1.0 - random.NextDouble();
        double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + z0 * stdDev;
    }
}