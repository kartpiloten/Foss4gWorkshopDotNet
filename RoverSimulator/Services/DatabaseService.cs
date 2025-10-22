/*
 The functionallity in this file is:
 - Create a data repository (GeoPackage or Postgres) from typed settings, with a minimal connectivity check.
 - Use async/await and CancellationToken for non-blocking validation.
 - Keep console guidance concise for workshop users.
*/

using RoverSimulator.Configuration;

namespace RoverSimulator.Services;

/// <summary>
/// Service responsible for creating and validating database connections.
/// </summary>
public class DatabaseService
{
    private readonly DatabaseSettings _settings;

    public DatabaseService(DatabaseSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Creates the appropriate data repository with connection validation.
    /// </summary>
    public async Task<IRoverDataRepository> CreateRepositoryAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"\n{new string('=', 60)}");
        Console.WriteLine("DATABASE CONNECTION SETUP");
        Console.WriteLine($"{new string('=', 60)}");
        Console.WriteLine($"Database type: {_settings.Type.ToUpper()}");

        if (_settings.Type.ToLower() == "postgres")
        {
            if (string.IsNullOrEmpty(_settings.Postgres.ConnectionString))
            {
                throw new InvalidOperationException("PostgreSQL connection string is required when database type is 'postgres'");
            }

            var (isConnected, errorMessage) = await TestPostgresConnectionAsync(cancellationToken);

            if (isConnected)
            {
                Console.WriteLine("SUCCESS: PostgreSQL connection successful - using PostgreSQL database");
                Console.WriteLine($"{new string('=', 60)}");
                return new PostgresRoverDataRepository(_settings.Postgres.ConnectionString);
            }
            else
            {
                Console.WriteLine($"ERROR: PostgreSQL connection failed: {errorMessage}");
                Console.WriteLine();
                Console.WriteLine("NETWORK TROUBLESHOOTING:");
                Console.WriteLine($"- Check if PostgreSQL server is running");
                Console.WriteLine("- Verify network connectivity to the database server");
                Console.WriteLine("- Check firewall settings on both client and server");
                Console.WriteLine("- Ensure PostgreSQL is configured to accept connections");
                Console.WriteLine("- Verify credentials and database permissions");
                Console.WriteLine();
                Console.WriteLine("DATABASE CONFIGURATION OPTIONS:");
                Console.WriteLine("1. Fix the PostgreSQL connection issue above");
                Console.WriteLine("2. Change Database.Type to 'geopackage' in appsettings.json");
                Console.WriteLine("3. Update the connection string in appsettings.json");
                Console.WriteLine($"{new string('=', 60)}");

                throw new InvalidOperationException($"PostgreSQL connection failed: {errorMessage}. Please fix the connection issue or change to a different database type.");
            }
        }
        else if (_settings.Type.ToLower() == "geopackage")
        {
            Console.WriteLine("SUCCESS: Using GeoPackage database (local file storage)");
            Console.WriteLine($"Folder: {_settings.GeoPackage.FolderPath}");
            Console.WriteLine($"{new string('=', 60)}");
            return new GeoPackageRoverDataRepository(_settings.GeoPackage.FolderPath);
        }
        else
        {
            throw new ArgumentException($"Unsupported database type: {_settings.Type}. Use 'postgres' or 'geopackage'.");
        }
    }

    /// <summary>
    /// Tests PostgreSQL database connectivity with timeout and retry logic.
    /// </summary>
    private async Task<(bool isConnected, string errorMessage)> TestPostgresConnectionAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Testing PostgreSQL connection...");
        Console.WriteLine($"Target: {GetPostgresServerInfo()}");
        Console.WriteLine($"Timeout: {_settings.Connection.TimeoutSeconds} seconds");

        for (int attempt = 1; attempt <= _settings.Connection.MaxRetryAttempts; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(_settings.Connection.TimeoutSeconds));

                using var testRepo = new PostgresRoverDataRepository(_settings.Postgres.ConnectionString);

                Console.WriteLine($"  Attempt {attempt}/{_settings.Connection.MaxRetryAttempts}: Connecting...");

                await testRepo.InitializeAsync(cts.Token);

                Console.WriteLine("  SUCCESS: PostgreSQL connection established!");
                return (true, string.Empty);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return (false, "Connection test cancelled by user");
            }
            catch (OperationCanceledException)
            {
                var message = $"  TIMEOUT: Connection attempt {attempt} timed out after {_settings.Connection.TimeoutSeconds} seconds";
                Console.WriteLine(message);

                if (attempt == _settings.Connection.MaxRetryAttempts)
                {
                    return (false, $"Connection failed: All {_settings.Connection.MaxRetryAttempts} attempts timed out after {_settings.Connection.TimeoutSeconds} seconds each");
                }
            }
            catch (Exception ex)
            {
                var message = $"  ERROR: Connection attempt {attempt} failed: {ex.Message}";
                Console.WriteLine(message);

                if (ex.Message.Contains("authentication failed") ||
                    ex.Message.Contains("database") && ex.Message.Contains("does not exist") ||
                    ex.Message.Contains("permission denied"))
                {
                    return (false, $"Connection failed: {ex.Message}");
                }

                if (attempt == _settings.Connection.MaxRetryAttempts)
                {
                    return (false, $"Connection failed after {_settings.Connection.MaxRetryAttempts} attempts: {ex.Message}");
                }
            }

            if (attempt < _settings.Connection.MaxRetryAttempts)
            {
                Console.WriteLine($"  Waiting {_settings.Connection.RetryDelayMs}ms before retry...");
                await Task.Delay(_settings.Connection.RetryDelayMs, cancellationToken);
            }
        }

        return (false, "Connection failed: Unknown error");
    }

    private string GetPostgresServerInfo()
    {
        try
        {
            var connectionString = _settings.Postgres.ConnectionString;
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
}