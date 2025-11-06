using ReadRoverDBStubLibrary;
using Microsoft.Extensions.Configuration;

// This project uses top-level statements (C# feature) to keep the sample minimal.

/// <summary>
/// Simple test to verify that GetLatestMeasurementAsync returns fresh data from GeoPackage
/// Run this while RoverSimulator is writing data to see if new measurements appear
/// </summary>

Console.WriteLine("========================================");
Console.WriteLine("  TESTING GetLatestMeasurementAsync");
Console.WriteLine("========================================");
Console.WriteLine();
Console.WriteLine("This test reads the latest measurement every 2 seconds");
Console.WriteLine("Make sure RoverSimulator is running to generate new data!");
Console.WriteLine();

// Load configuration from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

// Read configuration from appsettings.json
var dbSection = configuration.GetSection("DatabaseConfiguration");
var config = new DatabaseConfiguration
{
    DatabaseType = dbSection.GetValue<string>("DatabaseType") ?? "geopackage",
    GeoPackageFolderPath = dbSection.GetValue<string>("GeoPackageFolderPath") ?? @"C:\temp\Rover1\",
    PostgresConnectionString = dbSection.GetValue<string>("ConnectionString"),
    ConnectionTimeoutSeconds = dbSection.GetValue<int?>("ConnectionTimeoutSeconds") ?? 10,
    MaxRetryAttempts = dbSection.GetValue<int?>("MaxRetryAttempts") ?? 3,
    RetryDelayMs = dbSection.GetValue<int?>("RetryDelayMs") ?? 2000
};

Console.WriteLine($"Database Type: {config.DatabaseType}");
Console.WriteLine($"Configuration loaded from appsettings.json");
Console.WriteLine();

// GeoPackage is an OGC FOSS4G standard for storing spatial data in a SQLite file.

using var reader = RoverDataReaderFactory.CreateReader(config);

Console.WriteLine("Initializing reader...");
// Async initialization (async/await avoids blocking the main thread)
await reader.InitializeAsync();

Console.WriteLine("Reader initialized successfully!");
Console.WriteLine();

// First, let's check what's actually in the database
Console.WriteLine("=== CHECKING DATABASE CONTENTS ===");
var totalCount = await reader.GetMeasurementCountAsync();
Console.WriteLine($"Total measurements in database: {totalCount}");

if (totalCount > 0)
{
    // LINQ is used here to project and take subsets of the sequence list
    var allMeasurements = await reader.GetAllMeasurementsAsync();
    Console.WriteLine($"First 5 sequences: {string.Join(", ", allMeasurements.Take(5).Select(m => m.Sequence))}");
    Console.WriteLine($"Last 5 sequences: {string.Join(", ", allMeasurements.TakeLast(5).Select(m => m.Sequence))}");
    Console.WriteLine($"Highest sequence number: {allMeasurements.Max(m => m.Sequence)}");
}

Console.WriteLine();

// Read display interval from config
var displayIntervalMs = configuration.GetValue<int?>("Tester:DisplayUpdateIntervalMs") ?? 2000;
Console.WriteLine($"Starting continuous monitoring (update every {displayIntervalMs}ms)...");
Console.WriteLine("Press Ctrl+C to stop");
Console.WriteLine();
Console.WriteLine($"{"Time",-12} {"Sequence",-10} {"Latitude",-12} {"Longitude",-12} {"Wind Speed",-12} {"Wind Dir",-10}");
Console.WriteLine(new string('-', 80));

int previousSequence = -1;

// Poll at configured interval
while (true)
{
    try
    {
        // Get the most recent measurement asynchronously (async/await)
        var latest = await reader.GetLatestMeasurementAsync();

        if (latest != null)
        {
            // Check if sequence number increased
            string newDataMarker = latest.Sequence > previousSequence ? " *** NEW ***" : "";
            previousSequence = latest.Sequence;

            Console.WriteLine(
                $"{DateTime.Now:HH:mm:ss.fff,-12} " +
                $"{latest.Sequence,-10} " +
                $"{latest.Latitude,-12:F6} " +
                $"{latest.Longitude,-12:F6} " +
                $"{latest.WindSpeedMps,-12:F2} m/s " +
                $"{latest.WindDirectionDeg,-10}°" +
                newDataMarker
            );
        }
        else
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff,-12} NO DATA");
        }

        await Task.Delay(displayIntervalMs); // Asynchronously wait between polls
    }
    catch (Exception ex)
    {
        // Keep error handling minimal to avoid noise while still surfacing issues
        Console.WriteLine($"ERROR: {ex.Message}");
        await Task.Delay(displayIntervalMs);
    }
}
