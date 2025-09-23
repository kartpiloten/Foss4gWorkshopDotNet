namespace RoverSimulator;

/// <summary>
/// Represents environmental attributes measured by the rover
/// </summary>
public class RoverAttributes
{
    public int WindDirectionDegrees { get; set; }
    public double WindSpeedMps { get; set; }
    public double MaxWindSpeedMps { get; init; }
    
    // Smoothing parameters for more realistic transitions
    private double _targetWindDirection;
    private double _targetWindSpeed;
    private double _directionChangeRate;
    private double _speedChangeRate;
    private readonly Random _internalRandom;
    private int _updatesSinceLastMajorChange;
    
    // Weather pattern simulation
    private double _baseWindDirection;
    private double _baseWindSpeed;
    private int _weatherPatternDuration;
    private int _weatherPatternElapsed;

    public RoverAttributes(int initialWindDirection, double initialWindSpeed, double maxWindSpeed = 15.0)
    {
        WindDirectionDegrees = initialWindDirection;
        WindSpeedMps = initialWindSpeed;
        MaxWindSpeedMps = maxWindSpeed;
        
        // Initialize smoothing parameters
        _targetWindDirection = initialWindDirection;
        _targetWindSpeed = initialWindSpeed;
        _directionChangeRate = 0.0;
        _speedChangeRate = 0.0;
        _internalRandom = new Random();
        _updatesSinceLastMajorChange = 0;
        
        // Initialize weather pattern with moderate duration
        _baseWindDirection = initialWindDirection;
        _baseWindSpeed = initialWindSpeed;
        _weatherPatternDuration = _internalRandom.Next(180, 600); // 3-10 minutes (reduced from 5-15)
        _weatherPatternElapsed = 0;
    }

    /// <summary>
    /// Updates wind measurements with smooth realistic transitions
    /// </summary>
    public void UpdateWindMeasurements(Random random)
    {
        _updatesSinceLastMajorChange++;
        _weatherPatternElapsed++;
        
        // Check if we need a new weather pattern
        if (_weatherPatternElapsed >= _weatherPatternDuration)
        {
            StartNewWeatherPattern(random);
        }
        
        // Smooth transition towards target values
        UpdateTargetsAndTransition(random);
        
        // Apply the smooth changes
        ApplySmoothTransition();
        
        // Ensure values stay within valid ranges
        WindDirectionDegrees = (int)NormalizeDegrees(WindDirectionDegrees);
        WindSpeedMps = Math.Clamp(WindSpeedMps, 0.0, MaxWindSpeedMps);
    }
    
    /// <summary>
    /// Starts a new weather pattern with different base conditions
    /// </summary>
    private void StartNewWeatherPattern(Random random)
    {
        // Generate new base weather conditions with moderate changes
        var directionChange = NextGaussian(random, 0, 30); // Reduced from 45 to 30 degrees
        _baseWindDirection = NormalizeDegrees(_baseWindDirection + directionChange);
        
        var speedMultiplier = 0.8 + random.NextDouble() * 0.4; // 0.8 to 1.2x (reduced range)
        _baseWindSpeed = Math.Clamp(_baseWindSpeed * speedMultiplier, 0.5, MaxWindSpeedMps);
        
        // Shorter pattern timing for more variation
        _weatherPatternDuration = random.Next(180, 600); // 3-10 minutes (reduced from 5-20)
        _weatherPatternElapsed = 0;
        
        Console.WriteLine($"New weather pattern: {_baseWindDirection:F0}° @ {_baseWindSpeed:F1} m/s");
    }
    
    /// <summary>
    /// Updates target values and calculates transition rates
    /// </summary>
    private void UpdateTargetsAndTransition(Random random)
    {
        // More frequent adjustments to targets (increased from 10% to 25%)
        if (_updatesSinceLastMajorChange > 15 && random.NextDouble() < 0.25) // 25% chance every 15+ seconds
        {
            // Medium variations around the base weather pattern (increased from 8 to 15 degrees)
            var directionVariation = NextGaussian(random, 0, 15); // More noticeable variations
            _targetWindDirection = NormalizeDegrees(_baseWindDirection + directionVariation);
            
            // Moderate speed variations (increased from 0.3 to 0.8)
            var speedVariation = NextGaussian(random, 0, 0.8); // More speed variations
            _targetWindSpeed = Math.Clamp(_baseWindSpeed + speedVariation, 0.0, MaxWindSpeedMps);
            
            // Calculate transition rates (faster transitions - 30s instead of 60s)
            var directionDiff = CalculateAngleDifference(WindDirectionDegrees, _targetWindDirection);
            _directionChangeRate = directionDiff / 30.0; // Take 30 seconds to reach target
            
            var speedDiff = _targetWindSpeed - WindSpeedMps;
            _speedChangeRate = speedDiff / 25.0; // Take 25 seconds to reach target speed
            
            _updatesSinceLastMajorChange = 0;
        }
        
        // Add moderate random fluctuations for realism (increased micro-variations)
        var microDirectionChange = NextGaussian(random, 0, 1.5); // Increased from 0.5 to 1.5
        var microSpeedChange = NextGaussian(random, 0, 0.08); // Increased from 0.02 to 0.08
        
        _targetWindDirection = NormalizeDegrees(_targetWindDirection + microDirectionChange);
        _targetWindSpeed = Math.Clamp(_targetWindSpeed + microSpeedChange, 0.0, MaxWindSpeedMps);
    }
    
    /// <summary>
    /// Applies smooth transition towards target values
    /// </summary>
    private void ApplySmoothTransition()
    {
        // Gradually move towards targets
        WindDirectionDegrees += (int)Math.Round(_directionChangeRate);
        WindSpeedMps += _speedChangeRate;
        
        // Less aggressive decay for more persistent changes
        _directionChangeRate *= 0.995; // Reduced from 0.98 to 0.995 (slower settling)
        _speedChangeRate *= 0.995;
    }
    
    /// <summary>
    /// Calculates the shortest angular difference between two directions
    /// </summary>
    private static double CalculateAngleDifference(double current, double target)
    {
        var diff = target - current;
        
        // Handle wraparound (e.g., from 350° to 10°)
        if (diff > 180) diff -= 360;
        if (diff < -180) diff += 360;
        
        return diff;
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
        var weatherInfo = _weatherPatternElapsed < 10 ? " [NEW PATTERN]" : "";
        return $"{WindSpeedMps:F1}m/s dir {WindDirectionDegrees}°{weatherInfo}";
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