#!/usr/bin/env pwsh

# Test script for the new ReadRoverDBStub library architecture
# This demonstrates the split between the library and test client

Write-Host "========================================" -ForegroundColor Yellow
Write-Host "   READROVERDBSTUB LIBRARY ARCHITECTURE TEST" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""

Write-Host "ReadRoverDBStub has been split into two projects:" -ForegroundColor Green
Write-Host ""

Write-Host "?? ReadRoverDBStubLibrary (Silent Library)" -ForegroundColor Cyan
Write-Host "   Purpose: Reusable rover data reading functionality" -ForegroundColor Gray
Write-Host "   Features:" -ForegroundColor White
Write-Host "   ? Silent operation (no console output)" -ForegroundColor Green
Write-Host "   ? PostgreSQL and GeoPackage support" -ForegroundColor Green
Write-Host "   ? Connection validation with timeout/retry" -ForegroundColor Green
Write-Host "   ? Event-driven monitoring with RoverDataMonitor" -ForegroundColor Green
Write-Host "   ? Used by ConvertWinddataToPolygon and FrontendVersion2" -ForegroundColor Green
Write-Host ""

Write-Host "?? ReadRoverDBStubTester (Test Client)" -ForegroundColor Cyan
Write-Host "   Purpose: Console application that uses the library" -ForegroundColor Gray
Write-Host "   Features:" -ForegroundColor White
Write-Host "   ? Console output for testing and debugging" -ForegroundColor Green
Write-Host "   ? Connection diagnostics and troubleshooting" -ForegroundColor Green
Write-Host "   ? Real-time data monitoring display" -ForegroundColor Green
Write-Host "   ? Event handling for library notifications" -ForegroundColor Green
Write-Host ""

Write-Host "?? Updated Dependencies:" -ForegroundColor Yellow
Write-Host "   ConvertWinddataToPolygon ? ReadRoverDBStubLibrary" -ForegroundColor White
Write-Host "   FrontendVersion2 ? ReadRoverDBStubLibrary" -ForegroundColor White
Write-Host "   ReadRoverDBStubTester ? ReadRoverDBStubLibrary" -ForegroundColor White
Write-Host ""

Write-Host "Testing the new architecture..." -ForegroundColor Cyan
Write-Host ""

# Build the library first
Write-Host "Building ReadRoverDBStubLibrary..." -ForegroundColor White
try {
    $libraryResult = dotnet build ReadRoverDBStubLibrary/ReadRoverDBStubLibrary.csproj
    if ($LASTEXITCODE -eq 0) {
        Write-Host "? Library build successful" -ForegroundColor Green
    } else {
        Write-Host "? Library build failed" -ForegroundColor Red
        Write-Host $libraryResult
        exit 1
    }
} catch {
    Write-Host "? Error building library: $_" -ForegroundColor Red
    exit 1
}

# Build the test client
Write-Host "Building ReadRoverDBStubTester..." -ForegroundColor White
try {
    $testerResult = dotnet build ReadRoverDBStubTester/ReadRoverDBStubTester.csproj
    if ($LASTEXITCODE -eq 0) {
        Write-Host "? Test client build successful" -ForegroundColor Green
    } else {
        Write-Host "? Test client build failed" -ForegroundColor Red
        Write-Host $testerResult
        exit 1
    }
} catch {
    Write-Host "? Error building test client: $_" -ForegroundColor Red
    exit 1
}

# Build dependent projects
Write-Host "Building ConvertWinddataToPolygon..." -ForegroundColor White
try {
    $convertResult = dotnet build ConvertWinddataToPolygon/ConvertWinddataToPolygon.csproj
    if ($LASTEXITCODE -eq 0) {
        Write-Host "? ConvertWinddataToPolygon build successful" -ForegroundColor Green
    } else {
        Write-Host "? ConvertWinddataToPolygon build failed" -ForegroundColor Red
        Write-Host $convertResult
        exit 1
    }
} catch {
    Write-Host "? Error building ConvertWinddataToPolygon: $_" -ForegroundColor Red
    exit 1
}

Write-Host "Building FrontendVersion2..." -ForegroundColor White
try {
    $frontendResult = dotnet build FrontendVersion2/FrontendVersion2.csproj
    if ($LASTEXITCODE -eq 0) {
        Write-Host "? FrontendVersion2 build successful" -ForegroundColor Green
    } else {
        Write-Host "? FrontendVersion2 build failed" -ForegroundColor Red
        Write-Host $frontendResult
        exit 1
    }
} catch {
    Write-Host "? Error building FrontendVersion2: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "?? All projects built successfully!" -ForegroundColor Green
Write-Host ""

Write-Host "Quick test of the ReadRoverDBStubTester (will likely show PostgreSQL timeout)..." -ForegroundColor Cyan
Write-Host "Expected: Connection validation, then timeout with clear error message" -ForegroundColor Gray
Write-Host ""

# Navigate to ReadRoverDBStubTester directory
Push-Location ReadRoverDBStubTester

try {
    Write-Host "Running ReadRoverDBStubTester for 15 seconds..." -ForegroundColor White
    
    # Run for 15 seconds to see the connection attempt
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run" -PassThru -NoNewWindow
    
    # Wait up to 15 seconds
    $completed = $process.WaitForExit(15000)
    
    if (!$completed -and !$process.HasExited) {
        Write-Host "Stopping test after 15 seconds..." -ForegroundColor Yellow
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
Write-Host "Architecture Split Summary:" -ForegroundColor Yellow
Write-Host "==========================" -ForegroundColor Yellow
Write-Host ""
Write-Host "? ReadRoverDBStubLibrary: Silent, reusable rover data access" -ForegroundColor Green
Write-Host "   - Used by ConvertWinddataToPolygon for polygon generation" -ForegroundColor Gray
Write-Host "   - Used by FrontendVersion2 for web API data access" -ForegroundColor Gray
Write-Host "   - No console output, event-driven notifications" -ForegroundColor Gray
Write-Host ""
Write-Host "? ReadRoverDBStubTester: Verbose test client for debugging" -ForegroundColor Green  
Write-Host "   - Console output for troubleshooting" -ForegroundColor Gray
Write-Host "   - Connection diagnostics" -ForegroundColor Gray
Write-Host "   - Real-time monitoring display" -ForegroundColor Gray
Write-Host ""
Write-Host "? All dependent projects updated and building successfully" -ForegroundColor Green
Write-Host ""
Write-Host "Benefits:" -ForegroundColor Cyan
Write-Host "• Library can be used silently by other applications" -ForegroundColor White
Write-Host "• Test client provides debugging capabilities" -ForegroundColor White
Write-Host "• Clean separation of concerns" -ForegroundColor White
Write-Host "• Reusable across multiple projects" -ForegroundColor White