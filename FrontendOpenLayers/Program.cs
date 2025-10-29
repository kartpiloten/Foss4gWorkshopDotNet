using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ReadRoverDBStubLibrary;
using ScentPolygonLibrary;
using Microsoft.Extensions.Options;

// ====== Simple Rover Visualization Application ======

Console.WriteLine("=== FrontendOpenLayers Rover Tracker ===");

var builder = WebApplication.CreateBuilder(args);

// Basic services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.Configure<DatabaseConfiguration>(
    builder.Configuration.GetSection("DatabaseConfiguration"));

// Simple rover data reader configuration
builder.Services.AddSingleton<IRoverDataReader>(provider =>
{
    var options = provider.GetRequiredService<IOptions<DatabaseConfiguration>>();
    var databaseConfig = options.Value ?? throw new InvalidOperationException("Database configuration is unavailable.");

    var reader = RoverDataReaderFactory.CreateReader(databaseConfig);
    reader.InitializeAsync().GetAwaiter().GetResult();
    Console.WriteLine($"Rover data reader initialized for database type '{databaseConfig.DatabaseType}'.");
    return reader;
});

// Find forest file path
string FindForestFile()
{
    var possiblePaths = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "..", "Solutionresources", "RiverHeadForest.gpkg"),
        Path.Combine(Directory.GetCurrentDirectory(), "Solutionresources", "RiverHeadForest.gpkg"),
    };
    
    return possiblePaths.FirstOrDefault(File.Exists) ?? possiblePaths[0];
}

var forestPath = FindForestFile();

// Simple scent polygon service with forest path for real-time updates
builder.Services.AddSingleton<ScentPolygonService>(provider =>
{
    var reader = provider.GetRequiredService<IRoverDataReader>();
    return new ScentPolygonService(reader, forestGeoPackagePath: forestPath);
});
builder.Services.AddHostedService<ScentPolygonService>(provider => provider.GetRequiredService<ScentPolygonService>());

var app = builder.Build();

// Simple middleware setup
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// ====== API Endpoints (Optimized for Performance) ======

// Basic health check
app.MapGet("/api/test", () => Results.Json(new { status = "OK", time = DateTime.Now }));

// Forest boundary data
app.MapGet("/api/forest", async () =>
{
    try
    {
        var forestPath = FindForestFile();
        if (!File.Exists(forestPath))
            return Results.NotFound("Forest data not found");

        using var geoPackage = await GeoPackage.OpenAsync(forestPath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("riverheadforest", new Dictionary<string, string>(), 4326);
        
        await foreach (var feature in layer.ReadFeaturesAsync(new ReadOptions(IncludeGeometry: true, Limit: 1)))
        {
            if (feature.Geometry is Polygon polygon)
            {
                var geoJsonWriter = new GeoJsonWriter();
                var geoJsonGeometry = geoJsonWriter.Write(polygon);
                
                return Results.Json(new
                {
                    type = "FeatureCollection",
                    features = new[]
                    {
                        new
                        {
                            type = "Feature",
                            properties = new { name = "RiverHead Forest" },
                            geometry = System.Text.Json.JsonSerializer.Deserialize<object>(geoJsonGeometry)
                        }
                    }
                });
            }
        }
        
        return Results.NotFound("No forest data found");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
});

// Forest bounds for map centering
app.MapGet("/api/forest-bounds", async () =>
{
    try
    {
        var forestPath = FindForestFile();
        if (!File.Exists(forestPath))
            return Results.NotFound();

        using var geoPackage = await GeoPackage.OpenAsync(forestPath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("riverheadforest", new Dictionary<string, string>(), 4326);
        
        await foreach (var feature in layer.ReadFeaturesAsync(new ReadOptions(IncludeGeometry: true, Limit: 1)))
        {
            if (feature.Geometry is Polygon polygon)
            {
                var envelope = polygon.EnvelopeInternal;
                var centroid = polygon.Centroid;
                
                return Results.Json(new
                {
                    center = new { lat = centroid.Y, lng = centroid.X },
                    bounds = new
                    {
                        minLat = envelope.MinY, maxLat = envelope.MaxY,
                        minLng = envelope.MinX, maxLng = envelope.MaxX
                    }
                });
            }
        }
        
        return Results.NotFound();
    }
    catch
    {
        return Results.NotFound();
    }
});

// ====== Helper Functions ======

Console.WriteLine("Starting FrontendOpenLayers rover tracker...");
Console.WriteLine("Performance optimizations enabled for large datasets");
Console.WriteLine("Individual wind polygons removed to reduce visual clutter");
Console.WriteLine("Open your browser to see the clean map visualization");

app.Run();
