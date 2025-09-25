using System.Diagnostics;
using RoverSimulator;

// ===== DATABASE CONFIGURATION =====
// Choose database type: "postgres" or "geopackage"
const string DATABASE_TYPE = SimulatorConfiguration.DEFAULT_DATABASE_TYPE; 

// Check if we should just verify the existing GeoPackage
if (args.Length > 0 && args[0] == "--verify")
{
    await SimulatorConfiguration.VerifyGeoPackageAsync("rover_data.gpkg");
    return;
}

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"Rover simulator starting with {DATABASE_TYPE.ToUpper()} database.");
Console.WriteLine("Press Ctrl+C to stop. Press Ctrl+P to toggle progress output.");
Console.WriteLine("Run with '--verify' argument to check existing GeoPackage contents.");

// Load forest boundary from GeoPackage
Console.WriteLine("\nLoading forest boundary from RiverHeadForest.gpkg...");
try
{
    await SimulatorConfiguration.GetForestBoundaryAsync();
    Console.WriteLine("Forest boundary loaded successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Could not load forest boundary: {ex.Message}");
    Console.WriteLine("Using fallback coordinates...");
}

// Set up progress monitoring
using var progressMonitor = new ProgressMonitor(cts);

// Create appropriate repository with connection validation (no automatic fallback)
IRoverDataRepository repository;
try
{
    repository = await SimulatorConfiguration.CreateRepositoryWithValidationAsync(DATABASE_TYPE, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Database setup cancelled by user.");
    return;
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"\nDatabase connection failed: {ex.Message}");
    Console.WriteLine("\nSimulation cannot proceed without a valid database connection.");
    Console.WriteLine("Please resolve the database connection issue and try again.");
    return;
}
catch (ArgumentException ex)
{
    Console.WriteLine($"Configuration error: {ex.Message}");
    return;
}
catch (Exception ex)
{
    Console.WriteLine($"Unexpected error creating database repository: {ex.Message}");
    Console.WriteLine("Cannot proceed without a database connection.");
    return;
}

try
{
    // For GeoPackage, check file lock status before attempting to delete
    if (repository is GeoPackageRoverDataRepository geoRepo)
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
            
            // Check again after user intervention
            Console.WriteLine("\nRechecking file lock status...");
            if (geoRepo.IsFileLocked())
            {
                Console.WriteLine("\nFile is STILL LOCKED. Cannot proceed with simulation.");
                Console.WriteLine("Please follow the instructions above to resolve the file lock.");
                Console.WriteLine("If the problem persists, restart your computer to clear any stuck file handles.");
                return;
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
    }

    // Reset and initialize database
    Console.WriteLine("\nInitializing database...");
    try
    {
        await repository.ResetDatabaseAsync(cts.Token);
        await repository.InitializeAsync(cts.Token);
        Console.WriteLine("Database initialization completed successfully!");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Database initialization cancelled by user.");
        return;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database initialization failed: {ex.Message}");
        if (ex.InnerException != null)
        {
            Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
        }
        
        // Provide specific troubleshooting based on the exception
        if (ex.Message.Contains("timeout") || ex.Message.Contains("connection"))
        {
            Console.WriteLine("\nDATABASE CONNECTION TROUBLESHOOTING:");
            Console.WriteLine("- Network connectivity issues to the database server");
            Console.WriteLine("- Database server may be overloaded or not responding");
            Console.WriteLine("- Firewall blocking the connection");
            Console.WriteLine("- Database server not running");
        }
        
        throw; // Re-throw to be caught by outer try-catch
    }

    // Initialize rover position with smoother movement parameters
    var rng = new Random();
    var position = new RoverPosition(
        initialLat: SimulatorConfiguration.DEFAULT_START_LATITUDE,
        initialLon: SimulatorConfiguration.DEFAULT_START_LONGITUDE,
        initialBearing: rng.NextDouble() * 360.0,
        walkSpeed: 7,  // 7 m/s for debugging
        minLat: SimulatorConfiguration.MIN_LATITUDE,
        maxLat: SimulatorConfiguration.MAX_LATITUDE,
        minLon: SimulatorConfiguration.MIN_LONGITUDE,
        maxLon: SimulatorConfiguration.MAX_LONGITUDE
    );

    Console.WriteLine($"\nStarting rover at: ({position.Latitude:F6}, {position.Longitude:F6})");
    Console.WriteLine($"Forest bounds: ({SimulatorConfiguration.MIN_LONGITUDE:F6}, {SimulatorConfiguration.MIN_LATITUDE:F6}) to ({SimulatorConfiguration.MAX_LONGITUDE:F6}, {SimulatorConfiguration.MAX_LATITUDE:F6})");
    Console.WriteLine($"DEBUG: Rover speed set to {position.WalkSpeedMps} m/s (debug mode)");
    Console.WriteLine("POLYGON BOUNDARY: Using NetTopologySuite Contains for accurate forest boundary checking");
    Console.WriteLine("SMOOTH MOVEMENT: Enhanced wind and movement smoothing enabled");

    // Verify initial position is inside forest polygon
    var initialInForest = await position.IsInForestBoundaryAsync();
    Console.WriteLine($"Initial position inside forest polygon: {initialInForest}");

    // Initialize rover attributes with smoother transitions
    var attributes = new RoverAttributes(
        initialWindDirection: rng.Next(0, 360),
        initialWindSpeed: 1.0 + rng.NextDouble() * 5.0, // 1-6 m/s typical inside forest
        maxWindSpeed: 15.0
    );

    // Simulation parameters
    TimeSpan interval = TimeSpan.FromSeconds(1); // send a point every 1 second
    var sessionId = Guid.NewGuid();
    int sequenceNumber = 0;

    // Pace the loop accurately
    var sw = Stopwatch.StartNew();
    var nextTick = sw.Elapsed;

    Console.WriteLine("\nStarting rover simulation with BALANCED TRANSITIONS...");
    Console.WriteLine("Features:");
    Console.WriteLine("- Moderate wind direction changes (gradual transitions over 30s)");
    Console.WriteLine("- Weather patterns lasting 3-10 minutes");
    Console.WriteLine("- Balanced movement (40% smoother than original, but not too stable)");
    Console.WriteLine("- Natural environmental modeling with appropriate variation");
    Console.WriteLine($"Data storage: {(repository is PostgresRoverDataRepository ? "PostgreSQL database" : "GeoPackage file (rover_data.gpkg)")}");
    Console.WriteLine("Press Ctrl+C to stop simulation and save data...");

    while (!cts.IsCancellationRequested)
    {
        var now = DateTimeOffset.UtcNow;

        // Update rover position with balanced bearing changes
        position.UpdateBearing(rng, meanChange: 0, stdDev: 2.0); // Moderate value between 1.5 and 3.0
        position.UpdatePosition(interval);

        // Use ACTUAL POLYGON boundary checking with NetTopologySuite Contains
        // This replaces the simple bounding box method
        position.ConstrainToForestBoundary(); // Using synchronous version for performance

        // Update environmental measurements with balanced transitions
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
            await repository.InsertMeasurementAsync(measurement, cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError inserting measurement: {ex.Message}");
            throw; // Re-throw to stop simulation on database errors
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
            await Task.Delay(delay, cts.Token);
        else
            nextTick = sw.Elapsed; // if we're late, reset schedule
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nSimulation cancelled by user. Saving data and cleaning up...");
}
catch (Exception ex)
{
    Console.WriteLine($"\nError during simulation: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
    }
    
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
        Console.WriteLine("- Change DEFAULT_DATABASE_TYPE to \"geopackage\" in SimulatorConfiguration.cs to use local storage");
    }
}
finally
{
    Console.WriteLine("\nCleaning up and releasing file locks...");
    repository.Dispose();
    
    // Give extra time for file handles to be released
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
