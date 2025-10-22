/*
 The functionallity in this file is:
 - Simulate wind attributes with smooth transitions and occasional pattern shifts.
 - Provide formatted values and types suitable for storage (short/float).
 - Keep the model simple and readable for workshop demos.
*/

namespace RoverSimulator;

/// <summary>
/// Represents environmental attributes measured by the rover.
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
        
        _targetWindDirection = initialWindDirection;
        _targetWindSpeed = initialWindSpeed;
        _directionChangeRate = 0.0;
        _speedChangeRate = 0.0;
        _internalRandom = new Random();
        _updatesSinceLastMajorChange = 0;
        
        _baseWindDirection = initialWindDirection;
        _baseWindSpeed = initialWindSpeed;
        _weatherPatternDuration = _internalRandom.Next(180, 600);
        _weatherPatternElapsed = 0;
    }

    /// <summary>
    /// Update wind measurements with smooth realistic transitions.
    /// </summary>
    public void UpdateWindMeasurements(Random random)
    {
        _updatesSinceLastMajorChange++;
        _weatherPatternElapsed++;
        
        if (_weatherPatternElapsed >= _weatherPatternDuration)
        {
            StartNewWeatherPattern(random);
        }
        
        UpdateTargetsAndTransition(random);
        ApplySmoothTransition();
        
        WindDirectionDegrees = (int)NormalizeDegrees(WindDirectionDegrees);
        WindSpeedMps = Math.Clamp(WindSpeedMps, 0.0, MaxWindSpeedMps);
    }
    
    private void StartNewWeatherPattern(Random random)
    {
        var directionChange = NextGaussian(random, 0, 30);
        _baseWindDirection = NormalizeDegrees(_baseWindDirection + directionChange);
        
        var speedMultiplier = 0.8 + random.NextDouble() * 0.4;
        _baseWindSpeed = Math.Clamp(_baseWindSpeed * speedMultiplier, 0.5, MaxWindSpeedMps);
        
        _weatherPatternDuration = random.Next(180, 600);
        _weatherPatternElapsed = 0;
        
        Console.WriteLine($"New weather pattern: {_baseWindDirection:F0}° @ {_baseWindSpeed:F1} m/s");
    }
    
    private void UpdateTargetsAndTransition(Random random)
    {
        if (_updatesSinceLastMajorChange > 15 && random.NextDouble() < 0.25)
        {
            var directionVariation = NextGaussian(random, 0, 15);
            _targetWindDirection = NormalizeDegrees(_baseWindDirection + directionVariation);
            
            var speedVariation = NextGaussian(random, 0, 0.8);
            _targetWindSpeed = Math.Clamp(_baseWindSpeed + speedVariation, 0.0, MaxWindSpeedMps);
            
            var directionDiff = CalculateAngleDifference(WindDirectionDegrees, _targetWindDirection);
            _directionChangeRate = directionDiff / 30.0;
            
            var speedDiff = _targetWindSpeed - WindSpeedMps;
            _speedChangeRate = speedDiff / 25.0;
            
            _updatesSinceLastMajorChange = 0;
        }
        
        var microDirectionChange = NextGaussian(random, 0, 1.5);
        var microSpeedChange = NextGaussian(random, 0, 0.08);
        
        _targetWindDirection = NormalizeDegrees(_targetWindDirection + microDirectionChange);
        _targetWindSpeed = Math.Clamp(_targetWindSpeed + microSpeedChange, 0.0, MaxWindSpeedMps);
    }
    
    private void ApplySmoothTransition()
    {
        WindDirectionDegrees += (int)Math.Round(_directionChangeRate);
        WindSpeedMps += _speedChangeRate;
        _directionChangeRate *= 0.995;
        _speedChangeRate *= 0.995;
    }
    
    private static double CalculateAngleDifference(double current, double target)
    {
        var diff = target - current;
        if (diff > 180) diff -= 360;
        if (diff < -180) diff += 360;
        return diff;
    }

    public short GetWindDirectionAsShort() => (short)WindDirectionDegrees;
    public float GetWindSpeedAsFloat() => (float)WindSpeedMps;

    public string FormatWindInfo()
    {
        var weatherInfo = _weatherPatternElapsed < 10 ? " [NEW PATTERN]" : "";
        return $"{WindSpeedMps:F1}m/s dir {WindDirectionDegrees}°{weatherInfo}";
    }

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