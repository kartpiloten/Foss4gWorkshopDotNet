namespace ReadRoverDBStubLibrary;

/// <summary>
/// Configuration constants for the rover data reader library (defaults only)
/// </summary>
public static class ReaderDefaults
{
    // Connection validation settings - reasonable defaults
    public const int DEFAULT_CONNECTION_TIMEOUT_SECONDS = 10;
    public const int DEFAULT_MAX_RETRY_ATTEMPTS = 3;
    public const int DEFAULT_RETRY_DELAY_MS = 2000;

    // Monitoring settings - reasonable defaults
    public const int DEFAULT_POLL_INTERVAL_MS = 1000;
    public const int DEFAULT_STATUS_INTERVAL_MS = 2000;
}

/// <summary>
/// Configuration for database connections (provided by consuming application)
/// </summary>
public class DatabaseConfiguration
{
    public string DatabaseType { get; init; } = "geopackage";
    public string? PostgresConnectionString { get; init; }
    public string? GeoPackageFolderPath { get; init; }
    public int ConnectionTimeoutSeconds { get; init; } = ReaderDefaults.DEFAULT_CONNECTION_TIMEOUT_SECONDS;
    public int MaxRetryAttempts { get; init; } = ReaderDefaults.DEFAULT_MAX_RETRY_ATTEMPTS;
    public int RetryDelayMs { get; init; } = ReaderDefaults.DEFAULT_RETRY_DELAY_MS;
}

/// <summary>
/// Factory for creating rover data readers (silent library version)
/// </summary>
public static class RoverDataReaderFactory
{
    /// <summary>
    /// Tests PostgreSQL database connectivity with timeout and retry logic (silent)
    /// </summary>
    public static async Task<(bool isConnected, string errorMessage)> TestPostgresConnectionAsync(
        string connectionString, 
        int timeoutSeconds = ReaderDefaults.DEFAULT_CONNECTION_TIMEOUT_SECONDS,
        int maxRetryAttempts = ReaderDefaults.DEFAULT_MAX_RETRY_ATTEMPTS,
        int retryDelayMs = ReaderDefaults.DEFAULT_RETRY_DELAY_MS,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                
                // Create test reader to validate connection
                using var testReader = new PostgresRoverDataReader(connectionString);
                
                // Try to initialize and access data
                await testReader.InitializeAsync(cts.Token);
                
                // Test basic read operation
                await testReader.GetMeasurementCountAsync(cts.Token);
                
                return (true, string.Empty);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return (false, "Connection test cancelled by user");
            }
            catch (OperationCanceledException)
            {
                if (attempt == maxRetryAttempts)
                {
                    return (false, $"Connection failed: All {maxRetryAttempts} attempts timed out after {timeoutSeconds} seconds each");
                }
            }
            catch (Exception ex)
            {
                // For specific error types, don't retry
                if (ex.Message.Contains("authentication failed") || 
                    ex.Message.Contains("database") && ex.Message.Contains("does not exist") ||
                    ex.Message.Contains("permission denied") ||
                    ex.Message.Contains("table not found") ||
                    ex.Message.Contains("relation") && ex.Message.Contains("does not exist"))
                {
                    return (false, $"Connection failed: {ex.Message}");
                }
                
                if (attempt == maxRetryAttempts)
                {
                    return (false, $"Connection failed after {maxRetryAttempts} attempts: {ex.Message}");
                }
            }
            
            if (attempt < maxRetryAttempts)
            {
                await Task.Delay(retryDelayMs, cancellationToken);
            }
        }
        
        return (false, "Connection failed: Unknown error");
    }

    /// <summary>
    /// Creates the appropriate data reader with connection validation (silent - no automatic fallback)
    /// </summary>
    public static async Task<IRoverDataReader> CreateReaderWithValidationAsync(
        DatabaseConfiguration config, 
        CancellationToken cancellationToken = default)
    {
        if (config.DatabaseType.ToLower() == "postgres")
        {
            if (string.IsNullOrEmpty(config.PostgresConnectionString))
            {
                throw new ArgumentException("PostgreSQL connection string is required when database type is 'postgres'");
            }

            var (isConnected, errorMessage) = await TestPostgresConnectionAsync(
                config.PostgresConnectionString,
                config.ConnectionTimeoutSeconds,
                config.MaxRetryAttempts,
                config.RetryDelayMs,
                cancellationToken);
            
            if (isConnected)
            {
                return new PostgresRoverDataReader(config.PostgresConnectionString);
            }
            else
            {
                throw new InvalidOperationException($"PostgreSQL connection failed: {errorMessage}. Please fix the connection issue or change to a different database type.");
            }
        }
        else if (config.DatabaseType.ToLower() == "geopackage")
        {
            if (string.IsNullOrEmpty(config.GeoPackageFolderPath))
            {
                throw new ArgumentException("GeoPackage folder path is required when database type is 'geopackage'");
            }

            return new GeoPackageRoverDataReader(config.GeoPackageFolderPath);
        }
        else
        {
            throw new ArgumentException($"Unsupported database type: {config.DatabaseType}. Use 'postgres' or 'geopackage'.");
        }
    }

    /// <summary>
    /// Creates the appropriate data reader based on configuration (silent - legacy method)
    /// </summary>
    public static IRoverDataReader CreateReader(DatabaseConfiguration config)
    {
        return config.DatabaseType.ToLower() switch
        {
            "postgres" when !string.IsNullOrEmpty(config.PostgresConnectionString) 
                => new PostgresRoverDataReader(config.PostgresConnectionString),
            "geopackage" when !string.IsNullOrEmpty(config.GeoPackageFolderPath)
                => new GeoPackageRoverDataReader(config.GeoPackageFolderPath),
            "postgres" => throw new ArgumentException("PostgreSQL connection string is required when database type is 'postgres'"),
            "geopackage" => throw new ArgumentException("GeoPackage folder path is required when database type is 'geopackage'"),
            _ => throw new ArgumentException($"Unsupported database type: {config.DatabaseType}. Use 'postgres' or 'geopackage'.")
        };
    }
}