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
            DatabaseType = "geopackage",
            GeoPackageFolderPath = @"C:\temp\Rover1\",
            ConnectionTimeoutSeconds = ReaderDefaults.DEFAULT_CONNECTION_TIMEOUT_SECONDS,
            MaxRetryAttempts = ReaderDefaults.DEFAULT_MAX_RETRY_ATTEMPTS,
            RetryDelayMs = ReaderDefaults.DEFAULT_RETRY_DELAY_MS
        };
        
        if (!Directory.Exists(config.GeoPackageFolderPath))
        {
            Directory.CreateDirectory(config.GeoPackageFolderPath);
        }
        
        var reader = RoverDataReaderFactory.CreateReader(config);
        logger.LogInformation("Rover data reader configured for path: {Path}", config.GeoPackageFolderPath);
        return reader;
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to configure rover data reader - using null reader");
        return new NullRoverDataReader();
    }
});

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
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// API Endpoints
app.MapGet("/api/test", () => Results.Json(new { status = "OK", timestamp = DateTime.UtcNow }));

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

app.MapGet("/api/combined-coverage", async () =>
{
    try
    {
        var windPolygonPath = FindLatestWindPolygonFile();
        if (string.IsNullOrEmpty(windPolygonPath) || !File.Exists(windPolygonPath))
        {
            logger.LogWarning("Combined coverage data not available at path: {Path}", windPolygonPath ?? "null");
            return Results.NotFound(new { 
                error = "Combined coverage data not available", 
                expectedPath = @"C:\temp\Rover1\rover_windpolygon.gpkg",
                suggestion = "Run the ConvertWinddataToPolygon tool first to generate the wind polygon data"
            });
        }

        logger.LogInformation("Loading combined coverage from: {Path}", windPolygonPath);

        using var geoPackage = await GeoPackage.OpenAsync(windPolygonPath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("unified_scent_coverage", new Dictionary<string, string>(), 4326);
        var readOptions = new ReadOptions(IncludeGeometry: true, Limit: 1);
        
        await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
        {
            if (feature.Geometry is Polygon polygon && polygon.IsValid)
            {
                var geoJsonWriter = new GeoJsonWriter();
                var geoJsonGeometry = System.Text.Json.JsonSerializer.Deserialize<object>(geoJsonWriter.Write(polygon));
                
                // Parse with InvariantCulture to handle decimal separators correctly
                var totalPolygons = int.Parse(feature.Attributes["total_polygons"] ?? "0", CultureInfo.InvariantCulture);
                var totalAreaM2 = double.Parse(feature.Attributes["total_area_m2"] ?? "0", CultureInfo.InvariantCulture);
                
                var geoJsonFeature = new
                {
                    type = "Feature",
                    properties = new
                    {
                        totalPolygons = totalPolygons,
                        totalAreaM2 = totalAreaM2,
                        totalAreaHectares = totalAreaM2 / 10000.0,
                        createdAt = feature.Attributes["created_at"],
                        description = feature.Attributes["description"],
                        source = "unified_scent_coverage_layer"
                    },
                    geometry = geoJsonGeometry
                };
                
                logger.LogInformation("Combined coverage loaded successfully: {TotalPolygons} polygons, {TotalArea:F1} m²", 
                    geoJsonFeature.properties.totalPolygons, geoJsonFeature.properties.totalAreaM2);
                
                return Results.Json(new { 
                    type = "FeatureCollection", 
                    features = new[] { geoJsonFeature },
                    metadata = new 
                    {
                        sourceFile = Path.GetFileName(windPolygonPath),
                        layerName = "unified_scent_coverage",
                        loadedAt = DateTimeOffset.UtcNow.ToString("O")
                    }
                });
            }
        }
        
        return Results.NotFound(new { 
            error = "No combined coverage polygon found in the layer",
            suggestion = "The unified_scent_coverage layer exists but contains no valid polygons"
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error loading combined coverage");
        return Results.Problem(new ProblemDetails
        {
            Title = "Error loading combined coverage",
            Detail = ex.Message,
            Status = 500
        });
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
        var windPolygonPath = FindLatestWindPolygonFile();
        if (string.IsNullOrEmpty(windPolygonPath) || !File.Exists(windPolygonPath))
            return new { available = false, error = "Wind polygon file not found", expectedPath = @"C:\temp\Rover1\rover_windpolygon.gpkg" };
            
        using var geoPackage = await GeoPackage.OpenAsync(windPolygonPath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("unified_scent_coverage", new Dictionary<string, string>(), 4326);
        var count = await layer.CountAsync();
        
        // Get detailed info if available
        if (count > 0)
        {
            var readOptions = new ReadOptions(IncludeGeometry: false, Limit: 1);
            await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
            {
                return new 
                { 
                    available = true, 
                    polygonCount = count, 
                    path = windPolygonPath,
                    totalPolygons = int.Parse(feature.Attributes["total_polygons"] ?? "0", CultureInfo.InvariantCulture),
                    totalAreaM2 = double.Parse(feature.Attributes["total_area_m2"] ?? "0", CultureInfo.InvariantCulture),
                    createdAt = feature.Attributes["created_at"]
                };
            }
        }
        
        return new { available = count > 0, polygonCount = count, path = windPolygonPath };
    }
    catch (Exception ex)
    {
        return new { available = false, error = ex.Message };
    }
}
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