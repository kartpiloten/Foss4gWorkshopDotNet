/*
 The functionallity in this file is:
 - Bootstrap the simulator: load appsettings.json, wire DI, and run the async simulation loop.
 - Demonstrate .NET Options/DI patterns and CancellationToken handling in a simple console app.
 - Support GeoPackage or Postgres repositories and a '--verify' utility for GeoPackage contents.
*/

using Microsoft.Extensions.Configuration; // .NET configuration binding (appsettings.json)
using Microsoft.Extensions.DependencyInjection; // .NET Dependency Injection container
using Microsoft.Extensions.Options; // Options pattern (IOptions<T>) for typed settings
using NetTopologySuite.Geometries; // NTS Point used for positions (FOSS4G-friendly)
using ReadRoverDBStubLibrary; // Add reference to the library
using RoverSimulator.Configuration;
using RoverSimulator.Services;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Npgsql; // PostgreSQL ADO.NET provider for session table queries

namespace RoverSimulator;

class RoverSimulatorProgram
{
    public static async Task Main(string[] args)
    {
        // Configuration: load appsettings.json (kept minimal)
        // Handle different working directories (run from solution root or project directory)
   var appSettingsPath = File.Exists("appsettings.json") 
            ? Path.GetFullPath("appsettings.json")
     : Path.GetFullPath(Path.Combine("RoverSimulator", "appsettings.json"));

 if (!File.Exists(appSettingsPath))
        {
  Console.WriteLine($"ERROR: appsettings.json not found!");
   Console.WriteLine($"Looked in: {appSettingsPath}");
            Console.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
            Console.WriteLine();
       Console.WriteLine("Please ensure appsettings.json exists in the RoverSimulator project directory.");
        return;
 }

var configuration = new ConfigurationBuilder()
         .SetBasePath(Path.GetDirectoryName(appSettingsPath)!)
       .AddJsonFile(Path.GetFileName(appSettingsPath), optional: false, reloadOnChange: true)
      .Build();

        // Dependency Injection: register typed options and services
        var services = new ServiceCollection();
      services.Configure<SimulatorSettings>(configuration);
   services.AddSingleton<DatabaseService>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<SimulatorSettings>>().Value;
   return new DatabaseService(settings.Database);
     });
      
        // Use ForestBoundaryReader from ReadRoverDBStubLibrary
 services.AddSingleton<ForestBoundaryReader>(provider =>
        {
  var settings = provider.GetRequiredService<IOptions<SimulatorSettings>>().Value;
            var geoPackagePath = Path.Combine(GetSolutionDirectory(), settings.Forest.BoundaryFile);
            return new ForestBoundaryReader(geoPackagePath, settings.Forest.LayerName);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Resolve settings and services from DI
        var settings = serviceProvider.GetRequiredService<IOptions<SimulatorSettings>>().Value;
        var databaseService = serviceProvider.GetRequiredService<DatabaseService>();
        var forestReader = serviceProvider.GetRequiredService<ForestBoundaryReader>();

        // Utility: verify existing GeoPackage and exit
        if (args.Length >0 && args.Contains("--verify", StringComparer.OrdinalIgnoreCase))
  {
            Console.WriteLine("Verification mode - listing available sessions...");
   await ListAvailableSessionsAsync(settings, showOutput: true, default);
       return;
        }

        // === SESSION SELECTION ===
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("ROVER SIMULATOR - SESSION SETUP");
      Console.WriteLine(new string('=', 60));

// Cancellation: Ctrl+C to stop
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
  {
            e.Cancel = true;
         cts.Cancel();
        };

        string sessionName = await GetSessionNameAsync(settings, cts.Token);

        Console.WriteLine($"\nSession: {sessionName}");

        // === ROVER SETUP ===
        Guid roverId = GetRoverId(args) ?? GetRoverIdFromEnv() ?? Guid.NewGuid();
  string roverName = await GetRoverNameAsync();

        Console.WriteLine($"Rover ID: {roverId}");
    Console.WriteLine($"Rover Name: {roverName}");
        Console.WriteLine(new string('=', 60));

        Console.WriteLine($"\nRover simulator starting with {settings.Database.Type.ToUpper()} database.");
      Console.WriteLine("Press Ctrl+C to stop. Press Ctrl+P to toggle progress output.");

        // Load forest boundary using ForestBoundaryReader
     Console.WriteLine("\nLoading forest boundary...");
        Polygon? forestPolygon = null;
  try
        {
        forestPolygon = await forestReader.GetBoundaryPolygonAsync(cts.Token);
     if (forestPolygon != null)
            {
   var bbox = await forestReader.GetBoundingBoxAsync(cts.Token);
     var centroid = await forestReader.GetCentroidAsync(cts.Token);
     Console.WriteLine("Forest boundary loaded successfully!");
      Console.WriteLine($"  Bounding box: ({bbox?.MinX:F6}, {bbox?.MinY:F6}) to ({bbox?.MaxX:F6}, {bbox?.MaxY:F6})");
    Console.WriteLine($"  Centroid: ({centroid?.X:F6}, {centroid?.Y:F6})");
            }
 else
        {
    Console.WriteLine("Warning: Could not load forest boundary.");
   Console.WriteLine("Using fallback coordinates...");
   }
        }
  catch (Exception ex)
      {
   Console.WriteLine($"Warning: Could not load forest boundary: {ex.Message}");
            Console.WriteLine("Using fallback coordinates...");
        }

        using var progressMonitor = new ProgressMonitor(cts);

 // Create repository with session name
    IRoverDataRepository repository;
        try
        {
   repository = await databaseService.CreateRepositoryAsync(sessionName, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Database setup cancelled by user.");
       return;
   }
  catch (Exception ex)
     {
     Console.WriteLine($"\nDatabase connection failed: {ex.Message}");
    Console.WriteLine("\nSimulation cannot proceed without a valid database connection.");
            Console.WriteLine("Please check your appsettings.json configuration and try again.");
   return;
        }

  // Run main loop
        try
        {
    await RunSimulationAsync(repository, settings, forestReader, forestPolygon, roverId, roverName, Guid.NewGuid(), cts.Token);
        }
    catch (OperationCanceledException)
        {
          Console.WriteLine("\nSimulation cancelled by user. Saving data and cleaning up...");
        }
     catch (Exception ex)
   {
            Console.WriteLine($"\nError during simulation: {ex.Message}");
      await HandleSimulationError(ex);
     }
        finally
        {
     Console.WriteLine("\nCleaning up and releasing file locks...");
         repository.Dispose();
            forestReader.Dispose();
  await Task.Delay(500);
        }

        Console.WriteLine("\nRover simulator stopped.");
        if (repository is GeoPackageRoverDataRepository)
        {
   Console.WriteLine($"Session '{sessionName}' GeoPackage file is now available for other applications.");
  }
     else
        {
   Console.WriteLine("Database connection closed.");
        }
    }

    private static async Task<string> GetSessionNameAsync(SimulatorSettings settings, CancellationToken cancellationToken)
    {
        // List existing sessions (database-type aware)
        var existingSessions = await ListAvailableSessionsAsync(settings, showOutput: true, cancellationToken);

        Console.WriteLine("\nWould you like to join an existing session or create a new one?");
      Console.Write("Type 'new' for a new session, or enter an existing session name: ");
        
        string? input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
        {
            Console.WriteLine("No input provided. Creating new session...");
   input = "new";
        }

        if (input.Equals("new", StringComparison.OrdinalIgnoreCase))
        {
      Console.Write("\nName the new session (alphanumeric, no spaces): ");
            string? newSessionName = Console.ReadLine()?.Trim();

      while (string.IsNullOrWhiteSpace(newSessionName) || !IsValidSessionName(newSessionName))
            {
                Console.WriteLine("Invalid session name. Use only letters, numbers, and underscores.");
   Console.Write("Name the new session: ");
                newSessionName = Console.ReadLine()?.Trim();
}

    return SanitizeSessionName(newSessionName!);
        }
        else
        {
       // Validate that the session exists
      if (existingSessions.Contains(input, StringComparer.OrdinalIgnoreCase))
   {
         return input;
    }
else
       {
                Console.WriteLine($"\nWarning: Session '{input}' not found. Creating new session with this name...");
       return SanitizeSessionName(input);
            }
   }
    }

    private static async Task<string> GetRoverNameAsync()
    {
    Console.Write("\nWhat is the rover dog's name? ");
        string? name = Console.ReadLine()?.Trim();

        while (string.IsNullOrWhiteSpace(name))
        {
 Console.WriteLine("Please provide a name for the rover dog.");
 Console.Write("Rover dog's name: ");
     name = Console.ReadLine()?.Trim();
        }

        return name!;
    }

    private static async Task<List<string>> ListAvailableSessionsAsync(SimulatorSettings settings, bool showOutput = false, CancellationToken cancellationToken = default)
    {
    var sessions = new List<string>();

    if (settings.Database.Type.ToLower() == "postgres")
{
       // Query PostgreSQL rover_sessions table
    try
  {
    using var dataSource = new NpgsqlDataSourceBuilder(settings.Database.Postgres.ConnectionString)
         .UseNetTopologySuite()
    .Build();
    
    await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

      // Query rover_sessions table for all session names with counts in single query
     const string sql = @"
SELECT rs.session_name, COUNT(rp.id) as measurement_count
        FROM roverdata.rover_sessions rs
        LEFT JOIN roverdata.rover_points rp ON rs.session_id = rp.session_id
      GROUP BY rs.session_name, rs.last_updated
    ORDER BY rs.last_updated DESC;";

      await using var cmd = new NpgsqlCommand(sql, conn);
    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

    // Read all results and store them
    var sessionDetails = new List<(string name, long count)>();
 while (await reader.ReadAsync(cancellationToken))
{
         var sessionName = reader.GetString(0);
     var count = reader.GetInt64(1);
         sessionDetails.Add((sessionName, count));
  sessions.Add(sessionName);
     }

    // Close the reader before displaying output
    await reader.CloseAsync();

    if (showOutput)
  {
if (sessionDetails.Count == 0)
      {
   Console.WriteLine("No existing sessions found.");
 }
    else
   {
     Console.WriteLine($"\nExisting sessions ({sessionDetails.Count}):");
    foreach (var (name, count) in sessionDetails)
            {
  Console.WriteLine($"  - {name} (Measurements: {count})");
            }
 }
     }
    }
    catch (Exception ex)
     {
  if (showOutput)
    {
     Console.WriteLine($"Warning: Could not list sessions from PostgreSQL: {ex.Message}");
       Console.WriteLine("No existing sessions found.");
  }
}
   }
 else if (settings.Database.Type.ToLower() == "geopackage")
    {
  // Query filesystem for GeoPackage files
     var folderPath = settings.Database.GeoPackage.FolderPath;
    
   if (!Directory.Exists(folderPath))
      return sessions;

  var geoPackageFiles = Directory.GetFiles(folderPath, "session_*.gpkg");

    if (showOutput)
  {
    if (geoPackageFiles.Length == 0)
 {
       Console.WriteLine("No existing sessions found.");
    }
     else
     {
   Console.WriteLine($"\nExisting sessions ({geoPackageFiles.Length}):");
  foreach (var file in geoPackageFiles)
       {
var fileName = Path.GetFileNameWithoutExtension(file);
 var sessionName = fileName.Replace("session_", "");
     sessions.Add(sessionName);
   
 var fileInfo = new FileInfo(file);
     Console.WriteLine($"  - {sessionName} (Size: {fileInfo.Length / 1024.0:F1} KB, Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm})");
          }
 }
}
   else
    {
    foreach (var file in geoPackageFiles)
   {
   var fileName = Path.GetFileNameWithoutExtension(file);
     var sessionName = fileName.Replace("session_", "");
  sessions.Add(sessionName);
   }
 }
        }

   return sessions;
    }

    private static bool IsValidSessionName(string name)
    {
        // Allow only alphanumeric and underscores
     return Regex.IsMatch(name, @"^[a-zA-Z0-9_]+$");
    }

    private static string SanitizeSessionName(string name)
    {
        // Remove invalid characters and convert to lowercase
        return Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9_]", "_");
    }

    private static async Task RunSimulationAsync(
        IRoverDataRepository repository, 
     SimulatorSettings settings, 
        ForestBoundaryReader forestReader,
    Polygon? forestPolygon,
        Guid roverId,
        string roverName,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
// GeoPackage-only: check file lock before creating/opening
        if (repository is GeoPackageRoverDataRepository geoRepo)
     {
         await HandleGeoPackageFileLocks(geoRepo);
        }

      // Initialize storage (non-destructive)
Console.WriteLine("\nInitializing database (non-destructive)...");
        try
        {
      await repository.InitializeAsync(cancellationToken);
     Console.WriteLine("Database initialization completed successfully!");
        }
      catch (Exception ex)
        {
   Console.WriteLine($"Database initialization failed: {ex.Message}");
     throw;
   }

        // Get forest polygon if not already loaded
        if (forestPolygon == null)
        {
 forestPolygon = await forestReader.GetBoundaryPolygonAsync(cancellationToken);
    }

        // Create fallback polygon if still null
   if (forestPolygon == null)
        {
            var fallbackBounds = settings.Forest.FallbackBounds;
   var coordinates = new[]
            {
                new Coordinate(fallbackBounds.MinLongitude, fallbackBounds.MinLatitude),
 new Coordinate(fallbackBounds.MaxLongitude, fallbackBounds.MinLatitude),
            new Coordinate(fallbackBounds.MaxLongitude, fallbackBounds.MaxLatitude),
     new Coordinate(fallbackBounds.MinLongitude, fallbackBounds.MaxLatitude),
       new Coordinate(fallbackBounds.MinLongitude, fallbackBounds.MinLatitude)
     };
            forestPolygon = new Polygon(new LinearRing(coordinates)) { SRID = 4326 };
   }

        // Initial position
        var rng = new Random();
 var envelope = await forestReader.GetBoundingBoxAsync(cancellationToken) 
    ?? forestPolygon.EnvelopeInternal;
        
 Point startPoint;
        if (settings.Rover.StartPosition.UseForestCentroid)
        {
    var centroid = await forestReader.GetCentroidAsync(cancellationToken);
 startPoint = centroid ?? forestPolygon.Centroid;
        }
     else
        {
 startPoint = new Point(
      settings.Rover.StartPosition.DefaultLongitude, 
       settings.Rover.StartPosition.DefaultLatitude) { SRID = 4326 };
        }

        var position = new RoverPosition(
      initialLat: startPoint.Y,
  initialLon: startPoint.X,
   initialBearing: rng.NextDouble() * 360.0,
            walkSpeed: settings.Rover.Movement.WalkSpeedMps,
            minLat: envelope.MinY,
            maxLat: envelope.MaxY,
            minLon: envelope.MinX,
      maxLon: envelope.MaxX
        );

        Console.WriteLine($"\nStarting rover '{roverName}' at: ({position.Latitude:F6}, {position.Longitude:F6})");
      Console.WriteLine($"Forest bounds: ({envelope.MinX:F6}, {envelope.MinY:F6}) to ({envelope.MaxX:F6}, {envelope.MaxY:F6})");
     Console.WriteLine($"Rover speed: {position.WalkSpeedMps} m/s");

  // Initialize environment attributes (wind)
        var windRange = settings.Simulation.Wind.InitialSpeedRange;
   var attributes = new RoverAttributes(
       initialWindDirection: rng.Next(0, 360),
        initialWindSpeed: windRange.Min + rng.NextDouble() * (windRange.Max - windRange.Min),
            maxWindSpeed: settings.Simulation.Wind.MaxSpeedMps
        );

        // Main loop
    var interval = TimeSpan.FromSeconds(settings.Simulation.IntervalSeconds);
        int sequenceNumber = 0;
    var sw = Stopwatch.StartNew();
        var nextTick = sw.Elapsed;

        Console.WriteLine("\nStarting rover simulation...");
        Console.WriteLine("Features: Smooth movement, weather patterns, forest boundary checking");
        Console.WriteLine($"Data storage: {(repository is PostgresRoverDataRepository ? "PostgreSQL database" : "GeoPackage file")}");
Console.WriteLine("Press Ctrl+C to stop simulation and save data...");

        while (!cancellationToken.IsCancellationRequested)
        {
 var now = DateTimeOffset.UtcNow;

            // Update rover state
      position.UpdateBearing(rng, meanChange: 0, stdDev: settings.Rover.Movement.BearingStdDev);
            position.UpdatePosition(interval);
 position.ConstrainToForestBoundary(forestPolygon);

       // Update environment
            attributes.UpdateWindMeasurements(rng);

            // Create and insert measurement with rover name
 var measurement = new RoverMeasurement(
      roverId,
  roverName,
         sessionId,
        sequenceNumber++,
        now,
    position.Latitude,
      position.Longitude,
    attributes.GetWindDirectionAsShort(),
     attributes.GetWindSpeedAsFloat(),
       position.ToGeometry()
    );

  try
            {
      await repository.InsertMeasurementAsync(measurement, cancellationToken);
      }
       catch (Exception ex)
            {
    Console.WriteLine($"\nError inserting measurement: {ex.Message}");
              throw;
            }

            // Progress (every 10 rows)
    ProgressMonitor.ReportProgress(
     sequenceNumber,
     sessionId,
                position.Latitude,
            position.Longitude,
     attributes.FormatWindInfo()
            );

       // Fixed-rate loop
         nextTick += interval;
      var delay = nextTick - sw.Elapsed;
     if (delay > TimeSpan.Zero)
          await Task.Delay(delay, cancellationToken);
    else
        nextTick = sw.Elapsed;
        }
    }

  // ...existing helper methods...
    private static Guid? GetRoverId(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
          if (string.Equals(args[i], "--rover", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
          if (Guid.TryParse(args[i + 1], out var id))
  return id;
    }
  }
        return null;
    }

    private static Guid? GetRoverIdFromEnv()
 {
        var env = Environment.GetEnvironmentVariable("ROVER_ID");
 return Guid.TryParse(env, out var id) ? id : null;
    }

    private static string GetSolutionDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var directory = new DirectoryInfo(currentDir);

        while (directory != null && !directory.GetFiles("*.sln").Any())
   {
         directory = directory.Parent;
 }

        return directory?.FullName ?? currentDir;
    }

    private static Task HandleGeoPackageFileLocks(GeoPackageRoverDataRepository geoRepo)
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("GEOPACKAGE FILE LOCK CHECK");
   Console.WriteLine(new string('=', 60));

      geoRepo.CheckFileLockStatus();

        if (geoRepo.IsFileLocked())
        {
        var lockingProcesses = geoRepo.GetLockingProcesses();

 Console.WriteLine("\nERROR: The GeoPackage file is currently LOCKED!");
  Console.WriteLine(new string('-', 60));

          if (lockingProcesses.Any(p => p.ToLower().Contains("roversimulator")))
    {
   Console.WriteLine("DETECTED: Another RoverSimulator instance appears to be running!");
         Console.WriteLine("This is the most likely cause of the file lock.");
     Console.WriteLine("\nTo resolve this:");
         Console.WriteLine("1. Check Task Manager for other RoverSimulator.exe processes");
    Console.WriteLine("2. Close any other RoverSimulator instances");
         Console.WriteLine("3. Wait a few seconds for the file to be released");
  Console.WriteLine("4. Try running this simulator again");
     }
  else
     {
 Console.WriteLine("DETECTED: A GIS application may have the file open:");
      if (lockingProcesses.Any())
         {
      Console.WriteLine("Potentially locking processes:");
       foreach (var process in lockingProcesses)
       {
     Console.WriteLine($" - {process}");
 }
     }
     Console.WriteLine("\nTo resolve this:");
  Console.WriteLine("1. Close QGIS, ArcGIS, or any other GIS applications");
    Console.WriteLine("2. Make sure the GeoPackage file is not open in any program");
      Console.WriteLine("3. Wait a few seconds for the file to be released");
        }

          Console.WriteLine(new string('-', 60));
       Console.WriteLine("Press any key to retry, or Ctrl+C to exit and fix the issue manually.");
    Console.ReadKey(true);

      Console.WriteLine("\nRechecking file lock status...");
            if (geoRepo.IsFileLocked())
  {
      Console.WriteLine("\nFile is STILL LOCKED. Cannot proceed with simulation.");
     Console.WriteLine("Please follow the instructions above to resolve the file lock.");
       Console.WriteLine("If the problem persists, restart your computer to clear any stuck file handles.");
          Environment.Exit(1);
        }
  else
      {
        Console.WriteLine("\nSUCCESS: File is now available! Continuing with simulation...");
 }
      }
 else
     {
          Console.WriteLine("File lock check: OK - File is available for use.");
        }

        Console.WriteLine(new string('=', 60));
        return Task.CompletedTask;
    }

    private static Task HandleSimulationError(Exception ex)
    {
        if (ex.Message.Contains("locked"))
        {
  Console.WriteLine("\nTROUBLESHOOTING TIPS:");
  Console.WriteLine("- Check for other RoverSimulator instances in Task Manager");
        Console.WriteLine("- Close QGIS, ArcGIS, or other GIS applications");
  Console.WriteLine("- Wait a few seconds and try again");
  Console.WriteLine("- If problem persists, restart your computer");
 }
   else if (ex.Message.Contains("timeout") || ex.Message.Contains("connection"))
        {
      Console.WriteLine("\nDATABASE CONNECTION TROUBLESHOOTING:");
      Console.WriteLine("- Check network connectivity to the database server");
Console.WriteLine("- Verify the database server is running and accessible");
      Console.WriteLine("- Check firewall settings on both client and server");
  Console.WriteLine("- Change Database.Type to 'geopackage' in appsettings.json to use local storage");
        }
    return Task.CompletedTask;
    }
}