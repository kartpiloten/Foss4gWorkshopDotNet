using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using ReadRoverDBStub;
using System.Globalization;
using System.Linq;
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
    
    // Create unified coverage polygon layer
    Console.WriteLine("🔄 Creating unified coverage polygon...");
    await CreateUnifiedCoverageLayer(outputGeoPackage, windPolygons.Concat(new[] { sampleFeatureRecord }).ToList());
    
    // Export to GeoJSON as well for broader compatibility
    await GeoJsonExporter.ExportToGeoJsonAsync(outputGeopackagePath, outputGeoJsonPath);
    
    // Run verification and show statistics
    await WindPolygonVerifier.VerifyWindPolygonsAsync(outputGeopackagePath);
    
    Console.WriteLine("\n🎯 Files created:");
    Console.WriteLine($"   📦 GeoPackage: {outputGeopackagePath}");
    Console.WriteLine($"      - Individual scent polygons: wind_scent_polygons layer");
    Console.WriteLine($"      - Unified coverage area: unified_scent_coverage layer");
    Console.WriteLine($"   📄 GeoJSON:    {outputGeoJsonPath}");
    Console.WriteLine("\n📊 Visualization options:");
    Console.WriteLine("1. QGIS: Open the .gpkg file directly (contains both layers)");
    Console.WriteLine("2. Web GIS: Use the .geojson file");
    Console.WriteLine("3. Validate: Check the .geojson at geojsonlint.com");
    Console.WriteLine("\nLayers in GeoPackage:");
    Console.WriteLine("• wind_scent_polygons: Individual scent detection areas for each rover position");
    Console.WriteLine("• unified_scent_coverage: Combined coverage area showing total scent detection zone");
    
    Console.WriteLine("\n🔧 QGIS Instructions:");
    Console.WriteLine("1. Close any existing wind polygon layers in QGIS");
    Console.WriteLine("2. Layer → Add Layer → Add Vector Layer");
    Console.WriteLine($"3. Select: {outputGeopackagePath}");
    Console.WriteLine("4. Choose which layer(s) to load:");
    Console.WriteLine("   - wind_scent_polygons: Shows individual detection areas (detailed view)");
    Console.WriteLine("   - unified_scent_coverage: Shows total coverage area (overview)");
    Console.WriteLine("5. Style with transparency to see overlapping areas");
    Console.WriteLine("6. If still showing as points, use the GeoJSON file instead");
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
/// Creates a scent polygon representing downwind scent detection and omnidirectional detection around the dog.
/// The polygon extends in the downwind direction (where scent would be carried by the wind).
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
    
    // Convert wind direction to radians
    // Wind direction indicates where wind is coming FROM
    // Scent polygons should extend DOWNWIND (where wind is going)
    // So we add 180° to get the downwind direction
    var windRadians = ((windDirection + 180) % 360) * Math.PI / 180.0;
    
    // Dog's position
    var dogLon = measurement.Longitude;
    var dogLat = measurement.Latitude;
    var dogPoint = geometryFactory.CreatePoint(new Coordinate(dogLon, dogLat));
    dogPoint.SRID = 4326;
    
    try 
    {
        // 1. Create the downwind fan polygon (scent detection area)
        var fanCoordinates = new List<Coordinate>();
        fanCoordinates.Add(new Coordinate(dogLon, dogLat));
        
        var fanHalfAngle = CalculateFanAngle(windSpeed);
        var numPoints = 15;
        
        for (int i = 0; i <= numPoints; i++)
        {
            var angle = windRadians - fanHalfAngle + (2 * fanHalfAngle * i / numPoints);
            var angleFromCenter = Math.Abs(angle - windRadians);
            var distanceMultiplier = Math.Cos(angleFromCenter);
            var distance = maxDistance * Math.Max(0.4, distanceMultiplier);
            
            var deltaLat = distance * Math.Cos(angle) / metersPerDegLat;
            var deltaLon = distance * Math.Sin(angle) / metersPerDegLon;
            
            fanCoordinates.Add(new Coordinate(dogLon + deltaLon, dogLat + deltaLat));
        }
        fanCoordinates.Add(new Coordinate(dogLon, dogLat));
        
        var fanRing = geometryFactory.CreateLinearRing(fanCoordinates.ToArray());
        var fanPolygon = geometryFactory.CreatePolygon(fanRing);
        fanPolygon.SRID = 4326;
        
        // 2. Create 30-meter circular buffer around dog (omnidirectional detection)
        const double bufferRadiusMeters = 30.0;
        var bufferRadiusDegrees = bufferRadiusMeters / metersPerDegLat;
        var circularBuffer = dogPoint.Buffer(bufferRadiusDegrees);
        
        // 3. Combine fan and circular buffer using union
        var combinedGeometry = fanPolygon.Union(circularBuffer);
        
        // Handle different result types from union
        Polygon finalPolygon;
        if (combinedGeometry is Polygon singlePolygon)
        {
            finalPolygon = singlePolygon;
        }
        else if (combinedGeometry is MultiPolygon multiPolygon)
        {
            // Take the largest polygon
            finalPolygon = multiPolygon.Geometries.OfType<Polygon>().OrderByDescending(p => p.Area).First();
        }
        else
        {
            // Fallback to circular buffer only
            finalPolygon = circularBuffer as Polygon ?? fanPolygon;
        }
        
        finalPolygon.SRID = 4326;
        
        // Validate and fix if necessary
        if (!finalPolygon.IsValid)
        {
            var buffered = finalPolygon.Buffer(0.0);
            if (buffered is Polygon fixedPolygon && fixedPolygon.IsValid)
            {
                return fixedPolygon;
            }
        }
        
        return finalPolygon;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error creating polygon for sequence {measurement.Sequence}: {ex.Message}");
        
        // Fallback: simple circular buffer
        var fallbackRadius = 40.0 / metersPerDegLat;
        var fallbackBuffer = dogPoint.Buffer(fallbackRadius);
        
        if (fallbackBuffer is Polygon fallbackPolygon)
        {
            fallbackPolygon.SRID = 4326;
            return fallbackPolygon;
        }
        
        // Final fallback
        var finalFallback = geometryFactory.CreatePolygon(geometryFactory.CreateLinearRing(new[]
        {
            new Coordinate(dogLon, dogLat),
            new Coordinate(dogLon + 0.001, dogLat),
            new Coordinate(dogLon + 0.001, dogLat + 0.001),
            new Coordinate(dogLon, dogLat + 0.001),
            new Coordinate(dogLon, dogLat)
        }));
        finalFallback.SRID = 4326;
        return finalFallback;
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

/// <summary>
/// Creates a unified coverage polygon layer that combines all individual scent polygons into one
/// </summary>
static async Task CreateUnifiedCoverageLayer(GeoPackage geoPackage, List<FeatureRecord> allPolygons)
{
    try
    {
        Console.WriteLine($"Processing {allPolygons.Count} polygons for unified coverage...");
        
        // Define schema for unified coverage layer
        var unifiedSchema = new Dictionary<string, string>
        {
            ["total_polygons"] = "INTEGER NOT NULL",
            ["total_area_m2"] = "REAL NOT NULL",
            ["created_at"] = "TEXT NOT NULL",
            ["description"] = "TEXT NOT NULL"
        };
        
        // Create the unified coverage layer
        var unifiedLayer = await geoPackage.EnsureLayerAsync("unified_scent_coverage", unifiedSchema, 4326, "POLYGON");
        
        // Collect all geometries
        var geometries = new List<Geometry>();
        double totalOriginalArea = 0;
        
        foreach (var featureRecord in allPolygons)
        {
            if (featureRecord.Geometry != null && featureRecord.Geometry.IsValid)
            {
                geometries.Add(featureRecord.Geometry);
                
                // Sum up original areas
                if (double.TryParse(featureRecord.Attributes["scent_area_m2"], NumberStyles.Float, CultureInfo.InvariantCulture, out var area))
                {
                    totalOriginalArea += area;
                }
            }
        }
        
        if (geometries.Count == 0)
        {
            Console.WriteLine("⚠️ No valid geometries found for unified coverage");
            return;
        }
        
        Console.WriteLine($"Combining {geometries.Count} valid polygons...");
        
        // Create geometry factory
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        
        // Union all polygons into one
        Geometry unifiedGeometry = geometries.First();
        
        // Progressive union for better performance with many polygons
        var batchSize = 50; // Process in batches to avoid memory issues
        for (int i = 1; i < geometries.Count; i += batchSize)
        {
            var batch = geometries.Skip(i).Take(batchSize).ToList();
            
            foreach (var geometry in batch)
            {
                try
                {
                    unifiedGeometry = unifiedGeometry.Union(geometry);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to union polygon {i}: {ex.Message}");
                    // Continue with other polygons
                }
            }
            
            // Progress update for large datasets
            if (i % 100 == 0)
            {
                Console.WriteLine($"Processed {Math.Min(i + batchSize, geometries.Count)} / {geometries.Count} polygons...");
            }
        }
        
        // Handle MultiPolygon result - combine into single polygon if possible
        Polygon finalPolygon;
        if (unifiedGeometry is Polygon singlePolygon)
        {
            finalPolygon = singlePolygon;
        }
        else if (unifiedGeometry is MultiPolygon multiPolygon)
        {
            Console.WriteLine($"Union resulted in MultiPolygon with {multiPolygon.NumGeometries} parts");
            
            // Take the largest polygon or create a convex hull
            var largestPolygon = multiPolygon.Geometries
                .OfType<Polygon>()
                .OrderByDescending(p => p.Area)
                .FirstOrDefault();
                
            if (largestPolygon != null)
            {
                finalPolygon = largestPolygon;
                Console.WriteLine("Using largest polygon from MultiPolygon result");
            }
            else
            {
                // Create convex hull as fallback
                var convexHull = unifiedGeometry.ConvexHull();
                finalPolygon = convexHull as Polygon ?? geometryFactory.CreatePolygon();
                Console.WriteLine("Using convex hull as fallback");
            }
        }
        else
        {
            // Create convex hull from all points as final fallback
            var convexHull = unifiedGeometry.ConvexHull();
            finalPolygon = convexHull as Polygon ?? geometryFactory.CreatePolygon();
            Console.WriteLine("Using convex hull from union result");
        }
        
        // Smooth the polygon to make it more readable
        Console.WriteLine("Smoothing polygon for better readability...");
        var smoothedPolygon = SmoothPolygon(finalPolygon, geometryFactory);
        smoothedPolygon.SRID = 4326;
        
        // Validate final polygon
        if (!smoothedPolygon.IsValid)
        {
            Console.WriteLine("Smoothed polygon is invalid, attempting to fix...");
            var buffered = smoothedPolygon.Buffer(0.0);
            if (buffered is Polygon fixedPolygon && fixedPolygon.IsValid)
            {
                smoothedPolygon = fixedPolygon;
                smoothedPolygon.SRID = 4326;
            }
        }
        
        // Calculate final area
        var metersPerDegLat = 111320.0;
        var avgLatitude = smoothedPolygon.Centroid.Y;
        var metersPerDegLon = 111320.0 * Math.Cos(avgLatitude * Math.PI / 180.0);
        var unifiedAreaM2 = smoothedPolygon.Area * metersPerDegLat * metersPerDegLon;
        
        // Create feature record for unified coverage
        var unifiedFeature = new FeatureRecord(
            smoothedPolygon,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["total_polygons"] = allPolygons.Count.ToString(CultureInfo.InvariantCulture),
                ["total_area_m2"] = unifiedAreaM2.ToString("F1", CultureInfo.InvariantCulture),
                ["created_at"] = DateTimeOffset.UtcNow.ToString("O"),
                ["description"] = "Unified scent detection coverage area combining all individual rover measurements"
            }
        );
        
        // Insert the unified polygon
        await unifiedLayer.BulkInsertAsync(
            new[] { unifiedFeature },
            new BulkInsertOptions(BatchSize: 1, CreateSpatialIndex: true, ConflictPolicy: ConflictPolicy.Replace),
            null,
            CancellationToken.None
        );
        
        Console.WriteLine("✅ Unified coverage polygon created successfully!");
        Console.WriteLine($"   Combined {allPolygons.Count} individual polygons");
        Console.WriteLine($"   Total coverage area: {unifiedAreaM2:F0} m² ({unifiedAreaM2 / 10000:F1} hectares)");
        Console.WriteLine($"   Vertices in smoothed polygon: {smoothedPolygon.NumPoints}");
        Console.WriteLine($"   Layer name: unified_scent_coverage");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error creating unified coverage layer: {ex.Message}");
        Console.WriteLine("Individual polygons are still available in the main layer.");
    }
}

/// <summary>
/// Smooths a polygon to make it more readable by reducing vertex count and creating smoother edges
/// </summary>
static Polygon SmoothPolygon(Polygon polygon, GeometryFactory geometryFactory)
{
    try
    {
        // Apply Douglas-Peucker simplification with a small tolerance
        var envelope = polygon.EnvelopeInternal;
        var tolerance = Math.Min(envelope.Width, envelope.Height) * 0.01; // 1% of smallest dimension
        
        var simplified = polygon.Buffer(tolerance * 0.5).Buffer(-tolerance * 0.5); // Smooth and simplify
        
        if (simplified is Polygon smoothedPolygon && smoothedPolygon.IsValid)
        {
            return smoothedPolygon;
        }
        
        // Fallback: just buffer slightly to smooth edges
        var buffered = polygon.Buffer(tolerance * 0.1);
        if (buffered is Polygon fallbackPolygon && fallbackPolygon.IsValid)
        {
            return fallbackPolygon;
        }
        
        // Final fallback: return original polygon
        return polygon;
    }
    catch
    {
        // Return original polygon if smoothing fails
        return polygon;
    }
}
