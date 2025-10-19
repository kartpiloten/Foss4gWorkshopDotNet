using MapPiloteGeopackageHelper;
using Microsoft.Extensions.Configuration;
using ReadRoverDBStubLibrary;
using ScentPolygonLibrary;
using System.Globalization;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

TesterConfiguration.Initialize(configuration);

Console.WriteLine("========================================");
Console.WriteLine("     SCENT POLYGON SERVICE TESTER");
Console.WriteLine("     WITH GEOPACKAGE OUTPUT");
Console.WriteLine("========================================");
Console.WriteLine($"Database type preference: {TesterConfiguration.DefaultDatabaseType.ToUpper()}");
Console.WriteLine($"Output GeoPackage: {TesterConfiguration.OutputGeoPackagePath}");
Console.WriteLine();

var cts = new CancellationTokenSource();
Console.CancelKeyPress += HandleCancelKeyPress;

void HandleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\nShutdown requested...");
}

// Create database configuration
var databaseConfig = TesterConfiguration.CreateDatabaseConfig();
IRoverDataReader? dataReader = null;
ScentPolygonService? scentService = null;
GeoPackageUpdater? geoPackageUpdater = null;

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

    // Initialize GeoPackage updater with file locking handling
    geoPackageUpdater = new GeoPackageUpdater(TesterConfiguration.OutputGeoPackagePath);
    await geoPackageUpdater.InitializeAsync();

    // Subscribe to polygon updates
    scentService.PolygonsUpdated += async (sender, args) =>
    {
        Console.WriteLine($"[GeoPackage] Polygon update received: {args.NewPolygons.Count} new polygons, total: {args.TotalPolygonCount}");
        await geoPackageUpdater.UpdateUnifiedPolygonAsync(scentService);
    };

    // Subscribe to status updates for regular console output
    scentService.StatusUpdate += (sender, args) =>
    {
        Console.WriteLine($"[Status] Total polygons: {args.TotalPolygonCount}, Latest: {args.LatestPolygon?.Sequence ?? -1}, Source: {args.DataSource}");
    };

    Console.WriteLine("Initializing scent polygon service...");
    await scentService.StartAsync(cts.Token);

    // Wait for initial polygons to load
    await Task.Delay(2000, cts.Token);

    // Create initial unified polygon in GeoPackage
    Console.WriteLine("Creating initial unified polygon in GeoPackage...");
    await geoPackageUpdater.UpdateUnifiedPolygonAsync(scentService);

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

    Console.WriteLine($"\nGeoPackage output: {TesterConfiguration.OutputGeoPackagePath}");
    Console.WriteLine("Layer name: unified");
    Console.WriteLine("The GeoPackage will be updated automatically as new rover data arrives.");
    Console.WriteLine("\nNOTE: If the file is locked by QGIS or another application, updates will be skipped.");
    Console.WriteLine("Close QGIS to allow updates, or use a different filename.");
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
    if (ex.StackTrace != null)
    {
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
    }
}
finally
{
    if (geoPackageUpdater != null)
    {
        geoPackageUpdater.Dispose();
    }
    
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

/// <summary>
/// Handles GeoPackage creation and updating with unified scent polygons
/// Implements file locking handling and WAL mode considerations
/// </summary>
public class GeoPackageUpdater : IDisposable
{
    private readonly string _geoPackagePath;
    private GeoPackage? _geoPackage;
    private GeoPackageLayer? _unifiedLayer;
    private bool _disposed;
    private readonly object _updateLock = new();
    private int _updateAttempts = 0;

    public GeoPackageUpdater(string geoPackagePath)
    {
        _geoPackagePath = geoPackagePath;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Ensure output directory exists
            var directory = Path.GetDirectoryName(_geoPackagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Console.WriteLine($"[GeoPackage] Created directory: {directory}");
            }

            // Check if file is locked and handle accordingly
            if (File.Exists(_geoPackagePath))
            {
                if (IsFileLocked(_geoPackagePath))
                {
                    Console.WriteLine($"[GeoPackage] File is locked by another process: {Path.GetFileName(_geoPackagePath)}");
                    Console.WriteLine($"[GeoPackage] This is likely QGIS or another GIS application.");
                    Console.WriteLine($"[GeoPackage] Will attempt to work with existing file when possible.");
                    
                    // Try to open existing file
                    try
                    {
                        _geoPackage = await GeoPackage.OpenAsync(_geoPackagePath, 4326);
                        Console.WriteLine($"[GeoPackage] Opened existing GeoPackage: {Path.GetFileName(_geoPackagePath)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GeoPackage] Cannot access locked file: {ex.Message}");
                        // Create alternative filename with timestamp
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var directory2 = Path.GetDirectoryName(_geoPackagePath);
                        var filename = Path.GetFileNameWithoutExtension(_geoPackagePath);
                        var extension = Path.GetExtension(_geoPackagePath);
                        var alternativePath = Path.Combine(directory2!, $"{filename}_{timestamp}{extension}");
                        
                        Console.WriteLine($"[GeoPackage] Creating alternative file: {Path.GetFileName(alternativePath)}");
                        _geoPackage = await GeoPackage.OpenAsync(alternativePath, 4326);
                        // Update the path for future reference
                        typeof(GeoPackageUpdater).GetField("_geoPackagePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(this, alternativePath);
                    }
                }
                else
                {
                    // File exists but not locked - delete and recreate
                    try
                    {
                        File.Delete(_geoPackagePath);
                        Console.WriteLine($"[GeoPackage] Deleted existing file: {Path.GetFileName(_geoPackagePath)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GeoPackage] Could not delete existing file: {ex.Message}");
                    }
                    
                    _geoPackage = await GeoPackage.OpenAsync(_geoPackagePath, 4326);
                    Console.WriteLine($"[GeoPackage] Created new GeoPackage: {Path.GetFileName(_geoPackagePath)}");
                }
            }
            else
            {
                // Create new file
                _geoPackage = await GeoPackage.OpenAsync(_geoPackagePath, 4326);
                Console.WriteLine($"[GeoPackage] Created new GeoPackage: {Path.GetFileName(_geoPackagePath)}");
            }

            // Define schema for unified polygon layer
            var schema = new Dictionary<string, string>
            {
                ["polygon_count"] = "INTEGER NOT NULL",
                ["total_area_m2"] = "REAL NOT NULL",
                ["total_area_hectares"] = "REAL NOT NULL",
                ["coverage_efficiency"] = "REAL NOT NULL",
                ["average_wind_speed_mps"] = "REAL NOT NULL",
                ["min_wind_speed_mps"] = "REAL NOT NULL",
                ["max_wind_speed_mps"] = "REAL NOT NULL",
                ["earliest_measurement"] = "TEXT NOT NULL",
                ["latest_measurement"] = "TEXT NOT NULL",
                ["session_count"] = "INTEGER NOT NULL",
                ["vertex_count"] = "INTEGER NOT NULL",
                ["created_at"] = "TEXT NOT NULL",
                ["unified_version"] = "INTEGER NOT NULL"
            };

            // Create or ensure the unified layer exists
            _unifiedLayer = await _geoPackage.EnsureLayerAsync("unified", schema, 4326, "POLYGON");
            Console.WriteLine($"[GeoPackage] Ensured 'unified' layer exists with proper schema");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GeoPackage] Error during initialization: {ex.Message}");
            throw;
        }
    }

    public async Task UpdateUnifiedPolygonAsync(ScentPolygonService scentService)
    {
        if (_disposed || _geoPackage == null || _unifiedLayer == null)
            return;

        // Use task-based approach to avoid blocking
        await Task.Run(() =>
        {
            lock (_updateLock)
            {
                try
                {
                    // Get the unified polygon
                    var unified = scentService.GetUnifiedScentPolygonCached();
                    if (unified == null || !unified.IsValid)
                    {
                        Console.WriteLine("[GeoPackage] No valid unified polygon available - skipping update");
                        return;
                    }

                    _updateAttempts++;
                    Console.WriteLine($"[GeoPackage] Updating unified polygon (attempt {_updateAttempts}): {unified.PolygonCount} polygons, {unified.TotalAreaM2:F0} m², version {scentService.UnifiedVersion}");

                    // Clear existing data and insert new
                    Task.Run(async () =>
                    {
                        try
                        {
                            // Clear existing records
                            await _unifiedLayer.DeleteAsync("1=1");

                            // Create feature record
                            var featureRecord = new FeatureRecord(
                                unified.Polygon,
                                new Dictionary<string, string?>(StringComparer.Ordinal)
                                {
                                    ["polygon_count"] = unified.PolygonCount.ToString(CultureInfo.InvariantCulture),
                                    ["total_area_m2"] = unified.TotalAreaM2.ToString("F2", CultureInfo.InvariantCulture),
                                    ["total_area_hectares"] = (unified.TotalAreaM2 / 10000.0).ToString("F4", CultureInfo.InvariantCulture),
                                    ["coverage_efficiency"] = unified.CoverageEfficiency.ToString("F4", CultureInfo.InvariantCulture),
                                    ["average_wind_speed_mps"] = unified.AverageWindSpeedMps.ToString("F2", CultureInfo.InvariantCulture),
                                    ["min_wind_speed_mps"] = unified.WindSpeedRange.Min.ToString("F2", CultureInfo.InvariantCulture),
                                    ["max_wind_speed_mps"] = unified.WindSpeedRange.Max.ToString("F2", CultureInfo.InvariantCulture),
                                    ["earliest_measurement"] = unified.EarliestMeasurement.ToString("O"),
                                    ["latest_measurement"] = unified.LatestMeasurement.ToString("O"),
                                    ["session_count"] = unified.SessionIds.Count.ToString(CultureInfo.InvariantCulture),
                                    ["vertex_count"] = unified.VertexCount.ToString(CultureInfo.InvariantCulture),
                                    ["created_at"] = DateTimeOffset.UtcNow.ToString("O"),
                                    ["unified_version"] = scentService.UnifiedVersion.ToString(CultureInfo.InvariantCulture)
                                }
                            );

                            // Insert new record
                            await _unifiedLayer.BulkInsertAsync(
                                new[] { featureRecord },
                                new BulkInsertOptions(BatchSize: 1, CreateSpatialIndex: true, ConflictPolicy: ConflictPolicy.Replace),
                                null,
                                CancellationToken.None
                            );

                            Console.WriteLine($"[GeoPackage] ? Updated unified polygon: {unified.TotalAreaM2 / 10000.0:F2} hectares, {unified.VertexCount} vertices");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[GeoPackage] Error during database update: {ex.Message}");
                            if (ex.Message.Contains("locked") || ex.Message.Contains("busy"))
                            {
                                Console.WriteLine($"[GeoPackage] Database is locked - skipping this update (will retry on next update)");
                            }
                        }
                    }).Wait(5000); // Wait max 5 seconds for the update
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GeoPackage] Error updating unified polygon: {ex.Message}");
                }
            }
        });
    }

    /// <summary>
    /// Checks if a file is locked by another process
    /// </summary>
    private static bool IsFileLocked(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false; // File is not locked
        }
        catch (IOException)
        {
            return true; // File is locked
        }
        catch
        {
            return false; // Other errors, assume not locked
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _unifiedLayer = null;
            _geoPackage?.Dispose();
            
            if (File.Exists(_geoPackagePath))
            {
                var fileInfo = new FileInfo(_geoPackagePath);
                Console.WriteLine($"[GeoPackage] Final stats:");
                Console.WriteLine($"  File: {fileInfo.Name}");
                Console.WriteLine($"  Size: {fileInfo.Length / 1024.0:F1} KB");
                Console.WriteLine($"  Location: {fileInfo.DirectoryName}");
                Console.WriteLine($"  Total updates: {_updateAttempts}");
                Console.WriteLine($"  You can open this file in QGIS or other GIS software!");
                
                // Check for WAL and SHM files (SQLite Write-Ahead Logging files)
                var walFile = _geoPackagePath + "-wal";
                var shmFile = _geoPackagePath + "-shm";
                if (File.Exists(walFile))
                {
                    Console.WriteLine($"  WAL file: {new FileInfo(walFile).Length / 1024.0:F1} KB");
                }
                if (File.Exists(shmFile))
                {
                    Console.WriteLine($"  SHM file: {new FileInfo(shmFile).Length / 1024.0:F1} KB");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GeoPackage] Error during disposal: {ex.Message}");
        }
    }
}

// ===== APPLICATION CONFIGURATION =====
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

    public static string OutputFolderPath =>
        Configuration.GetValue<string>("Tester:OutputFolderPath") ?? GeoPackageFolderPath;

    public static string OutputGeoPackageFilename =>
        Configuration.GetValue<string>("Tester:OutputGeoPackageFilename") ?? "ScentPolygons.gpkg";

    public static string OutputGeoPackagePath =>
        Configuration.GetValue<string>("Tester:OutputGeoPackagePath") ??
        Path.Combine(OutputFolderPath, OutputGeoPackageFilename);

    public static DatabaseConfiguration CreateDatabaseConfig(string? databaseType = null)
    {
        var section = Configuration.GetSection("DatabaseConfiguration");
        var dbType = databaseType ?? section.GetValue<string>("DatabaseType") ?? "geopackage";

        return new DatabaseConfiguration
        {
            DatabaseType = dbType,
            PostgresConnectionString = section.GetValue<string>("PostgresConnectionString"),
            GeoPackageFolderPath = GeoPackageFolderPath,
            ConnectionTimeoutSeconds = section.GetValue<int?>("ConnectionTimeoutSeconds") ?? ReaderDefaults.DEFAULT_CONNECTION_TIMEOUT_SECONDS,
            MaxRetryAttempts = section.GetValue<int?>("MaxRetryAttempts") ?? ReaderDefaults.DEFAULT_MAX_RETRY_ATTEMPTS,
            RetryDelayMs = section.GetValue<int?>("RetryDelayMs") ?? ReaderDefaults.DEFAULT_RETRY_DELAY_MS
        };
    }
}
