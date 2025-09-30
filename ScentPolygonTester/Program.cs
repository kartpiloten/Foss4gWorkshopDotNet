using ReadRoverDBStubLibrary;
using ScentPolygonLibrary;

Console.WriteLine("========================================");
Console.WriteLine("     SCENT POLYGON SERVICE TESTER");
Console.WriteLine("========================================");
Console.WriteLine($"Database type preference: {TesterConfiguration.DEFAULT_DATABASE_TYPE.ToUpper()}");
Console.WriteLine();

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{    
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\nShutdown requested...");
};

// Create database configuration
var databaseConfig = TesterConfiguration.CreateDatabaseConfig();
IRoverDataReader? dataReader = null;
ScentPolygonService? scentService = null;

try
{
    // Create appropriate data reader with connection validation
    if (databaseConfig.DatabaseType.ToLower() == "postgres")
    {
        (bool isConnected, string? errorMessage) = await RoverDataReaderFactory.TestPostgresConnectionAsync(
            databaseConfig.PostgresConnectionString!,
            databaseConfig.ConnectionTimeoutSeconds,
            databaseConfig.MaxRetryAttempts,
            databaseConfig.RetryDelayMs,
            cts.Token);

        if (!isConnected)
        {
            Console.WriteLine($"ERROR: PostgreSQL connection failed: {errorMessage}");
            return;
        }
        dataReader = new PostgresRoverDataReader(databaseConfig.PostgresConnectionString!);
    }
    else
    {
        dataReader = new GeoPackageRoverDataReader(databaseConfig.GeoPackageFolderPath!);
    }

    var scentConfig = new ScentPolygonConfiguration
    {
        OmnidirectionalRadiusMeters = 30.0,
        FanPolygonPoints = 15,
        MinimumDistanceMultiplier = 0.4
    };

    scentService = new ScentPolygonService(
        dataReader,
        scentConfig,
        pollIntervalMs: 1000
    );

    Console.WriteLine("Initializing scent polygon service...");
    await scentService.StartAsync(cts.Token);

    // Wait for initial polygons to load
    await Task.Delay(1000, cts.Token);

    // Print latest scent polygon
    var latest = scentService.LatestPolygon;
    if (latest != null)
    {
        Console.WriteLine("\nLatest Scent Polygon:");
        Console.WriteLine($"  Sequence: {latest.Sequence}");
        Console.WriteLine($"  Location: ({latest.Latitude:F6}, {latest.Longitude:F6})");
        Console.WriteLine($"  Wind: {latest.WindSpeedMps:F1} m/s @ {latest.WindDirectionDeg}°");
        Console.WriteLine($"  Area: {latest.ScentAreaM2:F0} m²");
        Console.WriteLine($"  Polygon: {ScentPolygonCalculator.PolygonToText(latest.Polygon)}");
    }
    else
    {
        Console.WriteLine("No scent polygons available.");
    }

    // Print unified polygon (all polygons)
    var unified = scentService.GetUnifiedScentPolygon();
    if (unified != null)
    {
        Console.WriteLine("\nUnified Scent Polygon (All Polygons):");
        Console.WriteLine(ScentPolygonCalculator.UnifiedPolygonToText(unified));
    }
    else
    {
        Console.WriteLine("No unified polygon could be generated.");
    }

    Console.WriteLine("\nPress Ctrl+C to exit...");
    await Task.Delay(-1, cts.Token); // Wait indefinitely until cancelled
}
catch (OperationCanceledException)
{
    Console.WriteLine("Shutting down...");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
finally
{
    if (scentService != null)
    {
        scentService.Dispose();
    }
    
    if (dataReader != null)
    {
        dataReader.Dispose();
    }

    Console.WriteLine("Cleanup complete.");
}

// ===== APPLICATION CONFIGURATION =====
public static class TesterConfiguration
{
    public const string DEFAULT_DATABASE_TYPE = "postgres";
    public const string POSTGRES_CONNECTION_STRING = "Host=192.168.1.254;Port=5432;Username=anders;Password=tua123;Database=AucklandRoverData;Timeout=10;Command Timeout=30";
    public const string GEOPACKAGE_FOLDER_PATH = @"C:\temp\Rover1\";

    public static DatabaseConfiguration CreateDatabaseConfig(string? databaseType = null)
    {
        var dbType = databaseType ?? DEFAULT_DATABASE_TYPE;
        return new DatabaseConfiguration
        {
            DatabaseType = dbType,
            PostgresConnectionString = POSTGRES_CONNECTION_STRING,
            GeoPackageFolderPath = GEOPACKAGE_FOLDER_PATH,
            ConnectionTimeoutSeconds = ReaderDefaults.DEFAULT_CONNECTION_TIMEOUT_SECONDS,
            MaxRetryAttempts = ReaderDefaults.DEFAULT_MAX_RETRY_ATTEMPTS,
            RetryDelayMs = ReaderDefaults.DEFAULT_RETRY_DELAY_MS
        };
    }
}