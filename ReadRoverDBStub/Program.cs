using ReadRoverDBStub;

// ===== DATABASE CONFIGURATION =====
// Choose the same database type as RoverSimulator for compatibility
const string DATABASE_TYPE = ReaderConfiguration.DEFAULT_DATABASE_TYPE;

Console.WriteLine("========================================");
Console.WriteLine("       ROVER DATA READER STUB");
Console.WriteLine("========================================");
Console.WriteLine($"Database type: {DATABASE_TYPE.ToUpper()}");
Console.WriteLine("Press Ctrl+C to stop monitoring...");
Console.WriteLine();

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\nShutdown requested...");
};

try
{
    // Create the appropriate data reader
    using var dataReader = ReaderConfiguration.CreateReader(DATABASE_TYPE);
    
    // Create the monitor with default intervals (poll every 1s, display every 2s)
    using var monitor = new RoverDataMonitor(
        dataReader, 
        pollIntervalMs: ReaderConfiguration.DEFAULT_POLL_INTERVAL_MS,
        displayIntervalMs: ReaderConfiguration.DEFAULT_DISPLAY_INTERVAL_MS);
    
    // Start monitoring
    await monitor.StartAsync();
    
    Console.WriteLine();
    Console.WriteLine("Monitoring rover database for new measurements...");
    Console.WriteLine("The collection will be updated automatically as new data arrives.");
    Console.WriteLine();
    
    // Keep the application running until cancelled
    while (!cts.IsCancellationRequested)
    {
        await Task.Delay(100, cts.Token);
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Monitoring cancelled by user.");
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"Database file not found: {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("TROUBLESHOOTING:");
    Console.WriteLine("1. Make sure the RoverSimulator has created the database/GeoPackage file");
    Console.WriteLine("2. Check that the file path is correct in ReaderConfiguration");
    Console.WriteLine("3. Ensure the RoverSimulator is running or has run at least once");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    
    if (ex.Message.Contains("connection") || ex.Message.Contains("server"))
    {
        Console.WriteLine();
        Console.WriteLine("DATABASE CONNECTION TROUBLESHOOTING:");
        Console.WriteLine("For PostgreSQL:");
        Console.WriteLine("1. Verify PostgreSQL server is running");
        Console.WriteLine("2. Check connection string credentials");
        Console.WriteLine("3. Ensure the database 'AucklandRoverData' exists");
        Console.WriteLine();
        Console.WriteLine("For GeoPackage:");
        Console.WriteLine("1. Verify the GeoPackage file exists");
        Console.WriteLine("2. Check file permissions");
        Console.WriteLine("3. Ensure the file is not locked by another application");
    }
}

Console.WriteLine("\nRover data reader stopped.");
