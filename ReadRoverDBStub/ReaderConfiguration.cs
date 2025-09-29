namespace ReadRoverDBStub;

/// <summary>
/// Configuration for the rover data reader application
/// </summary>
public static class ReaderConfiguration
{
    // Database configuration - matches RoverSimulator configuration
    public const string DEFAULT_DATABASE_TYPE = "postgres"; // Match RoverSimulator default
    
    // PostgreSQL configuration - should match RoverSimulator settings
    public const string POSTGRES_CONNECTION_STRING = "Host=192.168.1.254;Port=5432;Username=anders;Password=tua123;Database=AucklandRoverData;Timeout=10;Command Timeout=30";
    
    public const string GEOPACKAGE_FOLDER_PATH = @"C:\temp\Rover1\";

    // Connection validation settings - match RoverSimulator
    public const int CONNECTION_TIMEOUT_SECONDS = 10;
    public const int MAX_RETRY_ATTEMPTS = 3;
    public const int RETRY_DELAY_MS = 2000;

    // Monitoring configuration
    public const int DEFAULT_POLL_INTERVAL_MS = 1000; // Check every second
    public const int DEFAULT_DISPLAY_INTERVAL_MS = 2000; // Update display every 2 seconds

    /// <summary>
    /// Tests PostgreSQL database connectivity with timeout and retry logic
    /// </summary>
    public static async Task<(bool isConnected, string errorMessage)> TestPostgresConnectionAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Testing PostgreSQL connection for reading...");
        Console.WriteLine($"Target: {GetPostgresServerInfo()}");
        Console.WriteLine($"Timeout: {CONNECTION_TIMEOUT_SECONDS} seconds");
        
        for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(CONNECTION_TIMEOUT_SECONDS));
                
                // Create test reader to validate connection
                using var testReader = new PostgresRoverDataReader(POSTGRES_CONNECTION_STRING);
                
                Console.WriteLine($"  Attempt {attempt}/{MAX_RETRY_ATTEMPTS}: Connecting...");
                
                // Try to initialize and access data
                await testReader.InitializeAsync(cts.Token);
                
                // Test basic read operation
                await testReader.GetMeasurementCountAsync(cts.Token);
                
                Console.WriteLine("  SUCCESS: PostgreSQL connection established and data accessible!");
                return (true, string.Empty);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return (false, "Connection test cancelled by user");
            }
            catch (OperationCanceledException)
            {
                var message = $"  TIMEOUT: Connection attempt {attempt} timed out after {CONNECTION_TIMEOUT_SECONDS} seconds";
                Console.WriteLine(message);
                
                if (attempt == MAX_RETRY_ATTEMPTS)
                {
                    return (false, $"Connection failed: All {MAX_RETRY_ATTEMPTS} attempts timed out after {CONNECTION_TIMEOUT_SECONDS} seconds each");
                }
            }
            catch (Exception ex)
            {
                var message = $"  ERROR: Connection attempt {attempt} failed: {ex.Message}";
                Console.WriteLine(message);
                
                // For specific error types, don't retry
                if (ex.Message.Contains("authentication failed") || 
                    ex.Message.Contains("database") && ex.Message.Contains("does not exist") ||
                    ex.Message.Contains("permission denied") ||
                    ex.Message.Contains("table not found") ||
                    ex.Message.Contains("relation") && ex.Message.Contains("does not exist"))
                {
                    return (false, $"Connection failed: {ex.Message}");
                }
                
                if (attempt == MAX_RETRY_ATTEMPTS)
                {
                    return (false, $"Connection failed after {MAX_RETRY_ATTEMPTS} attempts: {ex.Message}");
                }
            }
            
            if (attempt < MAX_RETRY_ATTEMPTS)
            {
                Console.WriteLine($"  Waiting {RETRY_DELAY_MS}ms before retry...");
                await Task.Delay(RETRY_DELAY_MS, cancellationToken);
            }
        }
        
        return (false, "Connection failed: Unknown error");
    }

    /// <summary>
    /// Gets a human-readable description of the PostgreSQL server info
    /// </summary>
    private static string GetPostgresServerInfo()
    {
        try
        {
            var connectionString = POSTGRES_CONNECTION_STRING;
            var parts = new List<string>();
            
            if (connectionString.Contains("Host="))
            {
                var host = ExtractConnectionStringValue(connectionString, "Host");
                var port = ExtractConnectionStringValue(connectionString, "Port") ?? "5432";
                parts.Add($"{host}:{port}");
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

    /// <summary>
    /// Creates the appropriate data reader with connection validation (no automatic fallback)
    /// </summary>
    public static async Task<IRoverDataReader> CreateReaderWithValidationAsync(string databaseType, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"\n{new string('=', 60)}");
        Console.WriteLine("DATABASE CONNECTION SETUP (READER)");
        Console.WriteLine($"{new string('=', 60)}");
        Console.WriteLine($"Database type: {databaseType.ToUpper()}");
        
        if (databaseType.ToLower() == "postgres")
        {
            var (isConnected, errorMessage) = await TestPostgresConnectionAsync(cancellationToken);
            
            if (isConnected)
            {
                Console.WriteLine("✅ PostgreSQL connection successful - using PostgreSQL database for reading");
                Console.WriteLine($"{new string('=', 60)}");
                return new PostgresRoverDataReader(POSTGRES_CONNECTION_STRING);
            }
            else
            {
                Console.WriteLine($"❌ PostgreSQL connection failed: {errorMessage}");
                Console.WriteLine();
                Console.WriteLine("NETWORK TROUBLESHOOTING:");
                Console.WriteLine($"- Check if PostgreSQL server is running on {GetPostgresServerInfo()}");
                Console.WriteLine("- Verify network connectivity to the database server");
                Console.WriteLine("- Check firewall settings on both client and server");
                Console.WriteLine("- Ensure PostgreSQL is configured to accept connections");
                Console.WriteLine("- Verify credentials and database permissions");
                Console.WriteLine("- Make sure RoverSimulator has created the database schema");
                Console.WriteLine();
                Console.WriteLine("DATABASE CONFIGURATION OPTIONS:");
                Console.WriteLine("1. Fix the PostgreSQL connection issue above");
                Console.WriteLine("2. Change DEFAULT_DATABASE_TYPE to \"geopackage\" in ReaderConfiguration.cs");
                Console.WriteLine("3. Run RoverSimulator first to create the database schema");
                Console.WriteLine($"{new string('=', 60)}");
                
                throw new InvalidOperationException($"PostgreSQL connection failed: {errorMessage}. Please fix the connection issue or change to a different database type.");
            }
        }
        else if (databaseType.ToLower() == "geopackage")
        {
            Console.WriteLine("✅ Using GeoPackage database (local file storage)");
            Console.WriteLine($"{new string('=', 60)}");
            return new GeoPackageRoverDataReader(GEOPACKAGE_FOLDER_PATH);
        }
        else
        {
            throw new ArgumentException($"Unsupported database type: {databaseType}. Use 'postgres' or 'geopackage'.");
        }
    }

    /// <summary>
    /// Creates the appropriate data reader based on database type (legacy method)
    /// </summary>
    public static IRoverDataReader CreateReader(string databaseType)
    {
        return databaseType.ToLower() switch
        {
            "postgres" => new PostgresRoverDataReader(POSTGRES_CONNECTION_STRING),
            "geopackage" => new GeoPackageRoverDataReader(GEOPACKAGE_FOLDER_PATH),
            _ => throw new ArgumentException($"Unsupported database type: {databaseType}. Use 'postgres' or 'geopackage'.")
        };
    }
}