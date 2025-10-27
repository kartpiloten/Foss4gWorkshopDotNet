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

namespace RoverSimulator;

class RoverSimulatorProgram
{
    public static async Task Main(string[] args)
    {
        // Configuration: load appsettings.json (kept minimal)
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Dependency Injection: register typed options and services
        var services = new ServiceCollection();
        services.Configure<SimulatorSettings>(configuration);
        services.AddSingleton<DatabaseService>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<SimulatorSettings>>().Value;
            return new DatabaseService(settings.Database);
        });
        
        // Replace ForestBoundaryService with ForestBoundaryReader from ReadRoverDBStubLibrary
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
        if (args.Length > 0 && args[0] == "--verify")
        {
            var geoPackagePath = Path.Combine(settings.Database.GeoPackage.FolderPath, "rover_data.gpkg");
            await VerifyGeoPackageAsync(geoPackagePath);
            return;
        }

        // Cancellation: Ctrl+C to stop
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine($"Rover simulator starting with {settings.Database.Type.ToUpper()} database.");
        Console.WriteLine("Press Ctrl+C to stop. Press Ctrl+P to toggle progress output.");
        Console.WriteLine("Run with '--verify' argument to check existing GeoPackage contents.");

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

        // Create repository
        IRoverDataRepository repository;
        try
        {
            repository = await databaseService.CreateRepositoryAsync(cts.Token);
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
            await RunSimulationAsync(repository, settings, forestReader, forestPolygon, cts.Token);
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
            Console.WriteLine("The GeoPackage file should now be available for other applications.");
        }
        else
        {
            Console.WriteLine("Database connection closed.");
        }
    }

    private static async Task RunSimulationAsync(
        IRoverDataRepository repository, 
        SimulatorSettings settings, 
        ForestBoundaryReader forestReader,
        Polygon? forestPolygon,
        CancellationToken cancellationToken)
    {
        // GeoPackage-only: check file lock before creating/opening
        if (repository is GeoPackageRoverDataRepository geoRepo)
        {
            await HandleGeoPackageFileLocks(geoRepo);
        }

        // Initialize storage
        Console.WriteLine("\nInitializing database...");
        try
        {
            await repository.ResetDatabaseAsync(cancellationToken);
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

        Console.WriteLine($"\nStarting rover at: ({position.Latitude:F6}, {position.Longitude:F6})");
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
        var sessionId = Guid.NewGuid();
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

            // Create and insert measurement
            var measurement = new RoverMeasurement(
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

    // GeoPackage file lock helper for Windows users with GIS apps open
    private static Task HandleGeoPackageFileLocks(GeoPackageRoverDataRepository geoRepo)
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("GEOPACKAGE FILE LOCK CHECK");
        Console.WriteLine(new string('=', 60));

        geoRepo.CheckFileLockStatus();

        if (geoRepo.IsFileLocked())
        {
            var lockingProcesses = geoRepo.GetLockingProcesses();

            Console.WriteLine("\nERROR: The GeoPackage file 'rover_data.gpkg' is currently LOCKED!");
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
                        Console.WriteLine($"  - {process}");
                    }
                }
                Console.WriteLine("\nTo resolve this:");
                Console.WriteLine("1. Close QGIS, ArcGIS, or any other GIS applications");
                Console.WriteLine("2. Make sure the rover_data.gpkg file is not open in any program");
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

    // Keep troubleshooting minimal; point users to likely fixes
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

    // Utility: verify contents of an existing GeoPackage (OGC standard)
    private static async Task<bool> VerifyGeoPackageAsync(string filePath)
    {
        Console.WriteLine("Verifying existing GeoPackage contents...");

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"No GeoPackage file found at: {filePath}");
            Console.WriteLine("Run the simulator first to create data.");
            return false;
        }

        try
        {
            using var geoPackage = await MapPiloteGeopackageHelper.GeoPackage.OpenAsync(filePath, 4326);
            var layer = await geoPackage.EnsureLayerAsync("rover_measurements", new Dictionary<string, string>(), 4326);

            var totalCount = await layer.CountAsync();
            Console.WriteLine($"Total measurements: {totalCount}");

            var fileInfo = new FileInfo(filePath);
            Console.WriteLine($"File size: {fileInfo.Length / 1024.0:F1} KB");
            Console.WriteLine($"File path: {fileInfo.FullName}");
            Console.WriteLine($"Last modified: {fileInfo.LastWriteTime}");

            if (totalCount > 0)
            {
                Console.WriteLine("\nSample data (first 3 records):");
                var readOptions = new MapPiloteGeopackageHelper.ReadOptions(IncludeGeometry: true, OrderBy: "sequence ASC", Limit: 3);

                int count = 0;
                await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
                {
                    var sequence = feature.Attributes["sequence"];
                    var latitude = feature.Attributes["latitude"];
                    var longitude = feature.Attributes["longitude"];
                    var windSpeed = feature.Attributes["wind_speed_mps"];
                    var windDir = feature.Attributes["wind_direction_deg"];

                    Console.WriteLine($"  {++count}. Seq: {sequence}, Location: ({latitude?[..8]}, {longitude?[..8]}), Wind: {windSpeed}m/s @ {windDir}deg");
                }

                Console.WriteLine($"\nGeoPackage verification successful! The file contains {totalCount} rover measurements.");
                Console.WriteLine("You can open this file in QGIS, ArcGIS, or other GIS software to visualize the rover track.");
                return true;
            }
            else
            {
                Console.WriteLine("GeoPackage exists but contains no data.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error verifying GeoPackage: {ex.Message}");
            return false;
        }
    }
}