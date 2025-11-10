using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using RoverData.Repository;
using ScentPolygonLibrary;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

// ====== Simple Rover Visualization Application ======

Console.WriteLine("=== FrontendLeaflet Rover Tracker ===");

var builder = WebApplication.CreateBuilder(args);

// Basic services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Configure repository based on appsettings.json
var dbType = builder.Configuration["DatabaseConfiguration:DatabaseType"] ?? "geopackage";
var sessionName = builder.Configuration["DatabaseConfiguration:SessionName"] ?? "default";

if (dbType.ToLower() == "postgres")
{
    // Register NpgsqlDataSource as a singleton (connection pool)
    var connStr = builder.Configuration["DatabaseConfiguration:PostgresConnectionString"] ?? "";
    builder.Services.AddNpgsqlDataSource(connStr, dataSourceBuilder =>
    {
        dataSourceBuilder.UseNetTopologySuite();
    });

    // Register SessionRepository for session management
    builder.Services.AddSingleton<ISessionRepository, SessionRepository>();

    // Initialize database schema once at startup
    builder.Services.AddSingleton<PostgresDatabaseInitializer>();

    // Register session context as scoped (for future per-request session selection)
    // Initialize session once at startup and cache the ID
    Guid sessionId;
    using (var tempScope = builder.Services.BuildServiceProvider().CreateScope())
    {
        var sessionRepo = tempScope.ServiceProvider.GetRequiredService<ISessionRepository>();
        sessionId = await sessionRepo.RegisterOrGetSessionAsync(sessionName);
        Console.WriteLine($"Session registered: {sessionName} -> {sessionId}");
    }
    
    builder.Services.AddScoped<ISessionContext>(provider =>
    {
        return new WebSessionContext(sessionId, sessionName);
    });

    // Register scoped repository
    builder.Services.AddScoped<IRoverDataRepository, PostgresRoverDataRepository>();
}
else
{
    builder.Services.Configure<GeoPackageRepositoryOptions>(options =>
    {
        options.FolderPath = builder.Configuration["DatabaseConfiguration:GeoPackageFolderPath"] ?? "/tmp/Rover1";
    });

    // For GeoPackage, session context is simpler (filename-based)
    builder.Services.AddScoped<ISessionContext>(provider =>
    {
        // Generate a session ID for GeoPackage (file-based, so just need unique ID)
        return new WebSessionContext(Guid.NewGuid(), sessionName);
    });

    builder.Services.AddScoped<IRoverDataRepository, GeoPackageRoverDataRepository>();
}

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

// Register memory cache for polygon caching
builder.Services.AddMemoryCache();

// Register ScentPolygonGenerator as scoped (matches repository lifetime)
// Note: IMemoryCache is still singleton, so cache is shared across all requests
builder.Services.AddScoped<ScentPolygonGenerator>(provider =>
{
    var repo = provider.GetRequiredService<IRoverDataRepository>();
    var cache = provider.GetRequiredService<IMemoryCache>();
    var config = new ScentPolygonConfiguration(); // Use default configuration
    return new ScentPolygonGenerator(repo, cache, config, forestPath);
});

var app = builder.Build();

// Initialize database schema if using Postgres
if (dbType.ToLower() == "postgres")
{
    using var scope = app.Services.CreateScope();
    var initializer = scope.ServiceProvider.GetRequiredService<PostgresDatabaseInitializer>();
    await initializer.InitializeSchemaAsync();
    Console.WriteLine("PostgreSQL schema initialized.");
}

// Simple middleware setup
//app.UseHttpsRedirection();
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

Console.WriteLine("Starting FrontendLeaflet rover tracker...");
Console.WriteLine("Performance optimizations enabled for large datasets");
Console.WriteLine("Using Leaflet.js for interactive mapping");
Console.WriteLine("Open your browser to see the clean map visualization");

app.Run();
