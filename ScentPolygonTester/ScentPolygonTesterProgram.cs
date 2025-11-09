/*
 The functionallity in this file is:
 - Minimal console tester to run ScentPolygonService and write a unified polygon to a GeoPackage.
 - Demonstrates async/await, events, and basic FOSS4G concepts (OGC GeoPackage).
 - Prompts user to select which session to monitor from available sessions.
 - NOW SESSION-AWARE: Filters PostgreSQL measurements by session_id.
 - Simplified per copilot-instructions.md (reduced logging and configuration noise).
*/

using MapPiloteGeopackageHelper; // FOSS4G: OGC GeoPackage helper
using Microsoft.Extensions.Configuration; // .NET configuration (appsettings.json)
using ScentPolygonLibrary;
using RoverData.Repository;
using Microsoft.Extensions.Options;
using System.Globalization; // Invariant formatting
using ScentPolygonTester; // Access TesterConfiguration
using Npgsql; // PostgreSQL for session queries

// ---- Minimal configuration bootstrap ----
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();
TesterConfiguration.Initialize(configuration);

Console.WriteLine("??????????????????????????????????????????????????????????");
Console.WriteLine("?    ScentPolygonTester - Session Monitor (v2)?");
Console.WriteLine("??????????????????????????????????????????????????????????");
Console.WriteLine();

// Cooperative cancellation (Ctrl+C)
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

// === SESSION SELECTION ===
Console.WriteLine(new string('=', 60));
Console.WriteLine("SESSION SELECTION");
Console.WriteLine(new string('=', 60));

var dbConfig = TesterConfiguration.CreateDatabaseConfig();
var (sessionName, sessionId) = await GetSessionInfoAsync(dbConfig, cts.Token);

Console.WriteLine($"\nMonitoring session: {sessionName}");
if (sessionId.HasValue)
{
    Console.WriteLine($"Session ID: {sessionId.Value}");
}
Console.WriteLine($"Output: {TesterConfiguration.OutputGeoPackagePath}");
Console.WriteLine(new string('=', 60));
Console.WriteLine();

double? lastUnifiedAreaM2 = null; // Track last unified polygon area to detect size changes

// Resolve forest file path once (used in update block)
string forestPath = FindForestFile();

// Create data repository with validation (GeoPackage or Postgres) - session-specific
RoverData.Repository.IRoverDataRepository dataRepository;
try
{
    // For PostgreSQL, create session-aware repository
    if (dbConfig.DatabaseType.ToLower() == "postgres")
    {
        // Create NpgsqlDataSource with NetTopologySuite support
        var dataSource = new NpgsqlDataSourceBuilder(dbConfig.PostgresConnectionString!)
            .UseNetTopologySuite()
            .Build();
        
        // Register or get session from database
        var sessionRepository = new SessionRepository(dataSource);
        var resolvedSessionId = await sessionRepository.RegisterOrGetSessionAsync(sessionName, cts.Token);
        
        var sessionContext = new ConsoleSessionContext(resolvedSessionId, sessionName);
        dataRepository = new PostgresRoverDataRepository(dataSource, sessionContext);
        await dataRepository.InitializeAsync(cts.Token);
  
        Console.WriteLine($"? PostgreSQL repository initialized for session: {sessionName}");
    }
    // For GeoPackage, point to the session-specific file
    else if (dbConfig.DatabaseType.ToLower() == "geopackage")
    {
        // Point to the session-specific GeoPackage folder
        var opts = Options.Create(new GeoPackageRepositoryOptions
        {
            FolderPath = dbConfig.GeoPackageFolderPath ?? ""
        });
        var sessionContext = new ConsoleSessionContext(Guid.NewGuid(), sessionName);
        dataRepository = new GeoPackageRoverDataRepository(opts, sessionContext);
        await dataRepository.InitializeAsync(cts.Token);
  
        Console.WriteLine($"? GeoPackage repository initialized for session: {sessionName}");
    }
    else
    {
        Console.WriteLine($"ERROR: Unsupported database type: {dbConfig.DatabaseType}");
        return;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Data source unavailable: {ex.Message}");
    return;
}

// Scent model parameters
var scentConfig = new ScentPolygonConfiguration
{
    OmnidirectionalRadiusMeters = 30.0,
    FanPolygonPoints = 15,
    MinimumDistanceMultiplier = 0.4
};

// Create memory cache and polygon generator
var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
var generator = new ScentPolygonGenerator(dataRepository, memoryCache, scentConfig, forestPath);
using var geoPackageUpdater = new GeoPackageUpdater(TesterConfiguration.OutputGeoPackagePath);
await geoPackageUpdater.InitializeAsync();

Console.WriteLine("✓ Generator initialized. Monitoring session with polling loop...");
Console.WriteLine("  Press Ctrl+C to stop.");
Console.WriteLine();
Console.WriteLine("✓ GeoPackage output structure:");
Console.WriteLine("  - Per-rover layers: <rover_name> (one layer per rover)");
Console.WriteLine("  - Combined layer: combined_all_rovers (all rovers unified)");
Console.WriteLine($"  - Output file: {TesterConfiguration.OutputGeoPackagePath}");
Console.WriteLine();

// Simple polling loop (checks every second)
int updateCount = 0;
try
{
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            // Get unified polygon (cached for 1 second)
            var unified = await generator.GetUnifiedPolygonAsync(cts.Token);
            
            if (unified != null && unified.IsValid)
            {
                var currentArea = unified.TotalAreaM2;
                
                // Update GeoPackage if area changed significantly
                if (lastUnifiedAreaM2 is null || Math.Abs(currentArea - lastUnifiedAreaM2.Value) > 0.5)
                {
                    lastUnifiedAreaM2 = currentArea;
                    updateCount++;
                    
                    // Update GeoPackage layers
                    await geoPackageUpdater.UpdateAllRoverPolygonsAsync(generator);
                    
                    // Calculate forest coverage
                    var (intersectM2, forestM2) = await generator.GetForestIntersectionAreasAsync(cts.Token);
                    int areaCoveredPercent = forestM2 > 0 ? (int)Math.Round(((double)intersectM2 / forestM2) * 100) : 0;
                    
                    Console.WriteLine($"\n[Update #{updateCount}] Coverage Statistics:");
                    Console.WriteLine($"  Unified scent area:    {currentArea:n0} m²");
                    Console.WriteLine($"  RiverHead forest:      {forestM2:n0} m²");
                    Console.WriteLine($"  Intersection:          {intersectM2:n0} m²");
                    Console.WriteLine($"  Forest covered:        {areaCoveredPercent}%");
                    Console.WriteLine($"  Total polygons:        {unified.PolygonCount}");
                    Console.WriteLine($"  Avg wind speed:        {unified.AverageWindSpeedMps:F1} m/s");
                    Console.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Error in polling loop: {ex.Message}");
        }
        
        // Poll every second
        await Task.Delay(1000, cts.Token);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\n✓ Monitoring stopped by user.");
}

Console.WriteLine("\n? Cleanup complete.");

// ---- Helper Functions ----

static async Task<(string sessionName, Guid? sessionId)> GetSessionInfoAsync(DatabaseConfiguration dbConfig, CancellationToken cancellationToken)
{
    var sessionData = await ListAvailableSessionsAsync(dbConfig, cancellationToken);

    if (!sessionData.Any())
    {
   Console.WriteLine();
 Console.WriteLine("? No existing sessions found.");
        Console.WriteLine("  Please start a RoverSimulator first to create a session.");
        Console.WriteLine();
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
        Environment.Exit(0);
 }

    Console.WriteLine();
    Console.Write("Enter session name or number to monitor: ");
    
    string? input = Console.ReadLine()?.Trim();
    
    if (string.IsNullOrEmpty(input))
    {
   Console.WriteLine("No input provided. Using most recent session...");
        return sessionData.First();
    }

    // Check if input is a number
    if (int.TryParse(input, out int sessionNumber) && sessionNumber >= 1 && sessionNumber <= sessionData.Count)
    {
        return sessionData[sessionNumber - 1];
    }

    // Check if input matches an existing session name
    var matchingSession = sessionData.FirstOrDefault(s => 
        s.sessionName.Equals(input, StringComparison.OrdinalIgnoreCase));
    
    if (matchingSession != default)
    {
        return matchingSession;
    }

    Console.WriteLine($"Session '{input}' not found. Using most recent session...");
    return sessionData.First();
}

static async Task<List<(string sessionName, Guid? sessionId)>> ListAvailableSessionsAsync(DatabaseConfiguration dbConfig, CancellationToken cancellationToken)
{
    var sessions = new List<(string sessionName, Guid? sessionId)>();

    Console.WriteLine();
    Console.WriteLine($"Database: {dbConfig.DatabaseType.ToUpper()}");

    if (dbConfig.DatabaseType.ToLower() == "postgres")
    {
        try
        {
         using var dataSource = new NpgsqlDataSourceBuilder(dbConfig.PostgresConnectionString)
 .UseNetTopologySuite()
                .Build();
    
         await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

            const string sql = @"
      SELECT rs.session_name, 
  rs.session_id,
        COUNT(rp.id) as measurement_count,
             MAX(rp.recorded_at) as last_measurement
      FROM roverdata.rover_sessions rs
  LEFT JOIN roverdata.rover_points rp ON rs.session_id = rp.session_id
  GROUP BY rs.session_name, rs.session_id
            ORDER BY MAX(rp.recorded_at) DESC NULLS LAST;";

        await using var cmd = new NpgsqlCommand(sql, conn);
 await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            Console.WriteLine($"\nAvailable sessions:");
     int index = 1;
 while (await reader.ReadAsync(cancellationToken))
            {
             var sessionName = reader.GetString(0);
        var sessionId = reader.GetGuid(1);
     var count = reader.GetInt64(2);
       var lastMeasurement = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);
        
    sessions.Add((sessionName, sessionId));
         
             var lastMeasurementStr = lastMeasurement.HasValue 
         ? lastMeasurement.Value.ToString("yyyy-MM-dd HH:mm:ss")
      : "No measurements yet";
      
    Console.WriteLine($"  {index}. {sessionName}");
        Console.WriteLine($"     ?? {count} measurements, last: {lastMeasurementStr}");
  index++;
}
        }
  catch (Exception ex)
        {
        Console.WriteLine($"? Warning: Could not list PostgreSQL sessions: {ex.Message}");
        }
    }
    else if (dbConfig.DatabaseType.ToLower() == "geopackage")
    {
        var folderPath = dbConfig.GeoPackageFolderPath;
    
    if (!Directory.Exists(folderPath))
        {
            Console.WriteLine($"? Warning: GeoPackage folder not found: {folderPath}");
  return sessions;
 }

        var geoPackageFiles = Directory.GetFiles(folderPath, "session_*.gpkg");

        Console.WriteLine($"\nAvailable sessions ({geoPackageFiles.Length}):");
        int index = 1;
foreach (var file in geoPackageFiles.OrderByDescending(f => new FileInfo(f).LastWriteTime))
   {
            var fileName = Path.GetFileNameWithoutExtension(file);
      var sessionName = fileName.Replace("session_", "");
      sessions.Add((sessionName, null)); // GeoPackage doesn't need session_id
 
            var fileInfo = new FileInfo(file);
       Console.WriteLine($"  {index}. {sessionName}");
    Console.WriteLine($"     ?? {fileInfo.Length / 1024.0:F1} KB, modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
       index++;
     }
    }

    return sessions;
}

static string FindForestFile()
{
    // Walk up from base directory and look for Solutionresources/RiverHeadForest.gpkg
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
  var candidate = Path.Combine(dir.FullName, "Solutionresources", "RiverHeadForest.gpkg");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    var fallbacks = new[]
    {
Path.Combine(Directory.GetCurrentDirectory(), "Solutionresources", "RiverHeadForest.gpkg"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "Solutionresources", "RiverHeadForest.gpkg"),
    };
    return fallbacks.FirstOrDefault(File.Exists) ?? fallbacks[0];
}

/// <summary>
/// Enhanced GeoPackage updater: creates per-rover layers and a combined layer
/// </summary>
public sealed class GeoPackageUpdater : IDisposable
{
    private readonly string _path;
    private GeoPackage? _gpkg;
    private readonly Dictionary<string, GeoPackageLayer> _roverLayers = new(); // Keyed by rover name
    private GeoPackageLayer? _combinedLayer;
    private bool _disposed;

 public GeoPackageUpdater(string path) => _path = path;

    public async Task InitializeAsync()
{
   var dir = Path.GetDirectoryName(_path);
   if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(_path)) { try { File.Delete(_path); } catch { } }

 _gpkg = await GeoPackage.OpenAsync(_path, 4326); // EPSG:4326
        
    // Schema for all layers (per-rover and combined)
        var schema = new Dictionary<string, string>
     {
         ["rover_name"] = "TEXT NOT NULL",
     ["polygon_count"] = "INTEGER NOT NULL",
            ["total_area_m2"] = "REAL NOT NULL",
          ["latest_sequence"] = "INTEGER NOT NULL",
            ["earliest_time"] = "TEXT NOT NULL",
            ["latest_time"] = "TEXT NOT NULL",
 ["created_at"] = "TEXT NOT NULL"
     };
        
     // Create the combined layer upfront
        _combinedLayer = await _gpkg.EnsureLayerAsync("combined_all_rovers", schema, 4326, "POLYGON");
     
        Console.WriteLine("? GeoPackage initialized with multi-rover support");
    }

    /// <summary>
    /// Updates all rover-specific layers and the combined layer
    /// </summary>
    public async Task UpdateAllRoverPolygonsAsync(ScentPolygonGenerator generator)
 {
    if (_disposed || _gpkg == null) return;

  // Get all rover-specific unified polygons
        var roverPolygons = await generator.GetRoverUnifiedPolygonsAsync();
        
    if (!roverPolygons.Any()) return;

   Console.WriteLine($"?? Updating {roverPolygons.Count} rover layers...");

 // Update each rover's individual layer
        int successCount = 0;
        int failCount = 0;
        
     foreach (var roverPolygon in roverPolygons)
     {
      if (!roverPolygon.IsValid) 
      {
         Console.WriteLine($"?? Skipping invalid polygon for rover {roverPolygon.RoverName}");
 continue;
            }

            // Get or create layer for this rover (sanitize name for layer naming)
 var layerName = SanitizeLayerName(roverPolygon.RoverName);
 
       if (!_roverLayers.ContainsKey(layerName))
      {
   var schema = new Dictionary<string, string>
        {
        ["rover_name"] = "TEXT NOT NULL",
 ["rover_id"] = "TEXT NOT NULL",
 ["polygon_count"] = "INTEGER NOT NULL",
 ["total_area_m2"] = "REAL NOT NULL",
        ["latest_sequence"] = "INTEGER NOT NULL",
  ["earliest_time"] = "TEXT NOT NULL",
   ["latest_time"] = "TEXT NOT NULL",
      ["version"] = "INTEGER NOT NULL",
   ["created_at"] = "TEXT NOT NULL"
  };
     
      var layer = await _gpkg.EnsureLayerAsync(layerName, schema, 4326, "POLYGON");
         _roverLayers[layerName] = layer;
   Console.WriteLine($"  ? Created layer: {layerName}");
      }

   // Update this rover's layer with exception handling
       try
            {
      await UpdateRoverLayerAsync(_roverLayers[layerName], roverPolygon);
         successCount++;
      Console.WriteLine($"  ? Updated {layerName} (v{roverPolygon.Version}, seq {roverPolygon.LatestSequence}, {roverPolygon.PolygonCount} polygons)");
 }
    catch (Exception ex)
{
       failCount++;
     Console.WriteLine($"  ? Failed to update {layerName}: {ex.Message}");
     Console.WriteLine($"     Stack trace: {ex.StackTrace}");
       // Continue with other rovers instead of failing completely
 }
 }

        Console.WriteLine($"?? Rover layers update: {successCount} succeeded, {failCount} failed");

        // Update combined layer
      try
        {
       await UpdateCombinedLayerAsync(generator);
      Console.WriteLine($"  ? Updated combined layer");
 }
 catch (Exception ex)
        {
    Console.WriteLine($"  ? Failed to update combined layer: {ex.Message}");
        }
        
     // Verify the update by reading back from GeoPackage
        await VerifyLayerUpdatesAsync();
    }

    /// <summary>
    /// Verifies that all rover layers have been updated correctly
    /// </summary>
    private async Task VerifyLayerUpdatesAsync()
    {
        try
        {
     Console.WriteLine("?? Verifying layer updates...");
   
    foreach (var (layerName, layer) in _roverLayers)
          {
      var count = await layer.CountAsync();
      Console.WriteLine($"  ?? {layerName}: {count} feature(s)");
     
              if (count == 0)
      {
           Console.WriteLine($"    ?? WARNING: Layer {layerName} has no features!");
     }
     else if (count > 1)
       {
          Console.WriteLine($"    ?? WARNING: Layer {layerName} has {count} features (expected 1)!");
       }
            }
            
   if (_combinedLayer != null)
      {
        var combinedCount = await _combinedLayer.CountAsync();
   Console.WriteLine($"  ?? combined_all_rovers: {combinedCount} feature(s)");
     }
        }
   catch (Exception ex)
        {
            Console.WriteLine($"?? Verification failed: {ex.Message}");
        }
    }

    private async Task UpdateRoverLayerAsync(GeoPackageLayer layer, RoverUnifiedPolygon roverPolygon)
    {
        try
    {
    await layer.DeleteAsync("1=1"); // Clear existing feature
        }
 catch (Exception ex)
        {
     Console.WriteLine($"?? Warning: Could not clear existing features for {roverPolygon.RoverName}: {ex.Message}");
            // Continue anyway - insert might still work
        }
        
        var attrs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
    ["rover_name"] = roverPolygon.RoverName,
         ["rover_id"] = roverPolygon.RoverId.ToString(),
       ["polygon_count"] = roverPolygon.PolygonCount.ToString(CultureInfo.InvariantCulture),
        ["total_area_m2"] = roverPolygon.TotalAreaM2.ToString("F2", CultureInfo.InvariantCulture),
   ["latest_sequence"] = roverPolygon.LatestSequence.ToString(CultureInfo.InvariantCulture),
 ["earliest_time"] = roverPolygon.EarliestMeasurement.ToString("O"),
        ["latest_time"] = roverPolygon.LatestMeasurement.ToString("O"),
  ["version"] = roverPolygon.Version.ToString(CultureInfo.InvariantCulture),
 ["created_at"] = DateTimeOffset.UtcNow.ToString("O")
        };
   
        var feature = new FeatureRecord(roverPolygon.UnifiedPolygon, attrs);
        
        try
        {
      await layer.BulkInsertAsync(
          new[] { feature }, 
  new BulkInsertOptions(BatchSize: 1, CreateSpatialIndex: true, ConflictPolicy: ConflictPolicy.Replace), 
     null, 
        CancellationToken.None);
   }
    catch (Exception)
        {
            Console.WriteLine($"? Insert failed for {roverPolygon.RoverName}:");
        Console.WriteLine($"   Polygon valid: {roverPolygon.UnifiedPolygon?.IsValid}");
  Console.WriteLine($"   Polygon SRID: {roverPolygon.UnifiedPolygon?.SRID}");
            Console.WriteLine($"   Polygon points: {roverPolygon.UnifiedPolygon?.NumPoints}");
            throw; // Re-throw to be caught by caller
     }
    }

    private async Task UpdateCombinedLayerAsync(ScentPolygonGenerator generator)
    {
  if (_combinedLayer == null) return;

        var unified = await generator.GetUnifiedPolygonAsync();
        if (unified == null || !unified.IsValid) return;

        await _combinedLayer.DeleteAsync("1=1"); // Clear existing
        
     var roverNames = string.Join(", ", unified.RoverNames.Distinct());
        
        var attrs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
          ["rover_name"] = roverNames, // All rover names combined
            ["polygon_count"] = unified.PolygonCount.ToString(CultureInfo.InvariantCulture),
      ["total_area_m2"] = unified.TotalAreaM2.ToString("F2", CultureInfo.InvariantCulture),
     ["latest_sequence"] = "-1", // Not applicable for combined
            ["earliest_time"] = unified.EarliestMeasurement.ToString("O"),
      ["latest_time"] = unified.LatestMeasurement.ToString("O"),
            ["created_at"] = DateTimeOffset.UtcNow.ToString("O")
        };
        
        var feature = new FeatureRecord(unified.Polygon, attrs);
        await _combinedLayer.BulkInsertAsync(
         new[] { feature }, 
    new BulkInsertOptions(BatchSize: 1, CreateSpatialIndex: true, ConflictPolicy: ConflictPolicy.Replace), 
         null, 
 CancellationToken.None);
    }

    private static string SanitizeLayerName(string roverName)
    {
        // GeoPackage layer names: alphanumeric + underscore, start with letter
        var sanitized = new string(roverName
   .Select(c => char.IsLetterOrDigit(c) ? c : '_')
  .ToArray());
     
      // Ensure it starts with a letter
        if (!char.IsLetter(sanitized[0]))
      {
          sanitized = "rover_" + sanitized;
        }
        
        return sanitized.ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed) return; 
        _disposed = true;
        try 
        { 
            _roverLayers.Clear();
  _combinedLayer = null; 
     _gpkg?.Dispose(); 
        } 
    catch { }
    }
}
