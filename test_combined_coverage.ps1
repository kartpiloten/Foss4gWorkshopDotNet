# Test script to verify combined coverage data is available
Write-Host "Testing Combined Coverage Data Availability" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green

# Check if the GeoPackage file exists
$geopackagePath = "C:\temp\Rover1\rover_windpolygon.gpkg"
Write-Host "`n1. Checking GeoPackage file..."
if (Test-Path $geopackagePath) {
    Write-Host "? Found: $geopackagePath" -ForegroundColor Green
    $fileInfo = Get-Item $geopackagePath
    Write-Host "   Size: $([math]::Round($fileInfo.Length / 1KB, 1)) KB"
    Write-Host "   Modified: $($fileInfo.LastWriteTime)"
} else {
    Write-Host "? Not found: $geopackagePath" -ForegroundColor Red
    Write-Host "   Please run the ConvertWinddataToPolygon tool first" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n2. Starting Blazor application..."
Write-Host "   You can now:"
Write-Host "   • Open your browser to: http://localhost:5000 or https://localhost:5001"
Write-Host "   • Test the API directly: http://localhost:5000/api/data-status"
Write-Host "   • View combined coverage: http://localhost:5000/api/combined-coverage"
Write-Host ""
Write-Host "3. Expected behavior:"
Write-Host "   • The map should show a tan/beige dashed polygon in the background"
Write-Host "   • This represents the unified scent detection coverage area"
Write-Host "   • Click on it to see details (total area, number of polygons, etc.)"
Write-Host ""
Write-Host "Press Ctrl+C to stop the application when testing is complete."
Write-Host ""

# Start the application
Set-Location "C:\source\Foss4gWorkshopDotNet\FrontendVersion2"
dotnet run