#!/usr/bin/env pwsh

# Test script for ReadRoverDBStub database support
# This demonstrates the new PostgreSQL and GeoPackage support in the reader

Write-Host "========================================" -ForegroundColor Yellow
Write-Host "   READROVERDBSTUB DATABASE SUPPORT TEST" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""

Write-Host "ReadRoverDBStub now supports both PostgreSQL and GeoPackage databases!" -ForegroundColor Green
Write-Host "This follows the same pattern as RoverSimulator for consistent configuration." -ForegroundColor White
Write-Host ""

Write-Host "Features implemented:" -ForegroundColor Cyan
Write-Host "? PostgreSQL support with connection validation" -ForegroundColor Green
Write-Host "? Connection timeout handling (10s timeout, 3 retries)" -ForegroundColor Green
Write-Host "? Automatic error detection and troubleshooting guidance" -ForegroundColor Green
Write-Host "? No automatic fallback (user controls database choice)" -ForegroundColor Green
Write-Host "? Enhanced error messages for common issues" -ForegroundColor Green
Write-Host "? Support for both PostgreSQL and GeoPackage readers" -ForegroundColor Green
Write-Host ""

Write-Host "Database Configuration:" -ForegroundColor Yellow
Write-Host "- Default database type: POSTGRES (matches RoverSimulator)" -ForegroundColor White
Write-Host "- PostgreSQL connection: 192.168.1.97:5432" -ForegroundColor White
Write-Host "- GeoPackage path: C:\temp\Rover1\" -ForegroundColor White
Write-Host ""

Write-Host "To change database type:" -ForegroundColor Cyan
Write-Host "Edit ReadRoverDBStub/ReaderConfiguration.cs and change:" -ForegroundColor White
Write-Host "  DEFAULT_DATABASE_TYPE = `"postgres`"  ?  `"geopackage`"" -ForegroundColor Yellow
Write-Host ""

Write-Host "Testing connection validation (this will likely show PostgreSQL timeout)..." -ForegroundColor Cyan
Write-Host ""

# Navigate to ReadRoverDBStub directory
Push-Location ReadRoverDBStub

try {
    Write-Host "Running ReadRoverDBStub with new database support..." -ForegroundColor White
    Write-Host "Expected: PostgreSQL connection will timeout, showing clear error message" -ForegroundColor Gray
    Write-Host ""
    
    # Run for 30 seconds to see the connection attempt
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run" -PassThru -NoNewWindow
    
    # Wait up to 30 seconds
    $completed = $process.WaitForExit(30000)
    
    if (!$completed -and !$process.HasExited) {
        Write-Host "Stopping test after 30 seconds..." -ForegroundColor Yellow
        $process.Kill()
        $process.WaitForExit()
    }
    
    Write-Host ""
    Write-Host "Test completed!" -ForegroundColor Green
    
} catch {
    Write-Host "Error running test: $_" -ForegroundColor Red
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Database Support Summary:" -ForegroundColor Yellow
Write-Host "=========================" -ForegroundColor Yellow
Write-Host ""
Write-Host "ReadRoverDBStub now has the same database capabilities as RoverSimulator:" -ForegroundColor White
Write-Host ""
Write-Host "?? PostgresRoverDataReader" -ForegroundColor Cyan
Write-Host "   - Connects to PostgreSQL database" -ForegroundColor Gray
Write-Host "   - Reads from roverdata.rover_measurements table" -ForegroundColor Gray
Write-Host "   - Connection validation with timeout/retry" -ForegroundColor Gray
Write-Host "   - Enhanced error handling" -ForegroundColor Gray
Write-Host ""
Write-Host "?? GeoPackageRoverDataReader" -ForegroundColor Cyan
Write-Host "   - Reads from local .gpkg files" -ForegroundColor Gray
Write-Host "   - Compatible with RoverSimulator output" -ForegroundColor Gray
Write-Host "   - Works offline" -ForegroundColor Gray
Write-Host ""
Write-Host "?? ReaderConfiguration" -ForegroundColor Cyan
Write-Host "   - Matches RoverSimulator configuration pattern" -ForegroundColor Gray
Write-Host "   - Connection validation with fallback handling" -ForegroundColor Gray
Write-Host "   - Clear error messages and troubleshooting" -ForegroundColor Gray
Write-Host ""
Write-Host "Both RoverSimulator and ReadRoverDBStub now support the same databases!" -ForegroundColor Green
Write-Host "Configure them to use the same database type for seamless data flow." -ForegroundColor White