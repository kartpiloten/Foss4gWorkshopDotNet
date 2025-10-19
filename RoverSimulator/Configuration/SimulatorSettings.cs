namespace RoverSimulator.Configuration;

/// <summary>
/// Root configuration class for the rover simulator
/// </summary>
public class SimulatorSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public ForestSettings Forest { get; set; } = new();
    public RoverSettings Rover { get; set; } = new();
    public SimulationSettings Simulation { get; set; } = new();
}

/// <summary>
/// Database connection settings
/// </summary>
public class DatabaseSettings
{
    public string Type { get; set; } = "geopackage";
    public PostgresSettings Postgres { get; set; } = new();
    public GeoPackageSettings GeoPackage { get; set; } = new();
    public ConnectionSettings Connection { get; set; } = new();
}

public class PostgresSettings
{
    public string ConnectionString { get; set; } = string.Empty;
}

public class GeoPackageSettings
{
    public string FolderPath { get; set; } = @"C:\temp\Rover1\";
}

public class ConnectionSettings
{
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 2000;
}

/// <summary>
/// Forest boundary settings
/// </summary>
public class ForestSettings
{
    public string BoundaryFile { get; set; } = "Solutionresources/RiverHeadForest.gpkg";
    public string LayerName { get; set; } = "riverheadforest";
    public BoundsSettings FallbackBounds { get; set; } = new();
}

public class BoundsSettings
{
    public double MinLatitude { get; set; } = -36.78;
    public double MaxLatitude { get; set; } = -36.70;
    public double MinLongitude { get; set; } = 174.55;
    public double MaxLongitude { get; set; } = 174.70;
}

/// <summary>
/// Rover behavior settings
/// </summary>
public class RoverSettings
{
    public StartPositionSettings StartPosition { get; set; } = new();
    public MovementSettings Movement { get; set; } = new();
}

public class StartPositionSettings
{
    public bool UseForestCentroid { get; set; } = true;
    public double DefaultLatitude { get; set; } = -36.75;
    public double DefaultLongitude { get; set; } = 174.60;
}

public class MovementSettings
{
    public double WalkSpeedMps { get; set; } = 7.0;
    public double BearingStdDev { get; set; } = 2.0;
}

/// <summary>
/// Simulation parameters
/// </summary>
public class SimulationSettings
{
    public int IntervalSeconds { get; set; } = 1;
    public WindSettings Wind { get; set; } = new();
}

public class WindSettings
{
    public double MaxSpeedMps { get; set; } = 15.0;
    public WindSpeedRange InitialSpeedRange { get; set; } = new();
}

public class WindSpeedRange
{
    public double Min { get; set; } = 1.0;
    public double Max { get; set; } = 6.0;
}