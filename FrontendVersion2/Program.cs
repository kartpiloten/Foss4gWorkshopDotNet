using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using MapPiloteGeopackageHelper;
using NetTopologySuite.IO;
using NetTopologySuite.Geometries;
using System.Collections.Concurrent;
using System.Globalization;
using ReadRoverDBStubLibrary;
using ScentPolygonLibrary;

// ====== Application Startup (Top-level statements) ======

Console.WriteLine("=== Starting FrontendVersion2 Rover Application ===");

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(o =>
    {
        if (builder.Environment.IsDevelopment())
        {
            o.DetailedErrors = true;
        }
    });

// Add rover data reader with proper configuration
builder.Services.AddSingleton<IRoverDataReader>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<IRoverDataReader>>();
    try
    {
        // FrontendVersion2 application configuration
        var config = new DatabaseConfiguration
        {
            DatabaseType = "postgres",
            PostgresConnectionString = "Host=192.168.1.254;Port=5432;Username=anders;Password=tua123;Database=AucklandRoverData;Timeout=10;Command Timeout=30",
            ConnectionTimeoutSeconds = ReaderDefaults.DEFAULT_CONNECTION_TIMEOUT_SECONDS,
            MaxRetryAttempts = ReaderDefaults.DEFAULT_MAX_RETRY_ATTEMPTS,
            RetryDelayMs = ReaderDefaults.DEFAULT_RETRY_DELAY_MS
        };
        
        logger.LogInformation("Configuring PostgreSQL database connection to: {Host}", "192.168.1.254:5432");
        var reader = RoverDataReaderFactory.CreateReader(config);
        return reader;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure PostgreSQL reader");
        throw; // Throw to prevent application from starting without database
    }
});

// Add ScentPolygonService configuration
builder.Services.AddSingleton(new ScentPolygonConfiguration
{
    OmnidirectionalRadiusMeters = 30.0,
    FanPolygonPoints = 15,
    MinimumDistanceMultiplier = 0.4
});

// Add ScentPolygonService as a hosted service so it starts automatically
builder.Services.AddSingleton<ScentPolygonService>();
builder.Services.AddHostedService<ScentPolygonService>(provider => provider.GetRequiredService<ScentPolygonService>());

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// Global state for tracking rover data updates
var lastKnownSequence = new ConcurrentDictionary<string, int>();

// Helper method to find GeoPackage file
string FindGeoPackageFile()
{
    var possiblePaths = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "..", "Solutionresources", "RiverHeadForest.gpkg"),
        Path.Combine(Directory.GetCurrentDirectory(), "Solutionresources", "RiverHeadForest.gpkg"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Solutionresources", "RiverHeadForest.gpkg")
    };
    
    foreach (var path in possiblePaths)
    {
        if (File.Exists(path))
        {
            logger.LogInformation("Found GeoPackage at: {Path}", Path.GetFullPath(path));
            return path;
        }
    }
    
    return possiblePaths[0]; // Fallback
}

// Helper method to find the latest wind polygon file
string FindLatestWindPolygonFile()
{
    const string roverDataPath = @"C:\temp\Rover1\";
    
    if (!Directory.Exists(roverDataPath))
        return string.Empty;

    // First, try the specific file from ConvertWinddataToPolygon
    var specificFile = Path.Combine(roverDataPath, "rover_windpolygon.gpkg");
    if (File.Exists(specificFile))
    {
        return specificFile;
    }

    // Fallback to timestamped files if the main file doesn't exist
    var windPolygonFiles = Directory.GetFiles(roverDataPath, "rover_windpolygon*.gpkg")
        .OrderByDescending(f => new FileInfo(f).LastWriteTime)
        .ToArray();

    return windPolygonFiles.Length > 0 ? windPolygonFiles[0] : string.Empty;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Configure static files with proper options
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Add headers for better debugging
        if (ctx.File.Name.EndsWith(".js"))
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "no-cache");
            logger.LogInformation("Serving JavaScript file: {Path}", ctx.File.PhysicalPath);
        }
    }
});

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// API Endpoints
app.MapGet("/api/test", () => Results.Json(new { status = "OK", timestamp = DateTime.UtcNow }));

// Debug endpoint to check JavaScript file
app.MapGet("/api/debug/js-file", () =>
{
    var jsPath = Path.Combine(app.Environment.WebRootPath, "js", "leafletInit.js");
    var exists = File.Exists(jsPath);
    
    if (exists)
    {
        var fileInfo = new FileInfo(jsPath);
        var content = File.ReadAllText(jsPath);
        var lines = content.Split('\n').Length;
        
        return Results.Json(new 
        {
            fileExists = true,
            path = jsPath,
            size = fileInfo.Length,
            lastModified = fileInfo.LastWriteTime,
            lineCount = lines,
            firstLines = content.Split('\n').Take(5).ToArray()
        });
    }
    
    return Results.Json(new { fileExists = false, path = jsPath });
});

app.MapGet("/api/riverhead-forest", async () =>
{
    try
    {
        var geoPackagePath = FindGeoPackageFile();
        if (!File.Exists(geoPackagePath))
            return Results.NotFound("Forest data not found");

        using var geoPackage = await GeoPackage.OpenAsync(geoPackagePath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("riverheadforest", new Dictionary<string, string>(), 4326);
        var readOptions = new ReadOptions(IncludeGeometry: true, Limit: 1);
        
        await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
        {
            if (feature.Geometry is Polygon polygon)
            {
                var geoJsonWriter = new GeoJsonWriter();
                var geoJsonGeometry = geoJsonWriter.Write(polygon);
                
                var featureCollection = new
                {
                    type = "FeatureCollection",
                    features = new[]
                    {
                        new
                        {
                            type = "Feature",
                            properties = new { name = "RiverHead Forest", description = "Forest boundary" },
                            geometry = System.Text.Json.JsonSerializer.Deserialize<object>(geoJsonGeometry)
                        }
                    }
                };
                
                return Results.Json(featureCollection);
            }
        }
        
        return Results.NotFound("No forest polygon found");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error loading forest polygon");
        return Results.Problem($"Error loading forest polygon: {ex.Message}");
    }
});

app.MapGet("/api/forest-bounds", async () =>
{
    try
    {
        var geoPackagePath = FindGeoPackageFile();
        if (!File.Exists(geoPackagePath))
            return Results.NotFound("Forest data not found");

        using var geoPackage = await GeoPackage.OpenAsync(geoPackagePath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("riverheadforest", new Dictionary<string, string>(), 4326);
        var readOptions = new ReadOptions(IncludeGeometry: true, Limit: 1);
        
        await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
        {
            if (feature.Geometry is Polygon polygon)
            {
                var envelope = polygon.EnvelopeInternal;
                var centroid = polygon.Centroid;
                
                return Results.Json(new
                {
                    bounds = new
                    {
                        minLat = envelope.MinY,
                        maxLat = envelope.MaxY,
                        minLng = envelope.MinX,
                        maxLng = envelope.MaxX
                    },
                    center = new { lat = centroid.Y, lng = centroid.X }
                });
            }
        }
        
        return Results.NotFound("No forest bounds found");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting bounds: {ex.Message}");
    }
});

app.MapGet("/api/rover-data", async (IRoverDataReader roverReader) =>
{
    try
    {
        await roverReader.InitializeAsync();
        var measurements = await roverReader.GetAllMeasurementsAsync();
        
        var features = measurements.Select(m => new
        {
            type = "Feature",
            properties = new
            {
                sessionId = m.SessionId.ToString(),
                sequence = m.Sequence,
                recordedAt = m.RecordedAt.ToString("O"),
                windDirectionDeg = m.WindDirectionDeg,
                windSpeedMps = m.WindSpeedMps,
                timestamp = m.RecordedAt.ToUnixTimeSeconds()
            },
            geometry = new
            {
                type = "Point",
                coordinates = new[] { m.Longitude, m.Latitude }
            }
        }).ToArray();
        
        return Results.Json(new { type = "FeatureCollection", features });
    }
    catch (Exception ex)
    {
        return Results.Json(new { type = "FeatureCollection", features = new object[0] });
    }
});

app.MapGet("/api/rover-data/updates", async (IRoverDataReader roverReader, string? clientId = null) =>
{
    try
    {
        await roverReader.InitializeAsync();
        clientId ??= "default";
        var lastSequence = lastKnownSequence.GetValueOrDefault(clientId, -1);
        var newMeasurements = await roverReader.GetNewMeasurementsAsync(lastSequence);
        
        if (newMeasurements.Any())
        {
            var maxSequence = newMeasurements.Max(m => m.Sequence);
            lastKnownSequence.AddOrUpdate(clientId, maxSequence, (key, oldValue) => Math.Max(oldValue, maxSequence));
        }
        
        var features = newMeasurements.Select(m => new
        {
            type = "Feature",
            properties = new
            {
                sessionId = m.SessionId.ToString(),
                sequence = m.Sequence,
                recordedAt = m.RecordedAt.ToString("O"),
                windDirectionDeg = m.WindDirectionDeg,
                windSpeedMps = m.WindSpeedMps,
                timestamp = m.RecordedAt.ToUnixTimeSeconds()
            },
            geometry = new
            {
                type = "Point",
                coordinates = new[] { m.Longitude, m.Latitude }
            }
        }).ToArray();
        
        return Results.Json(new { type = "FeatureCollection", features, metadata = new { count = features.Length } });
    }
    catch (Exception)
    {
        return Results.Json(new { type = "FeatureCollection", features = new object[0], metadata = new { count = 0 } });
    }
});

app.MapGet("/api/rover-data/stats", async (IRoverDataReader roverReader) =>
{
    try
    {
        await roverReader.InitializeAsync();
        var totalCount = await roverReader.GetMeasurementCountAsync();
        var latestMeasurement = await roverReader.GetLatestMeasurementAsync();
        
        return Results.Json(new
        {
            totalMeasurements = totalCount,
            latestSequence = latestMeasurement?.Sequence ?? -1,
            latestTimestamp = latestMeasurement?.RecordedAt.ToString("O"),
            latestPosition = latestMeasurement != null ? new { latitude = latestMeasurement.Latitude, longitude = latestMeasurement.Longitude } : null
        });
    }
    catch (Exception)
    {
        return Results.Json(new { totalMeasurements = 0, latestSequence = -1 });
    }
});

app.MapGet("/api/wind-polygons", async () =>
{
    try
    {
        var windPolygonPath = FindLatestWindPolygonFile();
        if (string.IsNullOrEmpty(windPolygonPath) || !File.Exists(windPolygonPath))
            return Results.NotFound("Wind polygon data not available");

        using var geoPackage = await GeoPackage.OpenAsync(windPolygonPath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("wind_scent_polygons", new Dictionary<string, string>(), 4326);
        var readOptions = new ReadOptions(IncludeGeometry: true, OrderBy: "sequence DESC", Limit: 50);
        
        var features = new List<object>();
        
        await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
        {
            if (feature.Geometry is Polygon polygon && polygon.IsValid)
            {
                var geoJsonWriter = new GeoJsonWriter();
                var geoJsonGeometry = System.Text.Json.JsonSerializer.Deserialize<object>(geoJsonWriter.Write(polygon));
                
                features.Add(new
                {
                    type = "Feature",
                    properties = new
                    {
                        sessionId = feature.Attributes["session_id"],
                        sequence = int.Parse(feature.Attributes["sequence"] ?? "0", CultureInfo.InvariantCulture),
                        recordedAt = feature.Attributes["recorded_at"],
                        windDirectionDeg = int.Parse(feature.Attributes["wind_direction_deg"] ?? "0", CultureInfo.InvariantCulture),
                        windSpeedMps = double.Parse(feature.Attributes["wind_speed_mps"] ?? "0", CultureInfo.InvariantCulture),
                        scentAreaM2 = double.Parse(feature.Attributes["scent_area_m2"] ?? "0", CultureInfo.InvariantCulture),
                        maxDistanceM = double.Parse(feature.Attributes["max_distance_m"] ?? "0", CultureInfo.InvariantCulture)
                    },
                    geometry = geoJsonGeometry
                });
            }
        }
        
        return Results.Json(new { type = "FeatureCollection", features });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error loading wind polygons: {ex.Message}");
    }
});

app.MapGet("/api/combined-coverage", async (ScentPolygonService scentService, ILogger<Program> logger, HttpContext http) =>
{
    try
    {
        // Ensure no caching
        http.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        http.Response.Headers["Pragma"] = "no-cache";
        http.Response.Headers["Expires"] = "0";
        http.Response.Headers["ETag"] = $"\"{scentService.UnifiedVersion}\"";

        var measurementCount = scentService.Count;
        var unified = scentService.GetUnifiedScentPolygonCached();
        var latestSeq = scentService.LatestPolygon?.Sequence ?? -1;
        var version = scentService.UnifiedVersion;

        logger.LogInformation("Combined coverage requested: polygons={Count}, version={Version}, latestSeq={LatestSeq}", 
            measurementCount, version, latestSeq);

        if (unified == null || !unified.IsValid)
        {
            logger.LogWarning("No valid unified scent polygon available: polygons={Count}, latestSeq={LatestSeq}", 
                measurementCount, latestSeq);
            return Results.NotFound(new { 
                error = "No valid unified scent polygon available", 
                details = new { measurementCount, latestSequence = latestSeq, version },
                timestamp = DateTimeOffset.UtcNow
            });
        }

        var geoJsonWriter = new GeoJsonWriter();
        var geoJsonGeometry = System.Text.Json.JsonSerializer.Deserialize<object>(geoJsonWriter.Write(unified.Polygon));

        var response = new
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
                        totalAreaM2 = unified.TotalAreaM2,
                        totalAreaHectares = unified.TotalAreaM2 / 10000.0,
                        coverageEfficiency = unified.CoverageEfficiency,
                        averageWindSpeedMps = unified.AverageWindSpeedMps,
                        windSpeedRange = new { min = unified.WindSpeedRange.Min, max = unified.WindSpeedRange.Max },
                        timeRange = new { start = unified.EarliestMeasurement.ToString("O"), end = unified.LatestMeasurement.ToString("O") },
                        sessionCount = unified.SessionIds.Count,
                        vertexCount = unified.VertexCount,
                        latestSequence = latestSeq,
                        unifiedVersion = version,
                        timestamp = DateTimeOffset.UtcNow.ToString("O")
                    },
                    geometry = geoJsonGeometry
                }
            },
            metadata = new { 
                source = "ScentPolygonLibrary", 
                measurementCount, 
                latestSequence = latestSeq, 
                unifiedVersion = version, 
                generatedAt = DateTimeOffset.UtcNow.ToString("O") 
            }
        };

        logger.LogInformation("Combined coverage response generated: area={Area}m², efficiency={Efficiency}%, version={Version}", 
            unified.TotalAreaM2, unified.CoverageEfficiency * 100, version);

        return Results.Json(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error generating unified scent polygon");
        return Results.Problem($"Error generating unified scent polygon: {ex.Message}");
    }
});

app.MapGet("/api/data-status", async (IRoverDataReader roverReader) =>
{
    var status = new
    {
        timestamp = DateTimeOffset.UtcNow.ToString("O"),
        sources = new
        {
            roverData = await CheckRoverDataStatus(roverReader),
            forestData = await CheckForestDataStatus(),
            windPolygons = await CheckWindPolygonStatus(),
            combinedCoverage = await CheckCombinedCoverageStatus()
        }
    };
    
    return Results.Json(status);
});

// Helper functions for status checks
async Task<object> CheckRoverDataStatus(IRoverDataReader roverReader)
{
    try
    {
        await roverReader.InitializeAsync();
        var count = await roverReader.GetMeasurementCountAsync();
        var latest = await roverReader.GetLatestMeasurementAsync();
        
        return new
        {
            available = true,
            measurementCount = count,
            latestSequence = latest?.Sequence ?? -1,
            latestTimestamp = latest?.RecordedAt.ToString("O")
        };
    }
    catch (Exception ex)
    {
        return new { available = false, error = ex.Message };
    }
}

async Task<object> CheckForestDataStatus()
{
    try
    {
        var geoPackagePath = FindGeoPackageFile();
        if (!File.Exists(geoPackagePath))
            return new { available = false, error = "Forest GeoPackage file not found" };
            
        using var geoPackage = await GeoPackage.OpenAsync(geoPackagePath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("riverheadforest", new Dictionary<string, string>(), 4326);
        var count = await layer.CountAsync();
        
        return new { available = true, polygonCount = count, path = geoPackagePath };
    }
    catch (Exception ex)
    {
        return new { available = false, error = ex.Message };
    }
}

async Task<object> CheckWindPolygonStatus()
{
    try
    {
        var windPolygonPath = FindLatestWindPolygonFile();
        if (string.IsNullOrEmpty(windPolygonPath) || !File.Exists(windPolygonPath))
            return new { available = false, error = "Wind polygon file not found", expectedPath = @"C:\temp\Rover1\rover_windpolygon.gpkg" };
            
        using var geoPackage = await GeoPackage.OpenAsync(windPolygonPath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("wind_scent_polygons", new Dictionary<string, string>(), 4326);
        var count = await layer.CountAsync();
        
        return new { available = true, polygonCount = count, path = windPolygonPath };
    }
    catch (Exception ex)
    {
        return new { available = false, error = ex.Message };
    }
}

async Task<object> CheckCombinedCoverageStatus()
{
    try
    {
        var scentService = app.Services.GetRequiredService<ScentPolygonService>();

        var unified = scentService.GetUnifiedScentPolygon();
        if (unified == null)
        {
            return new
            {
                available = false,
                error = "No unified polygon could be generated",
                source = "ScentPolygonLibrary"
            };
        }

        return new
        {
            available = true,
            source = "ScentPolygonLibrary",
            polygonCount = unified.PolygonCount,
            totalAreaM2 = unified.TotalAreaM2,
            coverageEfficiency = unified.CoverageEfficiency,
            timeRange = new
            {
                start = unified.EarliestMeasurement,
                end = unified.LatestMeasurement
            },
            sessionCount = unified.SessionIds.Count
        };
    }
    catch (Exception ex)
    {
        return new { available = false, error = ex.Message, source = "ScentPolygonLibrary" };
    }
}

app.MapGet("/api/unified-status", (ScentPolygonService scentService, HttpContext http) =>
{
    // Ensure no caching for status endpoint
    http.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
    http.Response.Headers["Pragma"] = "no-cache";
    http.Response.Headers["Expires"] = "0";

    // Quick debug info for client polling
    var measurementCount = scentService.Count;
    var latestPolygon = scentService.LatestPolygon;
    var version = scentService.UnifiedVersion;
    var hasValidUnified = scentService.GetUnifiedScentPolygonCached() != null;

    return Results.Json(new {
        polygonCount = measurementCount,
        latestSequence = latestPolygon?.Sequence ?? -1,
        unifiedVersion = version,
        hasValidUnified = hasValidUnified,
        lastPolygonTime = latestPolygon?.RecordedAt.ToString("HH:mm:ss"),
        timestamp = DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff")
    });
});

app.MapPost("/api/unified-recompute", (ScentPolygonService scentService) =>
{
    var unified = scentService.ForceRecomputeUnified();
    return unified == null ? Results.NotFound() : Results.Ok(new { unifiedVersion = scentService.UnifiedVersion });
});

app.MapGet("/api/unified-debug", async (ScentPolygonService scentService, IRoverDataReader roverReader) =>
{
    try
    {
        // Get current service state
        var serviceCount = scentService.Count;
        var latestPolygon = scentService.LatestPolygon;
        var version = scentService.UnifiedVersion;
        
        // Manually check what the data reader sees
        await roverReader.InitializeAsync();
        var allMeasurements = await roverReader.GetAllMeasurementsAsync();
        var readerCount = allMeasurements.Count;
        var readerLatest = allMeasurements.LastOrDefault();
        
        // Check for new measurements manually
        var lastSeq = latestPolygon?.Sequence ?? -1;
        var newMeasurements = await roverReader.GetNewMeasurementsAsync(lastSeq);
        
        return Results.Json(new {
            serviceState = new {
                polygonCount = serviceCount,
                latestSequence = latestPolygon?.Sequence ?? -1,
                latestTime = latestPolygon?.RecordedAt.ToString("HH:mm:ss"),
                unifiedVersion = version
            },
            readerState = new {
                totalMeasurements = readerCount,
                latestSequence = readerLatest?.Sequence ?? -1,
                latestTime = readerLatest?.RecordedAt.ToString("HH:mm:ss"),
                newMeasurementsSinceLastPoll = newMeasurements.Count
            },
            newMeasurements = newMeasurements.Select(m => new {
                sequence = m.Sequence,
                time = m.RecordedAt.ToString("HH:mm:ss"),
                location = $"{m.Latitude:F6},{m.Longitude:F6}"
            }).ToArray()
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Debug error: {ex.Message}");
    }
});

app.MapPost("/api/unified-force-poll", async (ScentPolygonService scentService, IRoverDataReader roverReader) =>
{
    try
    {
        // Force a manual poll to see what happens
        Console.WriteLine("[API] Manual poll requested");
        await roverReader.InitializeAsync();
        var latestPolygon = scentService.LatestPolygon;
        var lastSeq = latestPolygon?.Sequence ?? -1;
        var newMeasurements = await roverReader.GetNewMeasurementsAsync(lastSeq);
        
        Console.WriteLine($"[API] Manual poll found {newMeasurements.Count} new measurements since seq {lastSeq}");
        
        return Results.Json(new {
            lastSequence = lastSeq,
            newMeasurementsFound = newMeasurements.Count,
            measurements = newMeasurements.Select(m => new {
                sequence = m.Sequence,
                time = m.RecordedAt.ToString("HH:mm:ss")
            }).ToArray()
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Force poll error: {ex.Message}");
    }
});

Console.WriteLine("Starting web application...");

try
{
    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Application failed to start: {ex.Message}");
    logger.LogCritical(ex, "Application failed to start");
    throw;
}