# Script to test balanced rover simulation
Write-Host "Testing Balanced Rover Simulation" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

Write-Host "`nBalanced Improvement Summary:" -ForegroundColor Cyan
Write-Host "• Wind direction changes: Moderate transitions over 30 seconds" -ForegroundColor White
Write-Host "• Weather patterns: 3-10 minutes (shorter for more variation)" -ForegroundColor White
Write-Host "• Movement smoothing: 40% reduction in direction changes (balanced)" -ForegroundColor White
Write-Host "• Variation levels: Noticeable but not jumpy changes" -ForegroundColor White

Write-Host "`nComparison:" -ForegroundColor Yellow
Write-Host "  Original: ±10° wind direction every second (very jumpy)" -ForegroundColor Red
Write-Host "  Previous: ±0.5° wind direction every second (too stable)" -ForegroundColor Red  
Write-Host "  Balanced: ±1.5° micro + ±15° gradual changes (just right!)" -ForegroundColor Green

Write-Host "`n1. Checking existing rover data..."
$roverDataPath = "C:\temp\Rover1\rover_data.gpkg"
if (Test-Path $roverDataPath) {
    Write-Host "??  Found existing rover data - will be replaced with balanced data" -ForegroundColor Yellow
} else {
    Write-Host "? No existing rover data found - will create new balanced data" -ForegroundColor Green
}

Write-Host "`n2. Starting balanced rover simulator..."
Write-Host "   Watch for these indicators:" -ForegroundColor Cyan
Write-Host "   • 'BALANCED TRANSITIONS' in startup message" -ForegroundColor White
Write-Host "   • 'NEW PATTERN' every 3-10 minutes" -ForegroundColor White
Write-Host "   • Gradual wind changes instead of sudden jumps" -ForegroundColor White

Write-Host "`n3. Expected behavior:" -ForegroundColor Cyan
Write-Host "   • Wind direction: Noticeable changes but smooth transitions" -ForegroundColor White
Write-Host "   • Wind speed: Moderate variations, not too stable" -ForegroundColor White
Write-Host "   • Rover movement: Natural wandering with some variation" -ForegroundColor White

Write-Host "`n4. After generating balanced data:" -ForegroundColor Cyan
Write-Host "   • Wind feathers will show coherent patterns" -ForegroundColor White
Write-Host "   • Arrows and feathers will align properly" -ForegroundColor White
Write-Host "   • Combined coverage will be realistic" -ForegroundColor White

Write-Host "`nStarting balanced simulation in 3 seconds..."
Start-Sleep -Seconds 3

Set-Location "C:\source\Foss4gWorkshopDotNet\RoverSimulator"
& dotnet run

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n? Balanced rover simulation completed!" -ForegroundColor Green
    Write-Host "`nNext steps:" -ForegroundColor Cyan
    Write-Host "1. Run: ConvertWinddataToPolygon (to create balanced wind polygons)" -ForegroundColor Yellow
    Write-Host "2. Start: FrontendVersion2 Blazor app (to see balanced visualization)" -ForegroundColor Yellow
    Write-Host "3. Observe: Perfect balance between variation and smoothness!" -ForegroundColor Yellow
    
    Write-Host "`nExpected results:" -ForegroundColor Green
    Write-Host "• Wind arrows show consistent but changing directions" -ForegroundColor White
    Write-Host "• Wind feathers align with arrows and transition smoothly" -ForegroundColor White  
    Write-Host "• Combined coverage shows realistic scent patterns" -ForegroundColor White
    Write-Host "• No more jumpy, erratic wind data!" -ForegroundColor White
} else {
    Write-Host "`n? Balanced simulation failed!" -ForegroundColor Red
}

Write-Host "`nPress any key to continue..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")