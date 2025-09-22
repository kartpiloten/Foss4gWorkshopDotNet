using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MapPiloteGeopackageHelper;
using NetTopologySuite.IO;
using NetTopologySuite.Geometries;
using System.Collections.Concurrent;

Console.WriteLine("=== Starting FrontendVersion2 Rover Application ===");
Console.WriteLine($"Current Directory: {Directory.GetCurrentDirectory()}");
Console.WriteLine($"Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}");

var builder = WebApplication.CreateBuilder(args);

// Add enhanced logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

Console.WriteLine("Configuring services...");

builder.Services.AddRazorPages();
// Enable detailed circuit errors in Development to surface root causes to the browser
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(o =>
    {
        if (builder.Environment.IsDevelopment())
        {
            o.DetailedErrors = true;
        }
    });

// Add rover data reader as a singleton service
builder.Services.AddSingleton<IRoverDataReader>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<IRoverDataReader>>();
    try
    {
        const string geopackageFolderPath = @"C:\temp\Rover1\";
        var reader = new GeoPackageRoverDataReader(geopackageFolderPath);
        logger.LogInformation("Rover data reader configured for path: {Path}", geopackageFolderPath);
        return reader;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to configure rover data reader");
        throw;
    }
});

Console.WriteLine("Building application...");
var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Application built successfully");

// Global state for tracking rover data updates
var lastKnownSequence = new ConcurrentDictionary<string, int>();

// Helper method to find GeoPackage file
string FindGeoPackageFile()
{
    var possiblePaths = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "..", "Solutionresources", "RiverHeadForest.gpkg"),
        Path.Combine(Directory.GetCurrentDirectory(), "Solutionresources", "RiverHeadForest.gpkg"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Solutionresources", "RiverHeadForest.gpkg"),
        Path.Combine(Environment.CurrentDirectory, "..", "Solutionresources", "RiverHeadForest.gpkg")
    };
    
    foreach (var path in possiblePaths)
    {
        logger.LogInformation("Checking path: {Path}", Path.GetFullPath(path));
        if (File.Exists(path))
        {
            logger.LogInformation("Found GeoPackage at: {Path}", Path.GetFullPath(path));
            return path;
        }
    }
    
    logger.LogWarning("GeoPackage file not found in any of the expected locations");
    return possiblePaths[0]; // Return first path as fallback for error messages
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

// ====== Test endpoint to verify API is working ======
app.MapGet("/api/test", () =>
{
    logger.LogInformation("Test endpoint called");
    return Results.Json(new { status = "OK", timestamp = DateTime.UtcNow });
});

// ====== RiverHead Forest polygon endpoint ======
app.MapGet("/api/riverhead-forest", async () =>
{
    logger.LogInformation("RiverHead Forest endpoint called");
    
    try
    {
        var geoPackagePath = FindGeoPackageFile();
        
        if (!File.Exists(geoPackagePath))
        {
            var errorMsg = $"GeoPackage file not found. Checked path: {Path.GetFullPath(geoPackagePath)}";
            logger.LogWarning(errorMsg);
            return Results.NotFound(errorMsg);
        }

        logger.LogInformation("Opening GeoPackage file at: {Path}", Path.GetFullPath(geoPackagePath));
        
        // Open the GeoPackage and read the forest polygon
        using var geoPackage = await GeoPackage.OpenAsync(geoPackagePath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("riverheadforest", new Dictionary<string, string>(), 4326);

        logger.LogInformation("Reading polygon from layer...");
        
        // Read the polygon (there should be only one)
        var readOptions = new ReadOptions(IncludeGeometry: true, Limit: 1);
        
        await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
        {
            if (feature.Geometry is Polygon polygon)
            {
                logger.LogInformation("Found polygon with {PointCount} points", polygon.NumPoints);
                
                try
                {
                    // Convert to GeoJSON using NetTopologySuite
                    var geoJsonWriter = new GeoJsonWriter();
                    var geoJsonGeometry = geoJsonWriter.Write(polygon);
                    
                    // Create a proper GeoJSON FeatureCollection
                    var featureCollection = new
                    {
                        type = "FeatureCollection",
                        features = new object[]
                        {
                            new
                            {
                                type = "Feature",
                                properties = new { 
                                    name = "RiverHead Forest",
                                    description = "Forest boundary polygon",
                                    pointCount = polygon.NumPoints
                                },
                                geometry = System.Text.Json.JsonSerializer.Deserialize<object>(geoJsonGeometry)
                            }
                        }
                    };
                    
                    logger.LogInformation("Successfully created GeoJSON response");
                    return Results.Json(featureCollection);
                }
                catch (Exception geoJsonEx)
                {
                    logger.LogError(geoJsonEx, "Error converting polygon to GeoJSON");
                    return Results.Problem($"Error converting polygon to GeoJSON: {geoJsonEx.Message}");
                }
            }
        }
        
        logger.LogWarning("No polygon found in the RiverHeadForest layer");
        return Results.NotFound("No polygon found in the RiverHeadForest layer");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error loading forest polygon");
        return Results.Problem($"Error loading forest polygon: {ex.Message}");
    }
});

// ====== Get forest bounds for map centering ======
app.MapGet("/api/forest-bounds", async () =>
{
    logger.LogInformation("Forest bounds endpoint called");
    
    try
    {
        var geoPackagePath = FindGeoPackageFile();
        
        if (!File.Exists(geoPackagePath))
        {
            var errorMsg = $"GeoPackage file not found. Checked path: {Path.GetFullPath(geoPackagePath)}";
            logger.LogWarning(errorMsg);
            return Results.NotFound(errorMsg);
        }

        logger.LogInformation("Opening GeoPackage file for bounds calculation");

        using var geoPackage = await GeoPackage.OpenAsync(geoPackagePath, 4326);
        var layer = await geoPackage.EnsureLayerAsync("riverheadforest", new Dictionary<string, string>(), 4326);

        var readOptions = new ReadOptions(IncludeGeometry: true, Limit: 1);
        
        await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
        {
            if (feature.Geometry is Polygon polygon)
            {
                var envelope = polygon.EnvelopeInternal;
                var centroid = polygon.Centroid;
                
                logger.LogInformation("Forest bounds: ({MinLat}, {MinLng}) to ({MaxLat}, {MaxLng})", 
                    envelope.MinY, envelope.MinX, envelope.MaxY, envelope.MaxX);
                
                var boundsData = new
                {
                    bounds = new
                    {
                        minLat = envelope.MinY,
                        maxLat = envelope.MaxY,
                        minLng = envelope.MinX,
                        maxLng = envelope.MaxX
                    },
                    center = new
                    {
                        lat = centroid.Y,
                        lng = centroid.X
                    }
                };
                
                return Results.Json(boundsData);
            }
        }
        
        logger.LogWarning("No polygon found for bounds calculation");
        return Results.NotFound("No polygon found");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error getting forest bounds");
        return Results.Problem($"Error getting forest bounds: {ex.Message}");
    }
});

// ====== Get all rover measurements ======
app.MapGet("/api/rover-data", async (IRoverDataReader roverReader) =>
{
    logger.LogInformation("Rover data endpoint called");
    
    try
    {
        await roverReader.InitializeAsync();
        var measurements = await roverReader.GetAllMeasurementsAsync();
        
        logger.LogInformation("Retrieved {Count} rover measurements", measurements.Count);
        
        // Convert to GeoJSON format
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
        
        var geoJsonCollection = new
        {
            type = "FeatureCollection",
            features = features
        };
        
        return Results.Json(geoJsonCollection);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error retrieving rover data");
        return Results.Problem($"Error retrieving rover data: {ex.Message}");
    }
});

// ====== Get new rover measurements since last check ======
app.MapGet("/api/rover-data/updates", async (IRoverDataReader roverReader, string? clientId = null) =>
{
    logger.LogInformation("Rover data updates endpoint called for client: {ClientId}", clientId ?? "unknown");
    
    try
    {
        await roverReader.InitializeAsync();
        
        clientId ??= "default";
        var lastSequence = lastKnownSequence.GetValueOrDefault(clientId, -1);
        
        var newMeasurements = await roverReader.GetNewMeasurementsAsync(lastSequence);
        
        if (newMeasurements.Any())
        {
            // Update the last known sequence for this client
            var maxSequence = newMeasurements.Max(m => m.Sequence);
            lastKnownSequence.AddOrUpdate(clientId, maxSequence, (key, oldValue) => Math.Max(oldValue, maxSequence));
            
            logger.LogInformation("Found {Count} new measurements for client {ClientId}, sequences {MinSeq}-{MaxSeq}", 
                newMeasurements.Count, clientId, newMeasurements.Min(m => m.Sequence), maxSequence);
        }
        
        // Convert to GeoJSON format
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
        
        var geoJsonCollection = new
        {
            type = "FeatureCollection",
            features = features,
            metadata = new
            {
                count = features.Length,
                lastSequence = newMeasurements.Any() ? newMeasurements.Max(m => m.Sequence) : lastSequence
            }
        };
        
        return Results.Json(geoJsonCollection);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error retrieving rover data updates");
        return Results.Problem($"Error retrieving rover data updates: {ex.Message}");
    }
});

// ====== Get rover data statistics ======
app.MapGet("/api/rover-data/stats", async (IRoverDataReader roverReader) =>
{
    logger.LogInformation("Rover data stats endpoint called");
    
    try
    {
        await roverReader.InitializeAsync();
        
        var totalCount = await roverReader.GetMeasurementCountAsync();
        var latestMeasurement = await roverReader.GetLatestMeasurementAsync();
        
        var stats = new
        {
            totalMeasurements = totalCount,
            latestSequence = latestMeasurement?.Sequence ?? -1,
            latestTimestamp = latestMeasurement?.RecordedAt.ToString("O"),
            latestPosition = latestMeasurement != null ? new
            {
                latitude = latestMeasurement.Latitude,
                longitude = latestMeasurement.Longitude
            } : null
        };
        
        logger.LogInformation("Rover stats: {TotalCount} measurements, latest sequence: {LatestSeq}", 
            totalCount, latestMeasurement?.Sequence ?? -1);
        
        return Results.Json(stats);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error retrieving rover data stats");
        return Results.Problem($"Error retrieving rover data stats: {ex.Message}");
    }
});

Console.WriteLine("Starting web application...");
logger.LogInformation("Starting application on {Environment}", app.Environment.EnvironmentName);

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

// ====== Rover Data Reader Classes (copied from ReadRoverDBStub) ======

/// <summary>
/// Rover measurement data record - shared with RoverSimulator
/// </summary>
public record RoverMeasurement(
    Guid SessionId,
    int Sequence,
    DateTimeOffset RecordedAt,
    double Latitude,
    double Longitude,
    short WindDirectionDeg,
    float WindSpeedMps,
    Point Geometry);

/// <summary>
/// Interface for rover data readers - read-only access to rover measurement data
/// </summary>
public interface IRoverDataReader : IDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default);
    Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default);
    Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default);
    Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for rover data readers
/// </summary>
public abstract class RoverDataReaderBase : IRoverDataReader
{
    protected readonly string _connectionString;
    protected bool _disposed;

    protected RoverDataReaderBase(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public abstract Task InitializeAsync(CancellationToken cancellationToken = default);
    public abstract Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default);
    public abstract Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default);
    public abstract Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default);
    public abstract Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default);

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public class GeoPackageRoverDataReader : RoverDataReaderBase
{
    private GeoPackage? _geoPackage;
    private GeoPackageLayer? _measurementsLayer;
    private string? _dbPath;
    private string? _folderPath;
    private const string LayerName = "rover_measurements";
    private const string FixedFileName = "rover_data.gpkg";

    public GeoPackageRoverDataReader(string connectionString) : base(connectionString)
    {
        // Treat connection string as folder path or direct file path
        if (connectionString.EndsWith(".gpkg", StringComparison.OrdinalIgnoreCase))
        {
            _dbPath = connectionString;
            _folderPath = Path.GetDirectoryName(connectionString);
        }
        else
        {
            _folderPath = connectionString?.TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(_folderPath))
            {
                _folderPath = Environment.CurrentDirectory;
            }
            _dbPath = Path.Combine(_folderPath, FixedFileName);
        }
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_dbPath))
            throw new InvalidOperationException("Database path not specified");

        try
        {
            if (!File.Exists(_dbPath))
            {
                throw new FileNotFoundException($"GeoPackage file not found: {_dbPath}");
            }

            // Open the GeoPackage in read-only mode
            _geoPackage = await GeoPackage.OpenAsync(_dbPath, 4326);

            // Get the rover_measurements layer (schema is empty since we're just reading)
            _measurementsLayer = await _geoPackage.EnsureLayerAsync(LayerName, new Dictionary<string, string>(), 4326);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error opening GeoPackage: {ex.Message}", ex);
        }
    }

    public override async Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Reader not initialized. Call InitializeAsync first.");

        return await _measurementsLayer.CountAsync();
    }

    public override async Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Reader not initialized. Call InitializeAsync first.");

        var readOptions = new ReadOptions(
            IncludeGeometry: true,
            WhereClause: whereClause,
            OrderBy: "sequence ASC"
        );

        var measurements = new List<RoverMeasurement>();

        await foreach (var feature in _measurementsLayer.ReadFeaturesAsync(readOptions, cancellationToken))
        {
            measurements.Add(ConvertToRoverMeasurement(feature));
        }

        return measurements;
    }

    public override async Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Reader not initialized. Call InitializeAsync first.");

        var readOptions = new ReadOptions(
            IncludeGeometry: true,
            WhereClause: $"sequence > {lastSequence}",
            OrderBy: "sequence ASC"
        );

        var measurements = new List<RoverMeasurement>();

        await foreach (var feature in _measurementsLayer.ReadFeaturesAsync(readOptions, cancellationToken))
        {
            measurements.Add(ConvertToRoverMeasurement(feature));
        }

        return measurements;
    }

    public override async Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default)
    {
        if (_measurementsLayer == null)
            throw new InvalidOperationException("Reader not initialized. Call InitializeAsync first.");

        var readOptions = new ReadOptions(
            IncludeGeometry: true,
            OrderBy: "sequence DESC",
            Limit: 1
        );

        await foreach (var feature in _measurementsLayer.ReadFeaturesAsync(readOptions, cancellationToken))
        {
            return ConvertToRoverMeasurement(feature);
        }

        return null;
    }

    private static RoverMeasurement ConvertToRoverMeasurement(FeatureRecord feature)
    {
        var sessionId = Guid.Parse(feature.Attributes["session_id"] ?? throw new InvalidDataException("Missing session_id"));
        var sequence = int.Parse(feature.Attributes["sequence"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var recordedAt = DateTimeOffset.Parse(feature.Attributes["recorded_at"] ?? throw new InvalidDataException("Missing recorded_at"));
        var latitude = double.Parse(feature.Attributes["latitude"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var longitude = double.Parse(feature.Attributes["longitude"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var windDirection = short.Parse(feature.Attributes["wind_direction_deg"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var windSpeed = float.Parse(feature.Attributes["wind_speed_mps"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);

        return new RoverMeasurement(
            sessionId,
            sequence,
            recordedAt,
            latitude,
            longitude,
            windDirection,
            windSpeed,
            (Point)feature.Geometry
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _measurementsLayer = null;
                _geoPackage?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
