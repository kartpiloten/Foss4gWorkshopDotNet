/*
 The functionallity in this file is:
 - Provide simple defaults and configuration types for choosing a data reader backend.
 - Offer a minimal Postgres connectivity test and factory helpers (validated and legacy create).
 - Keep the library silent and focused on the essentials for workshop scenarios.
*/

namespace ReadRoverDBStubLibrary;

/// <summary>
/// Library-level defaults used by readers and factories (kept minimal for workshop).
/// </summary>
public static class ReaderDefaults
{
    // Connection validation defaults
    public const int DEFAULT_CONNECTION_TIMEOUT_SECONDS = 10;
    public const int DEFAULT_MAX_RETRY_ATTEMPTS = 3;
    public const int DEFAULT_RETRY_DELAY_MS = 2000;

    // Monitoring defaults (used by consuming apps)
    public const int DEFAULT_POLL_INTERVAL_MS = 1000;
    public const int DEFAULT_STATUS_INTERVAL_MS = 2000;
}

/// <summary>
/// Configuration for selecting and initializing a data reader.
/// Notes:
/// - DatabaseType: "postgres" (PostgreSQL/PostGIS) or "geopackage" (OGC GeoPackage).
/// - Async methods use CancellationToken (async/await) to support cooperative cancellation.
/// </summary>
public class DatabaseConfiguration
{
    // "postgres" (PostGIS) or "geopackage" (OGC GeoPackage on SQLite)
    public string DatabaseType { get; init; } = "geopackage";

    // Used when DatabaseType == "postgres" (Npgsql + NetTopologySuite in the reader)
    public string? PostgresConnectionString { get; init; }

    // Used when DatabaseType == "geopackage" (MapPiloteGeopackageHelper + NTS in the reader)
    public string? GeoPackageFolderPath { get; init; }

    // Connection test settings (simple retry pattern)
    public int ConnectionTimeoutSeconds { get; init; } = ReaderDefaults.DEFAULT_CONNECTION_TIMEOUT_SECONDS;
    public int MaxRetryAttempts { get; init; } = ReaderDefaults.DEFAULT_MAX_RETRY_ATTEMPTS;
    public int RetryDelayMs { get; init; } = ReaderDefaults.DEFAULT_RETRY_DELAY_MS;
}

/// <summary>
/// Factory for creating rover data readers.
/// Notes:
/// - Keep logging minimal (library is silent).
/// - Validation method provides a basic connectivity check for Postgres.
/// </summary>
public static class RoverDataReaderFactory
{
    /// <summary>
    /// Tests PostgreSQL connectivity with a simple timeout + retry loop.
    /// Uses async/await and CancellationToken to avoid blocking.
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
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds)); // cancel if the attempt takes too long

                // Create a reader and perform a simple read to validate connectivity
                using var testReader = new PostgresRoverDataReader(connectionString);
                await testReader.InitializeAsync(cts.Token);
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
                // Keep check simple: message-based short-circuit for common fatal issues
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
                await Task.Delay(retryDelayMs, cancellationToken); // brief pause between attempts
            }
        }
        
        return (false, "Connection failed: Unknown error");
    }

    /// <summary>
    /// Creates a data reader with prior connection validation for Postgres.
    /// Throws with a concise message if validation fails (kept minimal).
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
    /// Creates a data reader without validation (legacy path).
    /// Useful when the app wants to control validation separately.
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