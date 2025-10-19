using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ReadRoverDBStubLibrary;
using ScentPolygonLibrary;
using Microsoft.Extensions.Options;

// ====== Simple Rover Visualization Application ======

Console.WriteLine("=== FrontendLeaflet Rover Tracker ===");

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

// Simple scent polygon service
builder.Services.AddSingleton<ScentPolygonService>();
builder.Services.AddHostedService<ScentPolygonService>(provider => provider.GetRequiredService<ScentPolygonService>());

var app = builder.Build();

// Simple middleware setup
app.UseHttpsRedirection();
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

// OPTIMIZED: Latest rover measurement points (limited for performance)
app.MapGet("/api/rover-data", async (IRoverDataReader reader, int? limit = 100) =>
{
    try
    {
        await reader.InitializeAsync();
        
        // Get total count for statistics
        var totalCount = await reader.GetMeasurementCountAsync();
        
        // Get limited number of latest measurements for performance
        var measurements = await reader.GetAllMeasurementsAsync();
        var limitedMeasurements = measurements
            .OrderByDescending(m => m.Sequence)
            .Take(limit ?? 100)
            .OrderBy(m => m.Sequence) // Re-order chronologically
            .ToList();
        
        var features = limitedMeasurements.Select(m => new
        {
            type = "Feature",
            properties = new
            {
                sequence = m.Sequence,
                windDirection = m.WindDirectionDeg,
                windSpeed = m.WindSpeedMps,
                time = m.RecordedAt.ToString("HH:mm:ss")
            },
            geometry = new
            {
                type = "Point",
                coordinates = new[] { m.Longitude, m.Latitude }
            }
        });
        
        return Results.Json(new { 
            type = "FeatureCollection", 
            features,
            metadata = new { 
                totalCount, 
                shownCount = limitedMeasurements.Count,
                isLimited = totalCount > (limit ?? 100)
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { 
            type = "FeatureCollection", 
            features = new object[0],
            error = ex.Message
        });
    }
});

// NEW: Rover trail as a single LineString for better performance with many points
app.MapGet("/api/rover-trail", async (IRoverDataReader reader, int? limit = 500) =>
{
    try
    {
        await reader.InitializeAsync();
        var measurements = await reader.GetAllMeasurementsAsync();
        
        if (!measurements.Any())
            return Results.Json(new { type = "FeatureCollection", features = new object[0] });
        
        // Get limited measurements for the trail
        var limitedMeasurements = measurements
            .OrderBy(m => m.Sequence)
            .TakeLast(limit ?? 500)
            .ToList();
        
        // Create a LineString from the coordinates
        var coordinates = limitedMeasurements
            .Select(m => new[] { m.Longitude, m.Latitude })
            .ToArray();
        
        var lineFeature = new
        {
            type = "Feature",
            properties = new
            {
                name = "Rover Trail",
                pointCount = limitedMeasurements.Count,
                totalPoints = measurements.Count,
                startTime = limitedMeasurements.First().RecordedAt.ToString("HH:mm:ss"),
                endTime = limitedMeasurements.Last().RecordedAt.ToString("HH:mm:ss")
            },
            geometry = new
            {
                type = "LineString",
                coordinates
            }
        };
        
        return Results.Json(new { 
            type = "FeatureCollection", 
            features = new[] { lineFeature }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { 
            type = "FeatureCollection", 
            features = new object[0],
            error = ex.Message
        });
    }
});

// OPTIMIZED: Rover statistics for info display
app.MapGet("/api/rover-stats", async (IRoverDataReader reader) =>
{
    try
    {
        await reader.InitializeAsync();
        var totalCount = await reader.GetMeasurementCountAsync();
        var latest = await reader.GetLatestMeasurementAsync();
        
        return Results.Json(new
        {
            totalMeasurements = totalCount,
            latestSequence = latest?.Sequence ?? -1,
            latestTime = latest?.RecordedAt.ToString("HH:mm:ss"),
            latestPosition = latest != null ? new { 
                lat = latest.Latitude, 
                lng = latest.Longitude,
                windSpeed = latest.WindSpeedMps,
                windDirection = latest.WindDirectionDeg
            } : null
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { 
            error = ex.Message,
            totalMeasurements = 0
        });
    }
});

// NEW: Sampling API - get evenly distributed points for large datasets
app.MapGet("/api/rover-sample", async (IRoverDataReader reader, int? sampleSize = 200) =>
{
    try
    {
        await reader.InitializeAsync();
        var measurements = await reader.GetAllMeasurementsAsync();
        
        if (!measurements.Any())
            return Results.Json(new { type = "FeatureCollection", features = new object[0] });
        
        var orderedMeasurements = measurements.OrderBy(m => m.Sequence).ToList();
        var totalCount = orderedMeasurements.Count;
        var requestedSample = sampleSize ?? 200;
        
        List<RoverMeasurement> sampledMeasurements;
        
        if (totalCount <= requestedSample)
        {
            // If we have fewer points than requested, return all
            sampledMeasurements = orderedMeasurements;
        }
        else
        {
            // Sample evenly across the dataset
            sampledMeasurements = new List<RoverMeasurement>();
            var step = (double)totalCount / requestedSample;
            
            for (int i = 0; i < requestedSample; i++)
            {
                var index = (int)Math.Round(i * step);
                if (index >= totalCount) index = totalCount - 1;
                sampledMeasurements.Add(orderedMeasurements[index]);
            }
        }
        
        var features = sampledMeasurements.Select(m => new
        {
            type = "Feature",
            properties = new
            {
                sequence = m.Sequence,
                windDirection = m.WindDirectionDeg,
                windSpeed = m.WindSpeedMps,
                time = m.RecordedAt.ToString("HH:mm:ss")
            },
            geometry = new
            {
                type = "Point",
                coordinates = new[] { m.Longitude, m.Latitude }
            }
        });
        
        return Results.Json(new { 
            type = "FeatureCollection", 
            features,
            metadata = new {
                totalCount,
                sampleSize = sampledMeasurements.Count,
                samplingRatio = (double)sampledMeasurements.Count / totalCount
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { 
            type = "FeatureCollection", 
            features = new object[0],
            error = ex.Message
        });
    }
});

// Combined coverage area (the only scent visualization we need)
app.MapGet("/api/combined-coverage", (ScentPolygonService scentService) =>
{
    try
    {
        var unified = scentService.GetUnifiedScentPolygonCached();
        if (unified == null || !unified.IsValid)
            return Results.NotFound("No coverage data available");

        var geoJsonWriter = new GeoJsonWriter();
        var geoJsonGeometry = geoJsonWriter.Write(unified.Polygon);

        return Results.Json(new
        {
            type = "FeatureCollection",
            features = new[]
            {
                new
                {
                    type = "Feature",
                    properties = new
                    {
                        totalPolygons = unified.PolygonCount,
                        areaHectares = Math.Round(unified.TotalAreaM2 / 10000.0, 1),
                        avgWindSpeed = Math.Round(unified.AverageWindSpeedMps, 1)
                    },
                    geometry = System.Text.Json.JsonSerializer.Deserialize<object>(geoJsonGeometry)
                }
            }
        });
    }
    catch
    {
        return Results.NotFound();
    }
});

// ====== Helper Functions ======

string FindForestFile()
{
    var possiblePaths = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "..", "Solutionresources", "RiverHeadForest.gpkg"),
        Path.Combine(Directory.GetCurrentDirectory(), "Solutionresources", "RiverHeadForest.gpkg"),
    };
    
    return possiblePaths.FirstOrDefault(File.Exists) ?? possiblePaths[0];
}

Console.WriteLine("Starting FrontendLeaflet rover tracker...");
Console.WriteLine("Performance optimizations enabled for large datasets");
Console.WriteLine("Using Leaflet.js for interactive mapping");
Console.WriteLine("Open your browser to see the clean map visualization");

app.Run();
