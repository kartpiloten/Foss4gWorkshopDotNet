namespace RoverSimulator;

/// <summary>
/// Represents environmental attributes measured by the rover
/// </summary>
public class RoverAttributes
{
    public int WindDirectionDegrees { get; set; }
    public double WindSpeedMps { get; set; }
    public double MaxWindSpeedMps { get; init; }

    public RoverAttributes(int initialWindDirection, double initialWindSpeed, double maxWindSpeed = 15.0)
    {
        WindDirectionDegrees = initialWindDirection;
        WindSpeedMps = initialWindSpeed;
        MaxWindSpeedMps = maxWindSpeed;
    }

    /// <summary>
    /// Updates wind measurements with smooth random walk
    /// </summary>
    public void UpdateWindMeasurements(Random random)
    {
        // Update wind direction with some randomness
        WindDirectionDegrees = (int)NormalizeDegrees(WindDirectionDegrees + NextGaussian(random, 0, 10));
        
        // Update wind speed with constraints
        WindSpeedMps = Math.Clamp(WindSpeedMps + NextGaussian(random, 0, 0.5), 0.0, MaxWindSpeedMps);
    }

    /// <summary>
    /// Gets wind direction as a short for database storage
    /// </summary>
    public short GetWindDirectionAsShort()
    {
        return (short)WindDirectionDegrees;
    }

    /// <summary>
    /// Gets wind speed as a float for database storage
    /// </summary>
    public float GetWindSpeedAsFloat()
    {
        return (float)WindSpeedMps;
    }

    /// <summary>
    /// Creates a formatted string representation of wind measurements
    /// </summary>
    public string FormatWindInfo()
    {
        return $"{WindSpeedMps:F1}m/s dir {WindDirectionDegrees}deg";
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