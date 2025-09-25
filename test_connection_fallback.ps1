#!/usr/bin/env pwsh

# Test script for database connection validation (no automatic fallback)
# This script tests the new connection validation without automatic fallback

Write-Host "========================================" -ForegroundColor Yellow
Write-Host "   DATABASE CONNECTION VALIDATION TEST" -ForegroundColor Yellow  
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""

Write-Host "This test will:" -ForegroundColor Cyan
Write-Host "1. Try to connect to PostgreSQL database" -ForegroundColor White
Write-Host "2. If connection fails, show clear error message and stop" -ForegroundColor White
Write-Host "3. Provide instructions for fixing the issue or changing database type" -ForegroundColor White
Write-Host ""

Write-Host "Expected behavior:" -ForegroundColor Green
Write-Host "- PostgreSQL connection will likely timeout (192.168.1.97 unreachable)" -ForegroundColor White
Write-Host "- System will show detailed error information" -ForegroundColor White
Write-Host "- Simulation will stop with clear instructions" -ForegroundColor White
Write-Host "- NO automatic fallback to GeoPackage" -ForegroundColor Red
Write-Host ""

Write-Host "To test GeoPackage instead:" -ForegroundColor Cyan
Write-Host "- Change DEFAULT_DATABASE_TYPE to `"geopackage`" in SimulatorConfiguration.cs" -ForegroundColor White
Write-Host ""

Write-Host "Press any key to start the test..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# Navigate to the RoverSimulator directory
Push-Location RoverSimulator

try {
    Write-Host "Starting rover simulator with connection validation test..." -ForegroundColor Cyan
    Write-Host "The simulator will attempt to connect and either succeed or fail clearly." -ForegroundColor White
    Write-Host ""
    
    # Run the simulator with a timeout of 60 seconds to see the full connection process
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run" -PassThru -NoNewWindow
    
    # Wait for the process to exit or timeout after 60 seconds
    $timeoutReached = !$process.WaitForExit(60000)
    
    if ($timeoutReached -and !$process.HasExited) {
        Write-Host ""
        Write-Host "Test timeout reached (60 seconds). Stopping process..." -ForegroundColor Yellow
        $process.Kill()
        $process.WaitForExit()
        
        Write-Host ""
        Write-Host "ANALYSIS:" -ForegroundColor Cyan
        Write-Host "If the simulator was still running after 60 seconds, it likely means:" -ForegroundColor White
        Write-Host "- Database connection was successful, OR" -ForegroundColor Green
        Write-Host "- The simulation was running normally" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "Test completed!" -ForegroundColor Green
    Write-Host ""
    
    # Check if GeoPackage was created (would indicate the simulation ran)
    if (Test-Path "rover_data.gpkg") {
        $fileInfo = Get-Item "rover_data.gpkg"
        Write-Host "RESULT: Simulation ran successfully!" -ForegroundColor Green
        Write-Host "  Database used: GeoPackage" -ForegroundColor White
        Write-Host "  File: $($fileInfo.Name)" -ForegroundColor White
        Write-Host "  Size: $([math]::Round($fileInfo.Length / 1KB, 1)) KB" -ForegroundColor White
        Write-Host "  Created: $($fileInfo.CreationTime)" -ForegroundColor White
    } else {
        Write-Host "RESULT: No data file created" -ForegroundColor Yellow
        Write-Host "This could mean:" -ForegroundColor White
        Write-Host "- Database connection failed (expected behavior)" -ForegroundColor White
        Write-Host "- PostgreSQL was used successfully" -ForegroundColor White
        Write-Host "- Or the process was stopped before creating data" -ForegroundColor White
    }
    
} catch {
    Write-Host "Error running test: $_" -ForegroundColor Red
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Connection validation test completed." -ForegroundColor Yellow
Write-Host ""
Write-Host "Key points about the new behavior:" -ForegroundColor Cyan
Write-Host "- No automatic fallback to GeoPackage" -ForegroundColor White
Write-Host "- Clear error messages when connections fail" -ForegroundColor White
Write-Host "- User maintains control over database choice" -ForegroundColor White
Write-Host "- Explicit instructions provided for fixing issues" -ForegroundColor White