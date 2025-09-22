namespace ReadRoverDBStub;

/// <summary>
/// Configuration for the rover data reader application
/// </summary>
public static class ReaderConfiguration
{
    // Database configuration - simplified to GeoPackage only for now
    public const string DEFAULT_DATABASE_TYPE = "geopackage";
    public const string GEOPACKAGE_FOLDER_PATH = @"C:\temp\Rover1\";

    // Monitoring configuration
    public const int DEFAULT_POLL_INTERVAL_MS = 1000; // Check every second
    public const int DEFAULT_DISPLAY_INTERVAL_MS = 2000; // Update display every 2 seconds

    /// <summary>
    /// Creates the appropriate data reader based on database type
    /// </summary>
    public static IRoverDataReader CreateReader(string databaseType)
    {
        return databaseType.ToLower() switch
        {
            "geopackage" => new GeoPackageRoverDataReader(GEOPACKAGE_FOLDER_PATH),
            _ => throw new ArgumentException($"Database type '{databaseType}' not supported yet. Currently only 'geopackage' is supported.")
        };
    }
}