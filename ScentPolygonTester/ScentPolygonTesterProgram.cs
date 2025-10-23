/*
 The functionallity in this file is:
 - Minimal console tester to run ScentPolygonService and write a unified polygon to a GeoPackage.
 - Demonstrates async/await, events, and basic FOSS4G concepts (OGC GeoPackage).
 - Simplified per copilot-instructions.md (reduced logging and configuration noise).
*/

using MapPiloteGeopackageHelper; // FOSS4G: OGC GeoPackage helper
using Microsoft.Extensions.Configuration; // .NET configuration (appsettings.json)
using ReadRoverDBStubLibrary; // Reader abstraction + factory
using ScentPolygonLibrary; // Scent polygon service
using System.Globalization; // Invariant formatting
using ScentPolygonTester; // Access TesterConfiguration

// ---- Minimal configuration bootstrap ----
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();
TesterConfiguration.Initialize(configuration);

Console.WriteLine("ScentPolygonTester - starting");
Console.WriteLine($"Output: {TesterConfiguration.OutputGeoPackagePath}");

// Cooperative cancellation (Ctrl+C)
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

double? lastUnifiedAreaM2 = null; // Track last unified polygon area to detect size changes

// Resolve forest file path once (used in update block)
string forestPath = FindForestFile();

// Create data reader with validation (GeoPackage or Postgres)
IRoverDataReader dataReader;
try
{
    var dbConfig = TesterConfiguration.CreateDatabaseConfig();
    dataReader = await RoverDataReaderFactory.CreateReaderWithValidationAsync(dbConfig, cts.Token);
}
catch (Exception ex)
{
    Console.WriteLine($"Data source unavailable: {ex.Message}");
    return;
}

// Scent model parameters
var scentConfig = new ScentPolygonConfiguration
{
    OmnidirectionalRadiusMeters = 30.0,
    FanPolygonPoints = 15,
    MinimumDistanceMultiplier = 0.4
};

using var scentService = new ScentPolygonService(dataReader, scentConfig, pollIntervalMs: 1000);
using var geoPackageUpdater = new GeoPackageUpdater(TesterConfiguration.OutputGeoPackagePath);
await geoPackageUpdater.InitializeAsync();

// Subscribe to polygon updates
    scentService.PolygonsUpdated += async (sender, args) =>
    {
        Console.WriteLine($"[GeoPackage] Polygon update received: {args.NewPolygons.Count} new polygons, total: {args.TotalPolygonCount}");

        // Update unified polygon in GeoPackage
        await geoPackageUpdater.UpdateUnifiedPolygonAsync(scentService);

        // Check if unified polygon size changed and, if so, compute and print forest + unified areas
        try
        {
            var unified = scentService.GetUnifiedScentPolygonCached();
            if (unified != null && unified.IsValid)
            {
                var currentArea = unified.TotalAreaM2;
                // Consider any non-trivial change (tolerance 0.5 m^2 to avoid noise)
                if (lastUnifiedAreaM2 is null || Math.Abs(currentArea - lastUnifiedAreaM2.Value) > 0.5)
                {
                    lastUnifiedAreaM2 = currentArea;

                    var (intersectM2, forestM2) = await scentService.GetForestIntersectionAreasAsync(forestPath);
                    int AreaCoveredPercent = forestM2 > 0 ? (int)Math.Round(((double)intersectM2 / forestM2) * 100) : 0;
                    Console.WriteLine("\nCoverage sizes (m²):");
                    Console.WriteLine($"  Unified (scent):    {currentArea:n0} m²");
                    Console.WriteLine($"  RiverHead forest:   {forestM2:n0} m²");
                    Console.WriteLine($"  Intersection:       {intersectM2:n0} m²");
                    Console.WriteLine($"  Area covered:       {AreaCoveredPercent:n0}%");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Tester] Forest coverage update failed: {ex.Message}");
        }
    };

// Optional: status tick
scentService.StatusUpdate += (sender, args) =>
{
    Console.WriteLine($"[Status] Total polygons: {args.TotalPolygonCount}, Latest: {args.LatestPolygon?.Sequence ?? -1}");
};

Console.WriteLine("Starting service... (Ctrl+C to stop)");
await scentService.StartAsync(cts.Token);

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException) { }
finally
{
    await scentService.StopAsync(CancellationToken.None);
}

Console.WriteLine("Cleanup complete.");

// ---- Helpers ----

static string FindForestFile()
{
    // Walk up from base directory and look for Solutionresources/RiverHeadForest.gpkg
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "Solutionresources", "RiverHeadForest.gpkg");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    var fallbacks = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "Solutionresources", "RiverHeadForest.gpkg"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "Solutionresources", "RiverHeadForest.gpkg"),
    };
    return fallbacks.FirstOrDefault(File.Exists) ?? fallbacks[0];
}

/// <summary>
/// Minimal GeoPackage updater: ensures a POLYGON layer and replaces a single unified feature on updates.
/// </summary>
public sealed class GeoPackageUpdater : IDisposable
{
    private readonly string _path;
    private GeoPackage? _gpkg;
    private GeoPackageLayer? _layer;
    private bool _disposed;

    public GeoPackageUpdater(string path) => _path = path;

    public async Task InitializeAsync()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(_path)) { try { File.Delete(_path); } catch { } }

        _gpkg = await GeoPackage.OpenAsync(_path, 4326); // EPSG:4326
        var schema = new Dictionary<string, string>
        {
            ["polygon_count"] = "INTEGER NOT NULL",
            ["total_area_m2"] = "REAL NOT NULL",
            ["created_at"] = "TEXT NOT NULL",
            ["unified_version"] = "INTEGER NOT NULL"
        };
        _layer = await _gpkg.EnsureLayerAsync("unified", schema, 4326, "POLYGON");
    }

    public async Task UpdateUnifiedPolygonAsync(ScentPolygonService service)
    {
        if (_disposed || _gpkg == null || _layer == null) return;
        var unified = service.GetUnifiedScentPolygonCached();
        if (unified == null || !unified.IsValid) return;

        await _layer.DeleteAsync("1=1");
        var attrs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["polygon_count"] = unified.PolygonCount.ToString(CultureInfo.InvariantCulture),
            ["total_area_m2"] = unified.TotalAreaM2.ToString("F2", CultureInfo.InvariantCulture),
            ["created_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["unified_version"] = service.UnifiedVersion.ToString(CultureInfo.InvariantCulture)
        };
        var feature = new FeatureRecord(unified.Polygon, attrs);
        await _layer.BulkInsertAsync(new[] { feature }, new BulkInsertOptions(BatchSize: 1, CreateSpatialIndex: true, ConflictPolicy: ConflictPolicy.Replace), null, CancellationToken.None);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { _layer = null; _gpkg?.Dispose(); } catch { }
    }
}
