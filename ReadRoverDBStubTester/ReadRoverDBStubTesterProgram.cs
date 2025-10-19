using Microsoft.Extensions.Configuration;
using ReadRoverDBStubLibrary;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

TesterConfiguration.Initialize(configuration);

// ===== MAIN PROGRAM =====

Console.WriteLine("========================================");
Console.WriteLine("       ROVER DATA READER TESTER");
Console.WriteLine("========================================");
Console.WriteLine($"Database type preference: {TesterConfiguration.DefaultDatabaseType.ToUpper()}");
Console.WriteLine("Press Ctrl+C to stop monitoring...");
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

// Create appropriate data reader with connection validation
IRoverDataReader dataReader;
try
{
    Console.WriteLine($"\n{new string('=', 60)}");
    Console.WriteLine("DATABASE CONNECTION SETUP (READER)");
    Console.WriteLine($"{new string('=', 60)}");
    Console.WriteLine($"Database type: {databaseConfig.DatabaseType.ToUpper()}");
    
    if (databaseConfig.DatabaseType.ToLower() == "postgres")
    {
        Console.WriteLine("Testing PostgreSQL connection for reading...");
        Console.WriteLine($"Target: {TesterConfiguration.GetPostgresServerInfo()}");
        Console.WriteLine($"Timeout: {databaseConfig.ConnectionTimeoutSeconds} seconds");
        
        var (isConnected, errorMessage) = await RoverDataReaderFactory.TestPostgresConnectionAsync(
            databaseConfig.PostgresConnectionString!,
            databaseConfig.ConnectionTimeoutSeconds,
            databaseConfig.MaxRetryAttempts,
            databaseConfig.RetryDelayMs,
            cts.Token);
        
        if (isConnected)
        {
            Console.WriteLine("SUCCESS: PostgreSQL connection successful - using PostgreSQL database for reading");
            Console.WriteLine($"{new string('=', 60)}");
            dataReader = new PostgresRoverDataReader(databaseConfig.PostgresConnectionString!);
        }
        else
        {
            Console.WriteLine($"ERROR: PostgreSQL connection failed: {errorMessage}");
            Console.WriteLine();
            Console.WriteLine("NETWORK TROUBLESHOOTING:");
            Console.WriteLine($"- Check if PostgreSQL server is running on {TesterConfiguration.GetPostgresServerInfo()}");
            Console.WriteLine("- Verify network connectivity to the database server");
            Console.WriteLine("- Check firewall settings on both client and server");
            Console.WriteLine("- Ensure PostgreSQL is configured to accept connections");
            Console.WriteLine("- Verify credentials and database permissions");
            Console.WriteLine("- Make sure RoverSimulator has created the database schema");
            Console.WriteLine();
            Console.WriteLine("DATABASE CONFIGURATION OPTIONS:");
            Console.WriteLine("1. Fix the PostgreSQL connection issue above");
            Console.WriteLine("2. Update DatabaseConfiguration:DatabaseType to \"geopackage\" in appsettings.json");
            Console.WriteLine("3. Run RoverSimulator first to create the database schema");
            Console.WriteLine($"{new string('=', 60)}");
            
            Console.WriteLine("\nReader cannot proceed without a valid database connection.");
            Console.WriteLine("Please resolve the database connection issue and try again.");
            return;
        }
    }
    else
    {
        Console.WriteLine("SUCCESS: Using GeoPackage database (local file storage)");
        Console.WriteLine($"{new string('=', 60)}");
        dataReader = new GeoPackageRoverDataReader(databaseConfig.GeoPackageFolderPath!);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Database setup cancelled by user.");
    return;
}
catch (Exception ex)
{
    Console.WriteLine($"Unexpected error creating database reader: {ex.Message}");
    Console.WriteLine("Cannot proceed without a database connection.");
    return;
}

try
{
    // Create the monitor
    using var monitor = new RoverDataMonitor(
        dataReader, 
        pollIntervalMs: ReaderDefaults.DEFAULT_POLL_INTERVAL_MS,
        statusIntervalMs: TesterConfiguration.DisplayUpdateIntervalMs
    );
    
    // Subscribe to events for console output
    monitor.DataUpdated += (sender, e) =>
    {
        Console.WriteLine($"[UPDATE] Added {e.NewMeasurementsCount} new measurements. Total: {e.TotalMeasurementsCount}");
    };
    
    monitor.StatusUpdate += (sender, e) =>
    {
        if (e.LatestMeasurement != null)
        {
            Console.WriteLine($"[STATUS] Total measurements: {e.TotalMeasurementsCount}, " +
                            $"Latest: Seq={e.LatestMeasurement.Sequence}, " +
                            $"Location=({e.LatestMeasurement.Latitude:F6}, {e.LatestMeasurement.Longitude:F6}), " +
                            $"Wind={e.LatestMeasurement.WindSpeedMps:F1}m/s @ {e.LatestMeasurement.WindDirectionDeg}deg, " +
                            $"Time={e.LatestMeasurement.RecordedAt:HH:mm:ss}");
        }
        else
        {
            Console.WriteLine($"[STATUS] Total measurements: {e.TotalMeasurementsCount}, No data available yet");
        }
    };
    
    // Start monitoring
    Console.WriteLine("Initializing rover data monitor...");
    await monitor.StartAsync();
    
    Console.WriteLine();
    Console.WriteLine("Monitoring rover database for new measurements...");
    Console.WriteLine("The collection will be updated automatically as new data arrives.");
    Console.WriteLine($"Data source: {(dataReader is PostgresRoverDataReader ? "PostgreSQL database" : "GeoPackage file")}");
    Console.WriteLine($"Initial data loaded: {monitor.Count} measurements");
    
    if (monitor.LatestMeasurement != null)
    {
        Console.WriteLine($"Latest measurement: Seq={monitor.LatestMeasurement.Sequence}, " +
                        $"Location=({monitor.LatestMeasurement.Latitude:F6}, {monitor.LatestMeasurement.Longitude:F6}), " +
                        $"Time={monitor.LatestMeasurement.RecordedAt:HH:mm:ss}");
    }
    Console.WriteLine();
    
    // Keep the application running until cancelled
    while (!cts.IsCancellationRequested)
    {
        await Task.Delay(100, cts.Token);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Monitoring cancelled by user.");
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"Database file not found: {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("TROUBLESHOOTING:");
    Console.WriteLine("1. Make sure the RoverSimulator has created the database/GeoPackage file");
    Console.WriteLine("2. Check that the file path is correct in TesterConfiguration");
    Console.WriteLine("3. Ensure the RoverSimulator is running or has run at least once");
    Console.WriteLine("4. For PostgreSQL: Verify the database schema has been created");
}
catch (Exception ex)
{
    Console.WriteLine($"Error during monitoring: {ex.Message}");
    
    if (ex.Message.Contains("connection") || ex.Message.Contains("server") || ex.Message.Contains("timeout"))
    {
        Console.WriteLine();
        Console.WriteLine("DATABASE CONNECTION TROUBLESHOOTING:");
        Console.WriteLine("For PostgreSQL:");
        Console.WriteLine("1. Verify PostgreSQL server is running and accessible");
        Console.WriteLine("2. Check connection string credentials");
        Console.WriteLine("3. Ensure the RoverSimulator has created the database schema");
        Console.WriteLine("4. Verify the 'roverdata.rover_measurements' table exists");
        Console.WriteLine();
        Console.WriteLine("For GeoPackage:");
        Console.WriteLine("1. Verify the GeoPackage file exists");
        Console.WriteLine("2. Check file permissions");
        Console.WriteLine("3. Ensure the file is not locked by another application");
        Console.WriteLine();
        Console.WriteLine("Quick fix: Change DatabaseConfiguration:DatabaseType in appsettings.json");
    }
}
finally
{
    Console.WriteLine("\nCleaning up database connections...");
    dataReader.Dispose();
}

Console.WriteLine("\nRover data reader tester stopped.");

// ===== APPLICATION CONFIGURATION =====
// This configuration belongs to the consuming application, not the library

/// <summary>
/// Configuration for the ReadRoverDBStubTester application
/// </summary>
public static class TesterConfiguration
{
    private static IConfiguration? _configuration;

    public static void Initialize(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    private static IConfiguration Configuration =>
        _configuration ?? throw new InvalidOperationException("TesterConfiguration.Initialize must be called before accessing configuration values.");

    public static string DefaultDatabaseType =>
        Configuration.GetValue<string>("DatabaseConfiguration:DatabaseType") ?? "geopackage";

    public static string GeoPackageFolderPath =>
        Configuration.GetValue<string>("DatabaseConfiguration:GeoPackageFolderPath") ?? "/tmp/Rover1/";

    public static int DisplayUpdateIntervalMs =>
        Configuration.GetValue<int?>("Tester:DisplayUpdateIntervalMs") ?? 2000;

    private static string PostgresConnectionString =>
        Configuration.GetValue<string>("DatabaseConfiguration:PostgresConnectionString") ?? string.Empty;

    /// <summary>
    /// Creates database configuration object
    /// </summary>
    public static DatabaseConfiguration CreateDatabaseConfig(string? databaseType = null)
    {
        var section = Configuration.GetSection("DatabaseConfiguration");
        var dbType = databaseType ?? section.GetValue<string>("DatabaseType") ?? "geopackage";

        return new DatabaseConfiguration
        {
            DatabaseType = dbType,
            PostgresConnectionString = PostgresConnectionString,
            GeoPackageFolderPath = GeoPackageFolderPath,
            ConnectionTimeoutSeconds = section.GetValue<int?>("ConnectionTimeoutSeconds") ?? ReaderDefaults.DEFAULT_CONNECTION_TIMEOUT_SECONDS,
            MaxRetryAttempts = section.GetValue<int?>("MaxRetryAttempts") ?? ReaderDefaults.DEFAULT_MAX_RETRY_ATTEMPTS,
            RetryDelayMs = section.GetValue<int?>("RetryDelayMs") ?? ReaderDefaults.DEFAULT_RETRY_DELAY_MS
        };
    }

    /// <summary>
    /// Gets a human-readable description of the PostgreSQL server info
    /// </summary>
    public static string GetPostgresServerInfo()
    {
        try
        {
            var connectionString = PostgresConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return "PostgreSQL";
            }

            var parts = new List<string>();

            if (connectionString.Contains("Host="))
            {
                var host = ExtractConnectionStringValue(connectionString, "Host");
                var port = ExtractConnectionStringValue(connectionString, "Port") ?? "5432";
                if (!string.IsNullOrEmpty(host))
                {
                    parts.Add($"{host}:{port}");
                }
            }

            var database = ExtractConnectionStringValue(connectionString, "Database");
            if (!string.IsNullOrEmpty(database))
            {
                parts.Add($"Database: {database}");
            }

            var username = ExtractConnectionStringValue(connectionString, "Username");
            if (!string.IsNullOrEmpty(username))
            {
                parts.Add($"User: {username}");
            }

            return parts.Any() ? string.Join(", ", parts) : "PostgreSQL";
        }
        catch
        {
            return "PostgreSQL";
        }
    }

    private static string? ExtractConnectionStringValue(string connectionString, string key)
    {
        try
        {
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var keyValue = parts.FirstOrDefault(p => p.Trim().StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase));
            return keyValue?.Substring(keyValue.IndexOf('=') + 1).Trim();
        }
        catch
        {
            return null;
        }
    }
}
