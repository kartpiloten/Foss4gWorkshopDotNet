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

// Register ForestBoundaryReader as a singleton (forest boundary doesn't change)
builder.Services.AddSingleton<ForestBoundaryReader>(provider =>
{
    return new ForestBoundaryReader(forestPath);
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

Console.WriteLine("Starting FrontendLeaflet rover tracker...");
Console.WriteLine("Open your browser to see the clean map visualization");

app.Run();
