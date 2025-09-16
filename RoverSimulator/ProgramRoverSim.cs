using System.Diagnostics;
using RoverSimulator;

// ===== DATABASE CONFIGURATION =====
// Choose database type: "postgres" or "geopackage"
const string DATABASE_TYPE = SimulatorConfiguration.DEFAULT_DATABASE_TYPE; // Change this to "geopackage" to use GeoPackage

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

Console.WriteLine($"Rover simulator starting with {DATABASE_TYPE.ToUpper()} database. Press Ctrl+C to stop. Press Ctrl+P to toggle progress output.");
Console.WriteLine("Run with '--verify' argument to check existing GeoPackage contents.");

// Load forest boundary from GeoPackage
Console.WriteLine("Loading forest boundary from RiverHeadForest.gpkg...");
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

// Create appropriate repository based on configuration
var repository = SimulatorConfiguration.CreateRepository(DATABASE_TYPE);

try
{
    // Reset and initialize database
    await repository.ResetDatabaseAsync(cts.Token);
    await repository.InitializeAsync(cts.Token);

    // Initialize rover position
    var rng = new Random();
    var position = new RoverPosition(
        initialLat: SimulatorConfiguration.DEFAULT_START_LATITUDE,
        initialLon: SimulatorConfiguration.DEFAULT_START_LONGITUDE,
        initialBearing: rng.NextDouble() * 360.0,
        walkSpeed: 500,  // Very fast (500 m/s for debugging)
        minLat: SimulatorConfiguration.MIN_LATITUDE,
        maxLat: SimulatorConfiguration.MAX_LATITUDE,
        minLon: SimulatorConfiguration.MIN_LONGITUDE,
        maxLon: SimulatorConfiguration.MAX_LONGITUDE
    );

    Console.WriteLine($"Starting rover at: ({position.Latitude:F6}, {position.Longitude:F6})");
    Console.WriteLine($"Forest bounds: ({SimulatorConfiguration.MIN_LONGITUDE:F6}, {SimulatorConfiguration.MIN_LATITUDE:F6}) to ({SimulatorConfiguration.MAX_LONGITUDE:F6}, {SimulatorConfiguration.MAX_LATITUDE:F6})");
    Console.WriteLine($"DEBUG: Rover speed set to {position.WalkSpeedMps} m/s (fast mode)");
    Console.WriteLine("POLYGON BOUNDARY: Using NetTopologySuite Contains for accurate forest boundary checking");

    // Verify initial position is inside forest polygon
    var initialInForest = await position.IsInForestBoundaryAsync();
    Console.WriteLine($"Initial position inside forest polygon: {initialInForest}");

    // Initialize rover attributes (environmental measurements)
    var attributes = new RoverAttributes(
        initialWindDirection: rng.Next(0, 360),
        initialWindSpeed: 1.0 + rng.NextDouble() * 5.0, // 1-6 m/s typical inside forest
        maxWindSpeed: 15.0
    );

    // Simulation parameters
    // Debug: intervalls 0.001  
    TimeSpan interval = TimeSpan.FromSeconds(0.001); // send a point every 0.001 seconds
    var sessionId = Guid.NewGuid();
    int sequenceNumber = 0;

    // Pace the loop accurately
    var sw = Stopwatch.StartNew();
    var nextTick = sw.Elapsed;

    Console.WriteLine("Starting rover simulation with POLYGON boundary checking...");

    while (!cts.IsCancellationRequested)
    {
        var now = DateTimeOffset.UtcNow;

        // Update rover position with slight randomness
        position.UpdateBearing(rng, meanChange: 0, stdDev: 3);
        position.UpdatePosition(interval);

        // Use ACTUAL POLYGON boundary checking with NetTopologySuite Contains
        // This replaces the simple bounding box method
        position.ConstrainToForestBoundary(); // Using synchronous version for performance

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

        // Insert measurement
        await repository.InsertMeasurementAsync(measurement, cts.Token);

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
    Console.WriteLine("Simulation cancelled by user.");
}
catch (Exception ex)
{
    Console.WriteLine($"Error during simulation: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
    }
}
finally
{
    repository.Dispose();
}

Console.WriteLine("Rover simulator stopped.");
