using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using ReadRoverDBStub;
using System.Globalization;
using ConvertWinddataToPolygon;

Console.WriteLine("=== Wind Data to Polygon Converter ===");
Console.WriteLine("Converting rover wind measurements to scent area polygons...");

const string inputGeopackagePath = @"C:\temp\Rover1\rover_data.gpkg";
var outputGeopackagePath = @"C:\temp\Rover1\rover_windpolygon.gpkg";
var outputGeoJsonPath = @"C:\temp\Rover1\rover_windpolygon.geojson";

try
{
    // Verify input file exists
    if (!File.Exists(inputGeopackagePath))
    {
        Console.WriteLine($"❌ Input GeoPackage not found: {inputGeopackagePath}");
        Console.WriteLine("Please run the RoverSimulator first to generate rover data.");
        return;
    }

    Console.WriteLine($"📖 Reading rover data from: {inputGeopackagePath}");
    
    // Initialize rover data reader
    using var roverReader = new GeoPackageRoverDataReader(inputGeopackagePath);
    await roverReader.InitializeAsync();
    
    // Get all rover measurements
    var measurements = await roverReader.GetAllMeasurementsAsync();
    Console.WriteLine($"Found {measurements.Count} rover measurements");
    
    if (measurements.Count == 0)
    {
        Console.WriteLine("No measurements found. Exiting.");
        return;
    }

    // Handle file locking - try alternative filename if original is locked
    var attempts = 0;
    var maxAttempts = 3;
    
    while (attempts < maxAttempts)
    {
        try
        {
            // Try to delete existing files
            if (File.Exists(outputGeopackagePath))
            {
                File.Delete(outputGeopackagePath);
                Console.WriteLine("Deleted existing GeoPackage file");
            }
            
            if (File.Exists(outputGeoJsonPath))
            {
                File.Delete(outputGeoJsonPath);
                Console.WriteLine("Deleted existing GeoJSON file");
            }
            break; // Success, exit the loop
        }
        catch (IOException ex) when (ex.Message.Contains("being used by another process"))
        {
            attempts++;
            if (attempts < maxAttempts)
            {
                // Try alternative filename
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                outputGeopackagePath = $@"C:\temp\Rover1\rover_windpolygon_{timestamp}.gpkg";
                outputGeoJsonPath = $@"C:\temp\Rover1\rover_windpolygon_{timestamp}.geojson";
                Console.WriteLine($"File locked, trying alternative: {Path.GetFileName(outputGeopackagePath)}");
            }
            else
            {
                Console.WriteLine("❌ Output files are locked by another process (likely QGIS).");
                Console.WriteLine("Please close QGIS or any other application using the wind polygon files and try again.");
                throw;
            }
        }
    }

    Console.WriteLine($"📝 Creating wind polygon GeoPackage: {outputGeopackagePath}");

    // Create a sample polygon first to establish the correct geometry type
    var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
    var sampleMeasurement = measurements.First();
    var samplePolygon = CreateScentPolygon(sampleMeasurement, geometryFactory);
    
    Console.WriteLine($"Sample polygon geometry type: {samplePolygon.GeometryType}");
    Console.WriteLine($"Sample polygon SRID: {samplePolygon.SRID}");
    Console.WriteLine($"Sample polygon is valid: {samplePolygon.IsValid}");

    using var outputGeoPackage = await GeoPackage.OpenAsync(outputGeopackagePath, 4326);
    
    // Define schema for wind polygons
    var windPolygonSchema = new Dictionary<string, string>
    {
        ["session_id"] = "TEXT NOT NULL",
        ["sequence"] = "INTEGER NOT NULL", 
        ["recorded_at"] = "TEXT NOT NULL",
        ["wind_direction_deg"] = "INTEGER NOT NULL",
        ["wind_speed_mps"] = "REAL NOT NULL",
        ["scent_area_m2"] = "REAL NOT NULL",
        ["max_distance_m"] = "REAL NOT NULL"
    };

    // Create the layer with proper geometry type specification
    Console.WriteLine("Creating polygon layer with proper geometry type...");
    var windPolygonLayer = await outputGeoPackage.EnsureLayerAsync("wind_scent_polygons", windPolygonSchema, 4326, "POLYGON");
    
    // Insert the sample polygon first to establish geometry type
    var sampleFeatureRecord = new FeatureRecord(
        samplePolygon,
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["session_id"] = sampleMeasurement.SessionId.ToString(),
            ["sequence"] = sampleMeasurement.Sequence.ToString(CultureInfo.InvariantCulture),
            ["recorded_at"] = sampleMeasurement.RecordedAt.ToString("O"),
            ["wind_direction_deg"] = sampleMeasurement.WindDirectionDeg.ToString(CultureInfo.InvariantCulture),
            ["wind_speed_mps"] = sampleMeasurement.WindSpeedMps.ToString("F2", CultureInfo.InvariantCulture),
            ["scent_area_m2"] = (samplePolygon.Area * 111320.0 * 111320.0 * Math.Cos(sampleMeasurement.Latitude * Math.PI / 180.0)).ToString("F1", CultureInfo.InvariantCulture),
            ["max_distance_m"] = CalculateMaxScentDistance(sampleMeasurement.WindSpeedMps).ToString("F1", CultureInfo.InvariantCulture)
        }
    );
    
    // Insert sample to establish geometry type
    await windPolygonLayer.BulkInsertAsync(
        new[] { sampleFeatureRecord },
        new BulkInsertOptions(BatchSize: 1, CreateSpatialIndex: false, ConflictPolicy: ConflictPolicy.Ignore),
        null,
        CancellationToken.None
    );
    
    Console.WriteLine("Sample polygon inserted to establish geometry type");
    
    // Generate wind polygons for remaining measurements (skip the first one as it's already inserted)
    var windPolygons = new List<FeatureRecord>();
    
    foreach (var measurement in measurements.Skip(1)) // Skip first as it's already inserted
    {
        var scentPolygon = CreateScentPolygon(measurement, geometryFactory);
        
        // Validate the polygon before adding
        if (!scentPolygon.IsValid)
        {
            Console.WriteLine($"Warning: Invalid polygon for sequence {measurement.Sequence}, attempting to fix...");
            
            // Try to fix the polygon
            var buffered = scentPolygon.Buffer(0.0);
            if (buffered is Polygon fixedPolygon && fixedPolygon.IsValid)
            {
                fixedPolygon.SRID = 4326;
                scentPolygon = fixedPolygon;
            }
            else
            {
                Console.WriteLine($"Could not fix polygon for sequence {measurement.Sequence}, skipping...");
                continue;
            }
        }
        
        var scentArea = scentPolygon.Area * 111320.0 * 111320.0 * Math.Cos(measurement.Latitude * Math.PI / 180.0); // Approximate area in m²
        var maxDistance = CalculateMaxScentDistance(measurement.WindSpeedMps);
        
        var featureRecord = new FeatureRecord(
            scentPolygon,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["session_id"] = measurement.SessionId.ToString(),
                ["sequence"] = measurement.Sequence.ToString(CultureInfo.InvariantCulture),
                ["recorded_at"] = measurement.RecordedAt.ToString("O"),
                ["wind_direction_deg"] = measurement.WindDirectionDeg.ToString(CultureInfo.InvariantCulture),
                ["wind_speed_mps"] = measurement.WindSpeedMps.ToString("F2", CultureInfo.InvariantCulture),
                ["scent_area_m2"] = scentArea.ToString("F1", CultureInfo.InvariantCulture),
                ["max_distance_m"] = maxDistance.ToString("F1", CultureInfo.InvariantCulture)
            }
        );
        
        windPolygons.Add(featureRecord);
    }

    Console.WriteLine($"Generated {windPolygons.Count + 1} valid scent polygons (including sample)");
    
    // Bulk insert remaining polygons with smaller batch size for better compatibility
    if (windPolygons.Any())
    {
        await windPolygonLayer.BulkInsertAsync(
            windPolygons,
            new BulkInsertOptions(
                BatchSize: 100, // Smaller batch size for better stability
                CreateSpatialIndex: true,
                ConflictPolicy: ConflictPolicy.Ignore
            ),
            progress: null,
            CancellationToken.None
        );
    }

    // Verify the layer geometry type after insertion
    Console.WriteLine("Verifying layer geometry type after insertion...");
    var testReadOptions = new ReadOptions(IncludeGeometry: true, Limit: 3);
    int testCount = 0;
    await foreach (var testFeature in windPolygonLayer.ReadFeaturesAsync(testReadOptions))
    {
        Console.WriteLine($"Feature {++testCount}: {testFeature.Geometry?.GeometryType} (SRID: {testFeature.Geometry?.SRID}, Valid: {testFeature.Geometry?.IsValid})");
    }

    Console.WriteLine("✅ Wind polygon conversion completed successfully!");
    Console.WriteLine($"GeoPackage output: {outputGeopackagePath}");
    
    var gpkgFileInfo = new FileInfo(outputGeopackagePath);
    Console.WriteLine($"GeoPackage size: {gpkgFileInfo.Length / 1024.0:F1} KB");
    
    // Export to GeoJSON as well for broader compatibility
    await GeoJsonExporter.ExportToGeoJsonAsync(outputGeopackagePath, outputGeoJsonPath);
    
    // Run verification and show statistics
    await WindPolygonVerifier.VerifyWindPolygonsAsync(outputGeopackagePath);
    
    Console.WriteLine("\n🎯 Files created:");
    Console.WriteLine($"   📦 GeoPackage: {outputGeopackagePath}");
    Console.WriteLine($"   📄 GeoJSON:    {outputGeoJsonPath}");
    Console.WriteLine("\n📊 Visualization options:");
    Console.WriteLine("1. QGIS: Open the .gpkg file directly");
    Console.WriteLine("2. Web GIS: Use the .geojson file");
    Console.WriteLine("3. Validate: Check the .geojson at geojsonlint.com");
    Console.WriteLine("\nEach polygon represents the upwind scent detection area for a dog at that rover position.");
    
    Console.WriteLine("\n🔧 QGIS Instructions:");
    Console.WriteLine("1. Close any existing wind polygon layers in QGIS");
    Console.WriteLine("2. Layer → Add Layer → Add Vector Layer");
    Console.WriteLine($"3. Select: {outputGeopackagePath}");
    Console.WriteLine("4. Check that layer properties show 'Polygon' geometry type");
    Console.WriteLine("5. If still showing as points, use the GeoJSON file instead");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    if (ex.StackTrace != null)
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
    Console.WriteLine("Please ensure the rover_data.gpkg file exists and is not locked by another application.");
    Environment.Exit(1);
}

/// <summary>
/// Creates a scent polygon representing the upwind area where a dog could detect human scent
/// </summary>
static Polygon CreateScentPolygon(RoverMeasurement measurement, GeometryFactory geometryFactory)
{
    // Parameters for scent detection model
    var windSpeed = measurement.WindSpeedMps;
    var windDirection = measurement.WindDirectionDeg;
    
    // Calculate maximum scent distance based on wind speed
    var maxDistance = CalculateMaxScentDistance(windSpeed);
    
    // Convert to meters (approximate for the latitude)
    var metersPerDegLat = 111320.0;
    var metersPerDegLon = 111320.0 * Math.Cos(measurement.Latitude * Math.PI / 180.0);
    
    // Convert wind direction to radians (0° = North, clockwise)
    var windRadians = windDirection * Math.PI / 180.0;
    
    // Create a fan-shaped polygon representing scent dispersion
    var coordinates = new List<Coordinate>();
    
    // Dog's position (apex of the fan) - this will be the first and last point
    var dogLon = measurement.Longitude;
    var dogLat = measurement.Latitude;
    
    // Start at dog's position
    coordinates.Add(new Coordinate(dogLon, dogLat));
    
    // Create the fan shape - wider at max distance
    var fanHalfAngle = CalculateFanAngle(windSpeed); // Half-angle of the scent detection cone
    var numPoints = 15; // Number of points to create smooth arc (reduced for simpler geometry)
    
    // Generate points for the upwind arc (left side to right side)
    for (int i = 0; i <= numPoints; i++)
    {
        var angle = windRadians - fanHalfAngle + (2 * fanHalfAngle * i / numPoints);
        
        // Distance varies based on position in fan (max at center, reduced at edges)
        var angleFromCenter = Math.Abs(angle - windRadians);
        var distanceMultiplier = Math.Cos(angleFromCenter); // Reduces distance at edges
        var distance = maxDistance * Math.Max(0.4, distanceMultiplier); // Minimum 40% distance at edges
        
        // Convert distance to degrees
        var deltaLat = distance * Math.Cos(angle) / metersPerDegLat;
        var deltaLon = distance * Math.Sin(angle) / metersPerDegLon;
        
        coordinates.Add(new Coordinate(dogLon + deltaLon, dogLat + deltaLat));
    }
    
    // Explicitly close the polygon by returning to the starting point
    coordinates.Add(new Coordinate(dogLon, dogLat));
    
    // Ensure we have enough points for a valid polygon
    if (coordinates.Count < 4)
    {
        // Fallback: create a simple triangle
        var fallbackDistance = maxDistance * 0.8;
        coordinates.Clear();
        coordinates.Add(new Coordinate(dogLon, dogLat)); // Dog position
        
        var leftAngle = windRadians - Math.PI / 6; // 30 degrees left
        var rightAngle = windRadians + Math.PI / 6; // 30 degrees right
        
        coordinates.Add(new Coordinate(
            dogLon + fallbackDistance * Math.Sin(leftAngle) / metersPerDegLon,
            dogLat + fallbackDistance * Math.Cos(leftAngle) / metersPerDegLat));
            
        coordinates.Add(new Coordinate(
            dogLon + fallbackDistance * Math.Sin(rightAngle) / metersPerDegLon,
            dogLat + fallbackDistance * Math.Cos(rightAngle) / metersPerDegLat));
            
        coordinates.Add(new Coordinate(dogLon, dogLat)); // Close the triangle
    }
    
    try
    {
        // Create the linear ring and polygon using the geometry factory
        var linearRing = geometryFactory.CreateLinearRing(coordinates.ToArray());
        var polygon = geometryFactory.CreatePolygon(linearRing);
        
        // Ensure SRID is set correctly
        polygon.SRID = 4326;
        
        // Validate the polygon
        if (!polygon.IsValid)
        {
            // Try to fix common issues
            var buffered = polygon.Buffer(0.0); // Buffer with 0 distance can fix self-intersections
            if (buffered is Polygon fixedPolygon && fixedPolygon.IsValid)
            {
                fixedPolygon.SRID = 4326;
                return fixedPolygon;
            }
            
            // Last resort: create a simple circular buffer around the dog position
            var dogPoint = geometryFactory.CreatePoint(new Coordinate(dogLon, dogLat));
            dogPoint.SRID = 4326;
            var circularBuffer = dogPoint.Buffer(maxDistance / metersPerDegLat * 0.5); // Half the max distance as radius
            
            if (circularBuffer is Polygon circularPolygon)
            {
                circularPolygon.SRID = 4326;
                return circularPolygon;
            }
        }
        
        return polygon;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error creating polygon for sequence {measurement.Sequence}: {ex.Message}");
        
        // Fallback: create a simple point buffer
        var dogPoint = geometryFactory.CreatePoint(new Coordinate(dogLon, dogLat));
        dogPoint.SRID = 4326;
        var buffer = dogPoint.Buffer(maxDistance / metersPerDegLat * 0.3); // Small circular buffer
        
        if (buffer is Polygon bufferPolygon)
        {
            bufferPolygon.SRID = 4326;
            return bufferPolygon;
        }
        
        // Final fallback: simple square
        var fallbackPolygon = geometryFactory.CreatePolygon(geometryFactory.CreateLinearRing(new[]
        {
            new Coordinate(dogLon, dogLat),
            new Coordinate(dogLon + 0.001, dogLat),
            new Coordinate(dogLon + 0.001, dogLat + 0.001),
            new Coordinate(dogLon, dogLat + 0.001),
            new Coordinate(dogLon, dogLat)
        }));
        fallbackPolygon.SRID = 4326;
        return fallbackPolygon;
    }
}

/// <summary>
/// Calculates maximum scent detection distance based on wind speed
/// </summary>
static double CalculateMaxScentDistance(double windSpeedMps)
{
    // Scent detection model based on wind speed
    // Higher wind speed = better scent transport, but also more dilution
    
    if (windSpeedMps < 0.5) // Very light wind
        return 30.0; // Limited scent transport
    else if (windSpeedMps < 2.0) // Light wind  
        return 50.0 + (windSpeedMps - 0.5) * 20.0; // Good scent transport
    else if (windSpeedMps < 5.0) // Moderate wind
        return 80.0 + (windSpeedMps - 2.0) * 15.0; // Optimal conditions
    else if (windSpeedMps < 8.0) // Strong wind
        return 125.0 + (windSpeedMps - 5.0) * 10.0; // Some dilution starts
    else // Very strong wind
        return Math.Max(30.0, 155.0 - (windSpeedMps - 8.0) * 5.0); // Significant dilution
}

/// <summary>
/// Calculates the fan angle (half-angle) for scent detection based on wind speed
/// </summary>
static double CalculateFanAngle(double windSpeedMps)
{
    // Fan angle in radians - higher wind speed = narrower, more focused scent cone
    
    if (windSpeedMps < 1.0) // Light wind - wide dispersion
        return 30.0 * Math.PI / 180.0; // ±30 degrees (reduced from 45)
    else if (windSpeedMps < 3.0) // Moderate wind  
        return (30.0 - (windSpeedMps - 1.0) * 7.5) * Math.PI / 180.0; // ±15-30 degrees
    else if (windSpeedMps < 6.0) // Strong wind - more focused
        return (15.0 - (windSpeedMps - 3.0) * 2.0) * Math.PI / 180.0; // ±9-15 degrees  
    else // Very strong wind - very narrow cone
        return Math.Max(5.0, 9.0 - (windSpeedMps - 6.0) * 0.5) * Math.PI / 180.0; // ±5-9 degrees
}
