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

// Session selection using library discovery
static async Task<(string sessionName, Guid? sessionId)> SelectSessionAsync(IConfiguration configuration)
{
    var config = new DatabaseConfiguration
    {
        DatabaseType = configuration.GetValue<string>("DatabaseConfiguration:DatabaseType") ?? "geopackage",
        PostgresConnectionString = configuration.GetValue<string>("DatabaseConfiguration:PostgresConnectionString"),
        GeoPackageFolderPath = configuration.GetValue<string>("DatabaseConfiguration:GeoPackageFolderPath")
    };
    var sessions = await SessionDiscovery.ListSessionsAsync(config);
    if (sessions.Count == 0)
    {
        Console.WriteLine("No sessions found. Press Enter to exit.");
        Console.ReadLine();
        Environment.Exit(0);
    }
    Console.WriteLine("\nAvailable sessions:");
    for (int i = 0; i < sessions.Count; i++)
    {
        var s = sessions[i];
        var last = s.LastMeasurement.HasValue ? s.LastMeasurement.Value.ToString("yyyy-MM-dd HH:mm:ss") : "-";
        Console.WriteLine($" {i + 1}. {s.Name} ({s.MeasurementCount} rows, last: {last})");
    }
    Console.Write("\nEnter session name or number to present: ");
    var input = (Console.ReadLine() ?? string.Empty).Trim();
    if (int.TryParse(input, out var n) && n >= 1 && n <= sessions.Count)
    {
        var s = sessions[n - 1];
        return (s.Name, s.SessionId);
    }
    var byName = sessions.FirstOrDefault(s => s.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
    if (byName != null)
        return (byName.Name, byName.SessionId);
    var def = sessions[0];
    return (def.Name, def.SessionId);
}

var selected = await SelectSessionAsync(builder.Configuration);
Console.WriteLine($"Presenting session: {selected.sessionName}");

// Simple rover data reader configuration (session-aware)
builder.Services.AddSingleton<IRoverDataReader>(provider =>
{
    var options = provider.GetRequiredService<IOptions<DatabaseConfiguration>>();
    var databaseConfig = options.Value ?? throw new InvalidOperationException("Database configuration is unavailable.");

    IRoverDataReader reader;
    if (databaseConfig.DatabaseType.Equals("postgres", StringComparison.OrdinalIgnoreCase) && selected.sessionId.HasValue)
    {
        reader = new PostgresRoverDataReader(databaseConfig.PostgresConnectionString!, selected.sessionId);
    }
    else if (databaseConfig.DatabaseType.Equals("geopackage", StringComparison.OrdinalIgnoreCase))
    {
        // session-specific file path
        var sessionPath = Path.Combine(databaseConfig.GeoPackageFolderPath ?? "", $"session_{selected.sessionName}.gpkg");
        reader = new GeoPackageRoverDataReader(sessionPath);
    }
    else
    {
        reader = RoverDataReaderFactory.CreateReader(databaseConfig);
    }

    reader.InitializeAsync().GetAwaiter().GetResult();
    Console.WriteLine($"Rover data reader initialized for '{databaseConfig.DatabaseType}', session '{selected.sessionName}'.");
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

// ====== API Endpoints ======

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
                        minLat = envelope.MinY,
                        maxLat = envelope.MaxY,
                        minLng = envelope.MinX,
                        maxLng = envelope.MaxX
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

        return Results.Json(new
        {
            type = "FeatureCollection",
            features,
            metadata = new
            {
                totalCount,
                shownCount = limitedMeasurements.Count,
                isLimited = totalCount > (limit ?? 100)
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            type = "FeatureCollection",
            features = new object[0],
            error = ex.Message
        });
    }
});

// NEW: Rover trail as a single LineString for better performance with many points
// If 'after' timestamp is provided, only returns points after that time
app.MapGet("/api/rover-trail", async (IRoverDataReader reader, string? after = null) =>
{
    try
    {
        var measurements = await reader.GetAllMeasurementsAsync();

        if (!measurements.Any())
            return Results.Json(new { type = "FeatureCollection", features = new object[0] });

        // Filter by timestamp if 'after' parameter is provided
        var filteredMeasurements = measurements.OrderBy(m => m.Sequence);

        if (!string.IsNullOrEmpty(after) && DateTime.TryParse(after, out var afterTime))
        {
            filteredMeasurements = filteredMeasurements.Where(m => m.RecordedAt > afterTime).OrderBy(m => m.Sequence);
        }

        var limitedMeasurements = filteredMeasurements.ToList();

        if (!limitedMeasurements.Any())
            return Results.Json(new { type = "FeatureCollection", features = new object[0] });

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
                totalPoints = measurements.Count(),
                startTime = limitedMeasurements.First().RecordedAt.ToString("o"),
                endTime = limitedMeasurements.Last().RecordedAt.ToString("o")
            },
            geometry = new
            {
                type = "LineString",
                coordinates
            }
        };

        return Results.Json(new
        {
            type = "FeatureCollection",
            features = new[] { lineFeature }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
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
        var totalCount = await reader.GetMeasurementCountAsync();
        var latest = await reader.GetLatestMeasurementAsync();

        return Results.Json(new
        {
            totalMeasurements = totalCount,
            latestSequence = latest?.Sequence ?? -1,
            latestTime = latest?.RecordedAt.ToString("HH:mm:ss"),
            latestPosition = latest != null ? new
            {
                lat = latest.Latitude,
                lng = latest.Longitude,
                windSpeed = latest.WindSpeedMps,
                windDirection = latest.WindDirectionDeg
            } : null
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
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

        return Results.Json(new
        {
            type = "FeatureCollection",
            features,
            metadata = new
            {
                totalCount,
                sampleSize = sampledMeasurements.Count,
                samplingRatio = (double)sampledMeasurements.Count / totalCount
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
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

// NEW: per-rover unified polygons as individual features
app.MapGet("/api/rovers-coverage", (ScentPolygonService scentService) =>
{
    try
    {
        var rovers = scentService.GetAllRoverUnifiedPolygons();
        var geoJsonWriter = new GeoJsonWriter();
        var features = new List<object>();
        foreach (var r in rovers)
        {
            if (r.UnifiedPolygon == null || !r.UnifiedPolygon.IsValid) continue;
            var geom = geoJsonWriter.Write(r.UnifiedPolygon);
            features.Add(new
            {
                type = "Feature",
                properties = new
                {
                    roverId = r.RoverId,
                    roverName = r.RoverName,
                    polygonCount = r.PolygonCount,
                    areaM2 = r.TotalAreaM2,
                    version = r.Version,
                    latestSequence = r.LatestSequence
                },
                geometry = System.Text.Json.JsonSerializer.Deserialize<object>(geom)
            });
        }
        return Results.Json(new { type = "FeatureCollection", features });
    }
    catch (Exception ex)
    {
        return Results.Json(new { type = "FeatureCollection", features = Array.Empty<object>(), error = ex.Message });
    }
});

// NEW: Latest per-rover stats (one marker per rover)
app.MapGet("/api/rovers-stats", async (IRoverDataReader reader) =>
{
    try
    {
        var all = await reader.GetAllMeasurementsAsync();
        var latestByRover = all
        .GroupBy(m => new { m.RoverId, m.RoverName })
        .Select(g => g.OrderByDescending(x => x.RecordedAt).First())
        .ToList();

        var items = latestByRover.Select(m => new
        {
            roverId = m.RoverId,
            roverName = m.RoverName,
            latestSequence = m.Sequence,
            latestTime = m.RecordedAt.ToString("o"),
            position = new { lat = m.Latitude, lng = m.Longitude },
            windSpeed = m.WindSpeedMps,
            windDirection = m.WindDirectionDeg
        });

        return Results.Json(new { count = latestByRover.Count, rovers = items });
    }
    catch (Exception ex)
    {
        return Results.Json(new { count = 0, rovers = Array.Empty<object>(), error = ex.Message });
    }
});

// NEW: Per-rover trails as LineStrings (one feature per rover)
app.MapGet("/api/rover-trails", async (IRoverDataReader reader) =>
{
    try
    {
        var all = await reader.GetAllMeasurementsAsync();
        if (!all.Any())
            return Results.Json(new { type = "FeatureCollection", features = Array.Empty<object>() });

        var features = new List<object>();
        foreach (var g in all.GroupBy(m => new { m.RoverId, m.RoverName }))
        {
            var ordered = g.OrderBy(m => m.Sequence).ToList();
            var coords = ordered.Select(m => new[] { m.Longitude, m.Latitude }).ToArray();
            if (coords.Length < 2) continue;
            features.Add(new
            {
                type = "Feature",
                properties = new
                {
                    roverId = g.Key.RoverId,
                    roverName = g.Key.RoverName,
                    pointCount = ordered.Count,
                    startTime = ordered.First().RecordedAt.ToString("o"),
                    endTime = ordered.Last().RecordedAt.ToString("o")
                },
                geometry = new { type = "LineString", coordinates = coords }
            });
        }

        return Results.Json(new { type = "FeatureCollection", features });
    }
    catch (Exception ex)
    {
        return Results.Json(new { type = "FeatureCollection", features = Array.Empty<object>(), error = ex.Message });
    }
});

Console.WriteLine("Starting FrontendLeaflet rover tracker...");
Console.WriteLine("Using Leaflet.js for interactive mapping");
Console.WriteLine("Open your browser to see the rover polygons (per-rover + combined)");

app.Run();
