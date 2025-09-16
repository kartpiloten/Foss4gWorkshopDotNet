using NetTopologySuite.Geometries;

namespace RoverSimulator;

/// <summary>
/// Represents a rover's position and movement state
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
    /// Updates the position based on current bearing and step distance
    /// </summary>
    public void UpdatePosition(TimeSpan interval)
    {
        StepMeters = WalkSpeedMps * interval.TotalSeconds;
        
        // Convert bearing to radians
        double bearingRadians = BearingDegrees * Math.PI / 180.0;

        // Decompose into local N/E components
        double dNorth = StepMeters * Math.Cos(bearingRadians);
        double dEast = StepMeters * Math.Sin(bearingRadians);

        // Convert meters to degrees
        const double metersPerDegLat = 111_320.0; // near NZ; adequate for small steps
        double metersPerDegLon = 111_320.0 * Math.Cos(Latitude * Math.PI / 180.0);

        Latitude += dNorth / metersPerDegLat;
        Longitude += dEast / metersPerDegLon;
    }

    /// <summary>
    /// Updates the bearing with random variation
    /// </summary>
    public void UpdateBearing(Random random, double meanChange = 0, double stdDev = 3)
    {
        BearingDegrees = NormalizeDegrees(BearingDegrees + NextGaussian(random, meanChange, stdDev));
    }

    /// <summary>
    /// Checks if the current position is within bounds (using bounding box for performance - FALLBACK ONLY)
    /// </summary>
    public bool IsInBounds()
    {
        return Latitude >= MinLatitude && Latitude <= MaxLatitude && 
               Longitude >= MinLongitude && Longitude <= MaxLongitude;
    }

    /// <summary>
    /// Checks if the current position is within the actual forest boundary polygon using NetTopologySuite Contains
    /// </summary>
    public async Task<bool> IsInForestBoundaryAsync()
    {
        try
        {
            var forestPolygon = await SimulatorConfiguration.GetForestBoundaryAsync();
            var currentPoint = new Point(Longitude, Latitude) { SRID = 4326 };
            
            // Use NetTopologySuite Contains method for accurate polygon boundary checking
            return forestPolygon.Contains(currentPoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Polygon boundary check failed: {ex.Message}");
            // Fallback to bounding box check if polygon check fails
            return IsInBounds();
        }
    }

    /// <summary>
    /// Synchronous version of forest boundary check (loads polygon if needed)
    /// </summary>
    public bool IsInForestBoundary()
    {
        try
        {
            var forestPolygon = SimulatorConfiguration.GetForestBoundaryAsync().GetAwaiter().GetResult();
            var currentPoint = new Point(Longitude, Latitude) { SRID = 4326 };
            
            // Use NetTopologySuite Contains method for accurate polygon boundary checking
            return forestPolygon.Contains(currentPoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Polygon boundary check failed: {ex.Message}");
            // Fallback to bounding box check if polygon check fails
            return IsInBounds();
        }
    }

    /// <summary>
    /// DEPRECATED: Simple bounding box constraint (kept for fallback only)
    /// </summary>
    public void ConstrainToBounds()
    {
        if (!IsInBounds())
        {
            // Reflect bearing (turn around)
            BearingDegrees = NormalizeDegrees(BearingDegrees + 180);
            
            // Step back inside bounds
            UpdatePosition(TimeSpan.FromSeconds(2)); // Use standard interval
            
            // Clamp to exact bounds if still outside
            Latitude = Math.Clamp(Latitude, MinLatitude, MaxLatitude);
            Longitude = Math.Clamp(Longitude, MinLongitude, MaxLongitude);
        }
    }

    /// <summary>
    /// Enhanced constraint method that uses the actual forest polygon boundary with NetTopologySuite Contains
    /// </summary>
    public async Task ConstrainToForestBoundaryAsync()
    {
        try
        {
            if (!await IsInForestBoundaryAsync())
            {
                Console.WriteLine($"Rover outside forest boundary at ({Latitude:F6}, {Longitude:F6}) - reflecting bearing");
                
                // Store the position where we went outside
                double outsideLat = Latitude;
                double outsideLon = Longitude;
                
                // Reflect bearing (turn around) - no jumping, just change direction
                BearingDegrees = NormalizeDegrees(BearingDegrees + 180);
                
                // Take a small step back to get inside the polygon
                // Use a much smaller step to avoid the 1000m jump
                const double metersPerDegLat = 111_320.0;
                double metersPerDegLon = 111_320.0 * Math.Cos(Latitude * Math.PI / 180.0);
                
                // Take small steps back until we're inside (max 5 meters total)
                double stepBackMeters = Math.Min(StepMeters, 1.0); // Use 1 meter steps or current step size, whichever is smaller
                int attempts = 0;
                
                while (!await IsInForestBoundaryAsync() && attempts < 5)
                {
                    // Convert bearing to radians for the reverse direction
                    double bearingRadians = BearingDegrees * Math.PI / 180.0;
                    double dNorth = stepBackMeters * Math.Cos(bearingRadians);
                    double dEast = stepBackMeters * Math.Sin(bearingRadians);
                    
                    Latitude += dNorth / metersPerDegLat;
                    Longitude += dEast / metersPerDegLon;
                    attempts++;
                }
                
                // If still outside after small steps, fall back to bounding box constraint
                if (!await IsInForestBoundaryAsync())
                {
                    Console.WriteLine("Warning: Could not return to forest polygon with small steps, using bounding box fallback");
                    ConstrainToBounds();
                }
                else
                {
                    Console.WriteLine($"Rover returned to forest boundary at ({Latitude:F6}, {Longitude:F6}) after {attempts} small steps");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in polygon constraint: {ex.Message}");
            // If polygon checking fails, use the simpler bounding box method
            ConstrainToBounds();
        }
    }

    /// <summary>
    /// Synchronous version of polygon-based constraint - FIXED to avoid large jumps
    /// </summary>
    public void ConstrainToForestBoundary()
    {
        try
        {
            if (!IsInForestBoundary())
            {
                Console.WriteLine($"Rover outside forest boundary at ({Latitude:F6}, {Longitude:F6}) - reflecting bearing");
                
                // Simply reflect the bearing (turn around 180 degrees)
                BearingDegrees = NormalizeDegrees(BearingDegrees + 180);
                
                // Take small corrective steps back into the polygon (avoid the 1000m jump)
                const double metersPerDegLat = 111_320.0;
                double metersPerDegLon = 111_320.0 * Math.Cos(Latitude * Math.PI / 180.0);
                
                // Use very small correction steps - maximum 1 meter per step
                double correctionStepMeters = Math.Min(1.0, StepMeters * 0.1); // 1 meter or 10% of current step, whichever is smaller
                int attempts = 0;
                
                while (!IsInForestBoundary() && attempts < 10)
                {
                    // Convert bearing to radians for the reverse direction
                    double bearingRadians = BearingDegrees * Math.PI / 180.0;
                    double dNorth = correctionStepMeters * Math.Cos(bearingRadians);
                    double dEast = correctionStepMeters * Math.Sin(bearingRadians);
                    
                    Latitude += dNorth / metersPerDegLat;
                    Longitude += dEast / metersPerDegLon;
                    attempts++;
                }
                
                // If still outside after small corrective steps, just change direction and continue
                // The rover will naturally move away from the boundary with its new bearing
                if (!IsInForestBoundary())
                {
                    Console.WriteLine($"Warning: Still outside after {attempts} correction steps, continuing with new bearing");
                    // Don't use bounding box fallback - just let it continue with the new direction
                    // The next position update will move it further from the boundary
                }
                else
                {
                    Console.WriteLine($"Rover returned to forest boundary at ({Latitude:F6}, {Longitude:F6}) after {attempts} correction steps");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in polygon constraint: {ex.Message}");
            // If polygon checking fails, use the simpler bounding box method
            ConstrainToBounds();
        }
    }

    /// <summary>
    /// Creates a NetTopologySuite Point geometry from current position
    /// </summary>
    public Point ToGeometry()
    {
        return new Point(Longitude, Latitude) { SRID = 4326 };
    }

    /// <summary>
    /// Normalizes degrees to 0-360 range
    /// </summary>
    private static double NormalizeDegrees(double degrees)
    {
        degrees %= 360.0;
        if (degrees < 0) degrees += 360.0;
        return degrees;
    }

    /// <summary>
    /// Generates a Gaussian random number using Box-Muller transform
    /// </summary>
    private static double NextGaussian(Random random, double mean, double stdDev)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = 1.0 - random.NextDouble();
        double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + z0 * stdDev;
    }
}