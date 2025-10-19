using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using RoverSimulator.Configuration;
using RoverSimulator.Services;
using NetTopologySuite.Geometries;
using MapPiloteGeopackageHelper;

namespace RoverSimulator;

class Program
{
    public static async Task Main(string[] args)
    {
        // ===== CONFIGURATION SETUP =====
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // ===== DEPENDENCY INJECTION SETUP =====
        var services = new ServiceCollection();
        services.Configure<SimulatorSettings>(configuration);
        services.AddSingleton<DatabaseService>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<SimulatorSettings>>().Value;
            return new DatabaseService(settings.Database);
        });
        services.AddSingleton<ForestBoundaryService>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<SimulatorSettings>>().Value;
            return new ForestBoundaryService(settings.Forest);
        });

        var serviceProvider = services.BuildServiceProvider();

        // ===== GET SERVICES =====
        var settings = serviceProvider.GetRequiredService<IOptions<SimulatorSettings>>().Value;
        var databaseService = serviceProvider.GetRequiredService<DatabaseService>();
        var forestService = serviceProvider.GetRequiredService<ForestBoundaryService>();

        // ===== VERIFY COMMAND =====
        if (args.Length > 0 && args[0] == "--verify")
        {
            var geoPackagePath = Path.Combine(settings.Database.GeoPackage.FolderPath, "rover_data.gpkg");
            await VerifyGeoPackageAsync(geoPackagePath);
            return;
        }

        // ===== CANCELLATION SETUP =====
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine($"🚁 Rover simulator starting with {settings.Database.Type.ToUpper()} database.");
        Console.WriteLine("Press Ctrl+C to stop. Press Ctrl+P to toggle progress output.");
        Console.WriteLine("Run with '--verify' argument to check existing GeoPackage contents.");

        // ===== LOAD FOREST BOUNDARY =====
        Console.WriteLine("\n🌲 Loading forest boundary...");
        try
        {
            await forestService.GetForestBoundaryAsync();
            Console.WriteLine("✅ Forest boundary loaded successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Warning: Could not load forest boundary: {ex.Message}");
            Console.WriteLine("Using fallback coordinates...");
        }

        // ===== SETUP PROGRESS MONITORING =====
        using var progressMonitor = new ProgressMonitor(cts);

        // ===== CREATE DATABASE REPOSITORY =====
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
            Console.WriteLine($"\n❌ Database connection failed: {ex.Message}");
            Console.WriteLine("\nSimulation cannot proceed without a valid database connection.");
            Console.WriteLine("Please check your appsettings.json configuration and try again.");
            return;
        }

        // ===== RUN SIMULATION =====
        try
        {
            await RunSimulationAsync(repository, settings, forestService, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n⏹️  Simulation cancelled by user. Saving data and cleaning up...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error during simulation: {ex.Message}");
            await HandleSimulationError(ex);
        }
        finally
        {
            Console.WriteLine("\n🧹 Cleaning up and releasing file locks...");
            repository.Dispose();
            await Task.Delay(500); // Give time for file handles to be released
        }

        Console.WriteLine("\n🛑 Rover simulator stopped.");
        if (repository is GeoPackageRoverDataRepository)
        {
            Console.WriteLine("📁 The GeoPackage file should now be available for other applications.");
        }
        else
        {
            Console.WriteLine("🔌 Database connection closed.");
        }
    }

    private static async Task RunSimulationAsync(
        IRoverDataRepository repository, 
        SimulatorSettings settings, 
        ForestBoundaryService forestService, 
        CancellationToken cancellationToken)
    {
        // ===== HANDLE GEOPACKAGE FILE LOCKS =====
        if (repository is GeoPackageRoverDataRepository geoRepo)
        {
            await HandleGeoPackageFileLocks(geoRepo);
        }

        // ===== INITIALIZE DATABASE =====
        Console.WriteLine("\n💾 Initializing database...");
        try
        {
            await repository.ResetDatabaseAsync(cancellationToken);
            await repository.InitializeAsync(cancellationToken);
            Console.WriteLine("✅ Database initialization completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Database initialization failed: {ex.Message}");
            throw;
        }

        // ===== INITIALIZE ROVER POSITION =====
        var rng = new Random();
        var bounds = await forestService.GetBoundingBoxAsync();
        
        // Get start position based on configuration
        Point startPoint;
        if (settings.Rover.StartPosition.UseForestCentroid)
        {
            startPoint = await forestService.GetForestCentroidAsync();
        }
        else
        {
            startPoint = new Point(settings.Rover.StartPosition.DefaultLongitude, 
                                 settings.Rover.StartPosition.DefaultLatitude) { SRID = 4326 };
        }

        var position = new RoverPosition(
            initialLat: startPoint.Y,
            initialLon: startPoint.X,
            initialBearing: rng.NextDouble() * 360.0,
            walkSpeed: settings.Rover.Movement.WalkSpeedMps,
            minLat: bounds.MinY,
            maxLat: bounds.MaxY,
            minLon: bounds.MinX,
            maxLon: bounds.MaxX
        );

        Console.WriteLine($"\n🗺️  Starting rover at: ({position.Latitude:F6}, {position.Longitude:F6})");
        Console.WriteLine($"📍 Forest bounds: ({bounds.MinX:F6}, {bounds.MinY:F6}) to ({bounds.MaxX:F6}, {bounds.MaxY:F6})");
        Console.WriteLine($"🏃 Rover speed: {position.WalkSpeedMps} m/s");

        // ===== INITIALIZE ROVER ATTRIBUTES =====
        var windRange = settings.Simulation.Wind.InitialSpeedRange;
        var attributes = new RoverAttributes(
            initialWindDirection: rng.Next(0, 360),
            initialWindSpeed: windRange.Min + rng.NextDouble() * (windRange.Max - windRange.Min),
            maxWindSpeed: settings.Simulation.Wind.MaxSpeedMps
        );

        // ===== SIMULATION LOOP =====
        var interval = TimeSpan.FromSeconds(settings.Simulation.IntervalSeconds);
        var sessionId = Guid.NewGuid();
        int sequenceNumber = 0;
        var sw = Stopwatch.StartNew();
        var nextTick = sw.Elapsed;

        Console.WriteLine("\n🎯 Starting rover simulation...");
        Console.WriteLine("✨ Features: Smooth movement, weather patterns, forest boundary checking");
        Console.WriteLine($"💾 Data storage: {(repository is PostgresRoverDataRepository ? "PostgreSQL database" : "GeoPackage file")}");
        Console.WriteLine("Press Ctrl+C to stop simulation and save data...");

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            // Update rover position with configured parameters
            position.UpdateBearing(rng, meanChange: 0, stdDev: settings.Rover.Movement.BearingStdDev);
            position.UpdatePosition(interval);
            position.ConstrainToForestBoundary();

            // Update environmental measurements
            attributes.UpdateWindMeasurements(rng);

            // Create measurement record
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

            // Insert measurement with error handling
            try
            {
                await repository.InsertMeasurementAsync(measurement, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error inserting measurement: {ex.Message}");
                throw;
            }

            // Report progress
            ProgressMonitor.ReportProgress(
                sequenceNumber,
                sessionId,
                position.Latitude,
                position.Longitude,
                attributes.FormatWindInfo()
            );

            // Wait until next interval tick
            nextTick += interval;
            var delay = nextTick - sw.Elapsed;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            else
                nextTick = sw.Elapsed; // if we're late, reset schedule
        }
    }

    private static Task HandleGeoPackageFileLocks(GeoPackageRoverDataRepository geoRepo)
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("📁 GEOPACKAGE FILE LOCK CHECK");
        Console.WriteLine(new string('=', 60));

        geoRepo.CheckFileLockStatus();

        if (geoRepo.IsFileLocked())
        {
            var lockingProcesses = geoRepo.GetLockingProcesses();

            Console.WriteLine("\n❌ ERROR: The GeoPackage file 'rover_data.gpkg' is currently LOCKED!");
            Console.WriteLine(new string('-', 60));

            if (lockingProcesses.Any(p => p.ToLower().Contains("roversimulator")))
            {
                Console.WriteLine("🔍 DETECTED: Another RoverSimulator instance appears to be running!");
                Console.WriteLine("This is the most likely cause of the file lock.");
                Console.WriteLine("\n💡 To resolve this:");
                Console.WriteLine("1. Check Task Manager for other RoverSimulator.exe processes");
                Console.WriteLine("2. Close any other RoverSimulator instances");
                Console.WriteLine("3. Wait a few seconds for the file to be released");
                Console.WriteLine("4. Try running this simulator again");
            }
            else
            {
                Console.WriteLine("🔍 DETECTED: A GIS application may have the file open:");
                if (lockingProcesses.Any())
                {
                    Console.WriteLine("Potentially locking processes:");
                    foreach (var process in lockingProcesses)
                    {
                        Console.WriteLine($"  - {process}");
                    }
                }
                Console.WriteLine("\n💡 To resolve this:");
                Console.WriteLine("1. Close QGIS, ArcGIS, or any other GIS applications");
                Console.WriteLine("2. Make sure the rover_data.gpkg file is not open in any program");
                Console.WriteLine("3. Wait a few seconds for the file to be released");
            }

            Console.WriteLine(new string('-', 60));
            Console.WriteLine("Press any key to retry, or Ctrl+C to exit and fix the issue manually.");
            Console.ReadKey(true);

            // Check again after user intervention
            Console.WriteLine("\n🔄 Rechecking file lock status...");
            if (geoRepo.IsFileLocked())
            {
                Console.WriteLine("\n❌ File is STILL LOCKED. Cannot proceed with simulation.");
                Console.WriteLine("Please follow the instructions above to resolve the file lock.");
                Console.WriteLine("If the problem persists, restart your computer to clear any stuck file handles.");
                Environment.Exit(1);
            }
            else
            {
                Console.WriteLine("\n✅ SUCCESS: File is now available! Continuing with simulation...");
            }
        }
        else
        {
            Console.WriteLine("✅ File lock check: OK - File is available for use.");
        }

        Console.WriteLine(new string('=', 60));
        return Task.CompletedTask;
    }

    private static Task HandleSimulationError(Exception ex)
    {
        if (ex.Message.Contains("locked"))
        {
            Console.WriteLine("\n🔧 TROUBLESHOOTING TIPS:");
            Console.WriteLine("- Check for other RoverSimulator instances in Task Manager");
            Console.WriteLine("- Close QGIS, ArcGIS, or other GIS applications");
            Console.WriteLine("- Wait a few seconds and try again");
            Console.WriteLine("- If problem persists, restart your computer");
        }
        else if (ex.Message.Contains("timeout") || ex.Message.Contains("connection"))
        {
            Console.WriteLine("\n🔧 DATABASE CONNECTION TROUBLESHOOTING:");
            Console.WriteLine("- Check network connectivity to the database server");
            Console.WriteLine("- Verify the database server is running and accessible");
            Console.WriteLine("- Check firewall settings on both client and server");
            Console.WriteLine("- Change Database.Type to 'geopackage' in appsettings.json to use local storage");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Verifies existing GeoPackage contents (moved from SimulatorConfiguration)
    /// </summary>
    private static async Task<bool> VerifyGeoPackageAsync(string filePath)
    {
        Console.WriteLine("🔍 Verifying existing GeoPackage contents...");

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"❌ No GeoPackage file found at: {filePath}");
            Console.WriteLine("Run the simulator first to create data.");
            return false;
        }

        try
        {
            using var geoPackage = await MapPiloteGeopackageHelper.GeoPackage.OpenAsync(filePath, 4326);
            var layer = await geoPackage.EnsureLayerAsync("rover_measurements", new Dictionary<string, string>(), 4326);

            var totalCount = await layer.CountAsync();
            Console.WriteLine($"📊 Total measurements: {totalCount}");

            var fileInfo = new FileInfo(filePath);
            Console.WriteLine($"📁 File size: {fileInfo.Length / 1024.0:F1} KB");
            Console.WriteLine($"📍 File path: {fileInfo.FullName}");
            Console.WriteLine($"⏰ Last modified: {fileInfo.LastWriteTime}");

            if (totalCount > 0)
            {
                Console.WriteLine("\n📋 Sample data (first 3 records):");
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

                Console.WriteLine($"\n✅ GeoPackage verification successful! The file contains {totalCount} rover measurements.");
                Console.WriteLine("🗺️  You can open this file in QGIS, ArcGIS, or other GIS software to visualize the rover track.");
                return true;
            }
            else
            {
                Console.WriteLine("⚠️  GeoPackage exists but contains no data.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error verifying GeoPackage: {ex.Message}");
            return false;
        }
    }
}