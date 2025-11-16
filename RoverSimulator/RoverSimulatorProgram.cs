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
using RoverSimulator.Configuration;
using RoverSimulator.Services;
using RoverData.Repository;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Npgsql; // PostgreSQL ADO.NET provider for session table queries
using System.Linq;

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

    // Use ForestBoundaryReader from RoverData.Repository
    services.AddSingleton<ForestBoundaryReader>(provider =>
    {
      var settings = provider.GetRequiredService<IOptions<SimulatorSettings>>().Value;
      var geoPackagePath = Path.Combine(GetSolutionDirectory(), settings.Forest.BoundaryFile);
      return new ForestBoundaryReader(geoPackagePath, settings.Forest.LayerName);
    });

    var serviceProvider = services.BuildServiceProvider();

    // Resolve settings and services from DI
    var settings = serviceProvider.GetRequiredService<IOptions<SimulatorSettings>>().Value;
    var forestReader = serviceProvider.GetRequiredService<ForestBoundaryReader>();

    // Utility: verify existing GeoPackage and exit
    if (args.Length > 0 && args.Contains("--verify", StringComparer.OrdinalIgnoreCase))
    {
      Console.WriteLine("Verification mode - listing available sessions...");
      await ListAvailableSessionsAsync(settings, showOutput: true, default);
      return;
    }

    // Cancellation: Ctrl+C to stop
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) =>
    {
      e.Cancel = true;
      cts.Cancel();
    };

    // Initialize database schema early (if using Postgres) to support session queries
    if (settings.Database.Type.ToLower() == "postgres")
    {
      try
      {
        Console.WriteLine("\nInitializing database schema (if needed)...");
        using var dataSource = new NpgsqlDataSourceBuilder(settings.Database.Postgres.ConnectionString)
          .UseNetTopologySuite()
          .Build();
        var initializer = new PostgresDatabaseInitializer(dataSource);
        await initializer.InitializeSchemaAsync(cts.Token);
        Console.WriteLine("Database schema ready.");
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Warning: Could not initialize database schema: {ex.Message}");
      }
    }

    // === SESSION SELECTION ===
    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("ROVER SIMULATOR - SESSION SETUP");
    Console.WriteLine(new string('=', 60));

    string sessionName = await GetSessionNameAsync(settings, args, cts.Token);

    Console.WriteLine($"\nSession: {sessionName}");

    // === ROVER SETUP ===
    Guid roverId = GetRoverId(args) ?? GetRoverIdFromEnv() ?? Guid.NewGuid();
    string roverName = await GetRoverNameAsync(args);

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

    // Create repository based on configuration using DI
    Console.WriteLine($"\n{new string('=', 60)}");
    Console.WriteLine("DATABASE CONNECTION SETUP");
    Console.WriteLine($"{new string('=', 60)}");
    Console.WriteLine($"Database type: {settings.Database.Type.ToUpper()}");
    Console.WriteLine($"Session: {sessionName}");

    RoverData.Repository.IRoverDataRepository repository;
    try
    {
      if (settings.Database.Type.ToLower() == "postgres")
      {
        // Create NpgsqlDataSource with NetTopologySuite support
        var dataSource = new NpgsqlDataSourceBuilder(settings.Database.Postgres.ConnectionString)
          .UseNetTopologySuite()
          .Build();
        
        // Register or get session from database
        var sessionRepository = new SessionRepository(dataSource);
        var sessionId = await sessionRepository.RegisterOrGetSessionAsync(sessionName, cts.Token);
        
        var sessionContext = new ConsoleSessionContext(sessionId, sessionName);
        repository = new PostgresRoverDataRepository(dataSource, sessionContext);
        Console.WriteLine("Using PostgreSQL database");
      }
      else if (settings.Database.Type.ToLower() == "geopackage")
      {
        var repoOptions = Options.Create(new GeoPackageRepositoryOptions
        {
          FolderPath = settings.Database.GeoPackage.FolderPath
        });
        var sessionContext = new ConsoleSessionContext(Guid.NewGuid(), sessionName);
        repository = new GeoPackageRoverDataRepository(repoOptions, sessionContext);
        Console.WriteLine($"Using GeoPackage database (folder: {settings.Database.GeoPackage.FolderPath})");
      }
      else
      {
        throw new InvalidOperationException($"Unsupported database type: {settings.Database.Type}");
      }
      Console.WriteLine($"{new string('=', 60)}");
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

    // Initialize repository BEFORE starting simulation to obtain SessionId
    try
    {
      Console.WriteLine("\nInitializing database (non-destructive)...");
      await repository.InitializeAsync(cts.Token);
      Console.WriteLine("Database initialization completed successfully!");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Database initialization failed: {ex.Message}");
      repository.Dispose();
      return;
    }

    // Run main loop
    try
    {
      // Use the repository's SessionId provided by InitializeAsync
      await RunSimulationAsync(
        repository,
        settings,
        forestReader,
        forestPolygon,
        roverId,
        roverName,
        repository.SessionId,
        cts.Token);
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

  private static string GenerateAutoSessionName()
  {
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 6);
    return $"auto_{timestamp}_{randomSuffix}";
  }

  private static async Task<string> GetSessionNameAsync(SimulatorSettings settings, string[] args, CancellationToken cancellationToken)
  {
    // Check for --session command-line argument first
    var sessionFromArgs = GetSessionNameFromArgs(args);
    if (!string.IsNullOrEmpty(sessionFromArgs))
    {
      // Check for special "auto" keyword
      if (sessionFromArgs.Equals("auto", StringComparison.OrdinalIgnoreCase))
      {
        var autoSessionName = GenerateAutoSessionName();
        Console.WriteLine($"Auto-generated session name: {autoSessionName}");
        return autoSessionName;
      }

      Console.WriteLine($"Using session from command line: {sessionFromArgs}");
      return sessionFromArgs;
    }

    // Check for AUTO_SESSION environment variable
    var autoSession = Environment.GetEnvironmentVariable("AUTO_SESSION");
    if (!string.IsNullOrEmpty(autoSession) && autoSession.Equals("true", StringComparison.OrdinalIgnoreCase))
    {
      var autoSessionName = GenerateAutoSessionName();
      Console.WriteLine($"Auto-generated session name: {autoSessionName}");
      return autoSessionName;
    }

    // List existing sessions (database-type aware)
    var existingSessions = await ListAvailableSessionsAsync(settings, showOutput: true, cancellationToken);

    Console.WriteLine("\nWould you like to join an existing session or create a new one?");
    Console.Write("Type 'new' for a new session, 'auto' for auto-generated name, or enter an existing session name: ");

    string? input = Console.ReadLine()?.Trim();

    if (string.IsNullOrEmpty(input))
    {
      Console.WriteLine("No input provided. Creating new session...");
      input = "new";
    }

    if (input.Equals("auto", StringComparison.OrdinalIgnoreCase))
    {
      var autoSessionName = GenerateAutoSessionName();
      Console.WriteLine($"\nAuto-generated session name: {autoSessionName}");
      return autoSessionName;
    }
    else if (input.Equals("new", StringComparison.OrdinalIgnoreCase))
    {
      Console.Write("\nName the new session (alphanumeric, no spaces), or 'auto' for automatic: ");
      string? newSessionName = Console.ReadLine()?.Trim();

      if (string.IsNullOrWhiteSpace(newSessionName) || newSessionName.Equals("auto", StringComparison.OrdinalIgnoreCase))
      {
        newSessionName = GenerateAutoSessionName();
        Console.WriteLine($"Auto-generated session name: {newSessionName}");
        return newSessionName;
      }

      while (!IsValidSessionName(newSessionName))
      {
        Console.WriteLine("Invalid session name. Use only letters, numbers, and underscores.");
        Console.Write("Name the new session, or 'auto' for automatic: ");
        newSessionName = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(newSessionName) || newSessionName.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
          newSessionName = GenerateAutoSessionName();
          Console.WriteLine($"Auto-generated session name: {newSessionName}");
          return newSessionName;
        }
      }

      return SanitizeSessionName(newSessionName!);
    }
    else
    {
      // Validate that the session exists (case-insensitive)
      if (existingSessions.Any(s => s.Equals(input, StringComparison.OrdinalIgnoreCase)))
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

  private static Task<string> GetRoverNameAsync(string[] args)
  {
    // Check for --rover-name command-line argument first
    var roverNameFromArgs = GetRoverNameFromArgs(args);
    if (!string.IsNullOrEmpty(roverNameFromArgs))
    {
      Console.WriteLine($"Using rover name from command line: {roverNameFromArgs}");
      return Task.FromResult(roverNameFromArgs);
    }

    // Interactive mode
    Console.Write("\nWhat is the rover dog's name? ");
    string? name = Console.ReadLine()?.Trim();

    while (string.IsNullOrWhiteSpace(name))
    {
      Console.WriteLine("Please provide a name for the rover dog.");
      Console.Write("Rover dog's name: ");
      name = Console.ReadLine()?.Trim();
    }

    return Task.FromResult(name!);
  }

  private static async Task<List<string>> ListAvailableSessionsAsync(SimulatorSettings settings, bool showOutput = false, CancellationToken cancellationToken = default)
  {
    var sessions = new List<string>();

    try
    {
      if (settings.Database.Type.ToLower() == "postgres")
      {
        // Create NpgsqlDataSource for temporary query
        using var dataSource = new NpgsqlDataSourceBuilder(settings.Database.Postgres.ConnectionString)
          .UseNetTopologySuite()
          .Build();
        
        var sessionRepository = new SessionRepository(dataSource);
        
        // Get sessions with counts from SessionRepository
        var sessionInfos = await sessionRepository.GetSessionsWithCountsAsync(cancellationToken);
        
        sessions.AddRange(sessionInfos.Select(s => s.SessionName));

        if (showOutput)
        {
          if (sessionInfos.Count == 0)
          {
            Console.WriteLine("No existing sessions found.");
          }
          else
          {
            Console.WriteLine($"\nExisting sessions ({sessionInfos.Count}):");
            foreach (var sessionInfo in sessionInfos)
            {
              Console.WriteLine($"  - {sessionInfo.SessionName} (Measurements: {sessionInfo.MeasurementCount})");
            }
          }
        }
      }
      else
      {
        throw new NotSupportedException("Session querying for GeoPackage not yet implemented");
      }
    }
    catch (Exception ex)
    {
      if (showOutput)
      {
        Console.WriteLine($"Warning: Could not list sessions: {ex.Message}");
        Console.WriteLine("No existing sessions found.");
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
    RoverData.Repository.IRoverDataRepository repository,
    SimulatorSettings settings,
    ForestBoundaryReader forestReader,
    Polygon? forestPolygon,
    Guid roverId,
    string roverName,
    Guid sessionId,
    CancellationToken cancellationToken)
  {
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
    if (settings.Rover.StartPosition.UseRandomStartPosition)
    {
      startPoint = GetRandomPointInPolygon(forestPolygon, rng);
    }
    else
    {
      startPoint = new Point(
        settings.Rover.StartPosition.DefaultLongitude,
        settings.Rover.StartPosition.DefaultLatitude)
      { SRID = 4326 };
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
      var measurement = new RoverData.Repository.RoverMeasurement(
        roverId,
        roverName,
        sessionId,
        sequenceNumber++,
        now,
        attributes.GetWindDirectionAsShort(),
        attributes.GetWindSpeedAsFloat(),
        position.ToGeometry()
      );

      try
      {
        await repository.InsertAsync(measurement, cancellationToken);
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

  private static Point GetRandomPointInPolygon(Polygon polygon, Random rng, int maxAttempts = 10000)
  {
    if (polygon == null) throw new ArgumentNullException(nameof(polygon));

    var env = polygon.EnvelopeInternal;
    double minX = env.MinX, maxX = env.MaxX;
    double minY = env.MinY, maxY = env.MaxY;

    for (int i = 0; i < maxAttempts; i++)
    {
      double x = minX + rng.NextDouble() * (maxX - minX);
      double y = minY + rng.NextDouble() * (maxY - minY);
      var pt = new Point(x, y) { SRID = polygon.SRID };

      if (polygon.Contains(pt))
        return pt;
    }

    // Fallback to centroid if sampling fails (rare unless polygon is extremely thin)
    return polygon.Centroid ?? new Point((minX + maxX) / 2.0, (minY + maxY) / 2.0) { SRID = polygon.SRID };
  }

  private static string? GetSessionNameFromArgs(string[] args)
  {
    for (int i = 0; i < args.Length; i++)
    {
      if (string.Equals(args[i], "--session", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
      {
        return args[i + 1];
      }
    }
    return null;
  }

  private static string? GetRoverNameFromArgs(string[] args)
  {
    for (int i = 0; i < args.Length; i++)
    {
      if (string.Equals(args[i], "--rover-name", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
      {
        return args[i + 1];
      }
    }
    return null;
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