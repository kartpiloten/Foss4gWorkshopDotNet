using ReadRoverDBStub;

// ===== DATABASE CONFIGURATION =====
// Choose the same database type as RoverSimulator for compatibility
const string DATABASE_TYPE = ReaderConfiguration.DEFAULT_DATABASE_TYPE;

Console.WriteLine("========================================");
Console.WriteLine("       ROVER DATA READER STUB");
Console.WriteLine("========================================");
Console.WriteLine($"Database type preference: {DATABASE_TYPE.ToUpper()}");
Console.WriteLine("Press Ctrl+C to stop monitoring...");
Console.WriteLine();

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\nShutdown requested...");
};

// Create appropriate data reader with connection validation
IRoverDataReader dataReader;
try
{
    dataReader = await ReaderConfiguration.CreateReaderWithValidationAsync(DATABASE_TYPE, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Database setup cancelled by user.");
    return;
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"\nDatabase connection failed: {ex.Message}");
    Console.WriteLine("\nReader cannot proceed without a valid database connection.");
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
    Console.WriteLine($"Unexpected error creating database reader: {ex.Message}");
    Console.WriteLine("Cannot proceed without a database connection.");
    return;
}

try
{
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
    Console.WriteLine($"Data source: {(dataReader is PostgresRoverDataReader ? "PostgreSQL database" : "GeoPackage file")}");
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
    Console.WriteLine("4. For PostgreSQL: Verify the database schema has been created");
}
catch (Exception ex)
{
    Console.WriteLine($"Error during monitoring: {ex.Message}");
    
    if (ex.Message.Contains("connection") || ex.Message.Contains("server") || ex.Message.Contains("timeout"))
    {
        Console.WriteLine();
        Console.WriteLine("DATABASE CONNECTION TROUBLESHOOTING:");
        Console.WriteLine("For PostgreSQL:");
        Console.WriteLine("1. Verify PostgreSQL server is running and accessible");
        Console.WriteLine("2. Check connection string credentials");
        Console.WriteLine("3. Ensure the RoverSimulator has created the database schema");
        Console.WriteLine("4. Verify the 'roverdata.rover_measurements' table exists");
        Console.WriteLine();
        Console.WriteLine("For GeoPackage:");
        Console.WriteLine("1. Verify the GeoPackage file exists");
        Console.WriteLine("2. Check file permissions");
        Console.WriteLine("3. Ensure the file is not locked by another application");
        Console.WriteLine();
        Console.WriteLine("Quick fix: Change DEFAULT_DATABASE_TYPE to \"geopackage\" in ReaderConfiguration.cs");
    }
}
finally
{
    Console.WriteLine("\nCleaning up database connections...");
    dataReader.Dispose();
}

Console.WriteLine("\nRover data reader stopped.");
