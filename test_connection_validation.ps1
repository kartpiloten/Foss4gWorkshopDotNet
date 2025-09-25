#!/usr/bin/env pwsh

# Quick test to demonstrate the new database connection validation
# This will just test the connection without running the full simulation

Write-Host "Database Connection Validation Test" -ForegroundColor Yellow
Write-Host "====================================" -ForegroundColor Yellow
Write-Host ""

# Navigate to RoverSimulator directory
Push-Location RoverSimulator

try {
    Write-Host "Testing database connections..." -ForegroundColor Cyan
    Write-Host ""
    
    # Create a simple test program
    $testCode = @'
using RoverSimulator;

Console.WriteLine("Testing PostgreSQL connection with timeout handling...");
Console.WriteLine();

var cts = new CancellationTokenSource();

try 
{
    var repo = await SimulatorConfiguration.CreateRepositoryWithFallbackAsync("postgres", cts.Token);
    
    Console.WriteLine();
    Console.WriteLine($"Repository type: {repo.GetType().Name}");
    
    repo.Dispose();
    
    Console.WriteLine();
    Console.WriteLine("Connection test completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"Connection test failed: {ex.Message}");
}
'@
    
    # Write test code to a temporary file
    $testFile = "TempConnectionTest.cs"
    $testCode | Out-File -FilePath $testFile -Encoding UTF8
    
    Write-Host "Running connection validation..." -ForegroundColor White
    Write-Host ""
    
    # Run the test
    dotnet run --project . $testFile 2>&1
    
    # Clean up
    if (Test-Path $testFile) {
        Remove-Item $testFile
    }
    
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Connection validation test completed." -ForegroundColor Green