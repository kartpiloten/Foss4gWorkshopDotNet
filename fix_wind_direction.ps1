# Script to regenerate wind polygons with corrected direction
Write-Host "Regenerating Wind Polygons with Corrected Direction" -ForegroundColor Green
Write-Host "===================================================" -ForegroundColor Green

Write-Host "`n1. Checking rover data availability..."
$roverDataPath = "C:\temp\Rover1\rover_data.gpkg"
if (Test-Path $roverDataPath) {
    Write-Host "? Found rover data: $roverDataPath" -ForegroundColor Green
    $fileInfo = Get-Item $roverDataPath
    Write-Host "   Modified: $($fileInfo.LastWriteTime)"
} else {
    Write-Host "? Rover data not found: $roverDataPath" -ForegroundColor Red
    Write-Host "   Please run the RoverSimulator first to generate rover data." -ForegroundColor Yellow
    exit 1
}

Write-Host "`n2. Removing old wind polygon file to force regeneration..."
$windPolygonPath = "C:\temp\Rover1\rover_windpolygon.gpkg"
if (Test-Path $windPolygonPath) {
    try {
        Remove-Item $windPolygonPath -Force
        Write-Host "? Removed old wind polygon file" -ForegroundColor Green
    } catch {
        Write-Host "??  Could not remove old file (may be locked by QGIS): $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "   The converter will try an alternative filename" -ForegroundColor Yellow
    }
}

Write-Host "`n3. Running ConvertWinddataToPolygon with corrected direction logic..."
Set-Location "C:\source\Foss4gWorkshopDotNet\ConvertWinddataToPolygon"
& dotnet run

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n? Wind polygons regenerated successfully!" -ForegroundColor Green
    Write-Host "`n4. What was fixed:" -ForegroundColor Cyan
    Write-Host "   • Wind arrows point DOWNWIND (where wind is going)" -ForegroundColor White
    Write-Host "   • Scent polygons now extend DOWNWIND (same direction as arrows)" -ForegroundColor White
    Write-Host "   • Both now use consistent wind direction interpretation" -ForegroundColor White
    
    Write-Host "`n5. Next steps:" -ForegroundColor Cyan
    Write-Host "   • Restart your Blazor application" -ForegroundColor White
    Write-Host "   • The wind feathers should now align with the arrows" -ForegroundColor White
    Write-Host "   • The combined coverage area will also be updated" -ForegroundColor White
} else {
    Write-Host "`n? Wind polygon generation failed!" -ForegroundColor Red
    Write-Host "   Check the output above for error details" -ForegroundColor Yellow
}

Write-Host "`nPress any key to continue..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")