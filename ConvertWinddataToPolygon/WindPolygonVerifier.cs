using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using System.Globalization;

namespace ConvertWinddataToPolygon;

/// <summary>
/// Utility class to verify and display statistics about generated wind polygons
/// </summary>
public static class WindPolygonVerifier
{
    public static async Task VerifyWindPolygonsAsync(string geoPackagePath)
    {
        Console.WriteLine("\n=== Wind Polygon Verification ===");
        
        if (!File.Exists(geoPackagePath))
        {
            Console.WriteLine($"Wind polygon file not found: {geoPackagePath}");
            return;
        }

        try
        {
            using var geoPackage = await GeoPackage.OpenAsync(geoPackagePath, 4326);
            
            // Don't use EnsureLayerAsync with empty schema - this would override the geometry type!
            // Instead, get the existing layer without modifying its schema
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
            
            // Use the correct schema to preserve the existing layer geometry type
            var layer = await geoPackage.EnsureLayerAsync("wind_scent_polygons", windPolygonSchema, 4326);
            
            var totalCount = await layer.CountAsync();
            Console.WriteLine($"Total wind polygons: {totalCount}");
            
            if (totalCount == 0)
            {
                Console.WriteLine("No wind polygons found.");
                return;
            }

            // Check layer metadata and geometry type
            Console.WriteLine("\nLayer Information:");
            Console.WriteLine($"  Layer name: wind_scent_polygons");
            Console.WriteLine($"  Expected SRID: 4326");
            
            // Read first few features to analyze geometry types
            var firstFeaturesOptions = new ReadOptions(IncludeGeometry: true, Limit: 5);
            var geometryTypes = new HashSet<string>();
            var sridValues = new HashSet<int>();
            
            await foreach (var feature in layer.ReadFeaturesAsync(firstFeaturesOptions))
            {
                if (feature.Geometry != null)
                {
                    geometryTypes.Add(feature.Geometry.GeometryType);
                    sridValues.Add(feature.Geometry.SRID);
                }
            }
            
            Console.WriteLine($"  Geometry types found: {string.Join(", ", geometryTypes)}");
            Console.WriteLine($"  SRID values found: {string.Join(", ", sridValues)}");
            
            if (geometryTypes.Contains("Point"))
            {
                Console.WriteLine("  ??  WARNING: Point geometries detected! This explains why QGIS shows points.");
                Console.WriteLine("     The layer should contain only Polygon geometries.");
            }
            
            if (!geometryTypes.Contains("Polygon"))
            {
                Console.WriteLine("  ? ERROR: No Polygon geometries found! This is the root cause of the QGIS issue.");
                Console.WriteLine("     The layer creation process may have failed to preserve polygon geometry type.");
            }
            else
            {
                Console.WriteLine("  ? Polygon geometries detected - layer should work correctly in QGIS.");
            }

            // Statistics tracking
            var windSpeeds = new List<double>();
            var scentAreas = new List<double>();
            var maxDistances = new List<double>();
            var windDirections = new List<int>();
            var geometryStats = new Dictionary<string, int>
            {
                ["Valid"] = 0,
                ["Invalid"] = 0,
                ["Polygon"] = 0,
                ["MultiPolygon"] = 0,
                ["Point"] = 0,
                ["Other"] = 0
            };
            
            var readOptions = new ReadOptions(IncludeGeometry: true);
            
            await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
            {
                // Collect attribute statistics
                if (double.TryParse(feature.Attributes["wind_speed_mps"], CultureInfo.InvariantCulture, out var windSpeed))
                    windSpeeds.Add(windSpeed);
                
                if (double.TryParse(feature.Attributes["scent_area_m2"], CultureInfo.InvariantCulture, out var scentArea))
                    scentAreas.Add(scentArea);
                
                if (double.TryParse(feature.Attributes["max_distance_m"], CultureInfo.InvariantCulture, out var maxDistance))
                    maxDistances.Add(maxDistance);
                
                if (int.TryParse(feature.Attributes["wind_direction_deg"], CultureInfo.InvariantCulture, out var windDir))
                    windDirections.Add(windDir);
                
                // Check geometry validity and type
                if (feature.Geometry != null)
                {
                    geometryStats[feature.Geometry.IsValid ? "Valid" : "Invalid"]++;
                    
                    switch (feature.Geometry)
                    {
                        case Polygon:
                            geometryStats["Polygon"]++;
                            break;
                        case MultiPolygon:
                            geometryStats["MultiPolygon"]++;
                            break;
                        case Point:
                            geometryStats["Point"]++;
                            break;
                        default:
                            geometryStats["Other"]++;
                            break;
                    }
                }
            }

            // Display geometry statistics
            Console.WriteLine("\nGeometry Statistics:");
            Console.WriteLine($"  Valid geometries: {geometryStats["Valid"]}");
            Console.WriteLine($"  Invalid geometries: {geometryStats["Invalid"]}");
            Console.WriteLine($"  Polygon type: {geometryStats["Polygon"]}");
            Console.WriteLine($"  MultiPolygon type: {geometryStats["MultiPolygon"]}");
            Console.WriteLine($"  Point type: {geometryStats["Point"]} ??");
            Console.WriteLine($"  Other geometry types: {geometryStats["Other"]}");
            
            if (geometryStats["Point"] > 0)
            {
                Console.WriteLine($"\n? QGIS ISSUE IDENTIFIED: {geometryStats["Point"]} Point geometries found!");
                Console.WriteLine("   This is why QGIS displays the layer as points instead of polygons.");
                Console.WriteLine("   The polygon creation process needs to be fixed.");
            }
            else if (geometryStats["Invalid"] > 0)
            {
                Console.WriteLine($"??  WARNING: {geometryStats["Invalid"]} invalid geometries detected!");
                Console.WriteLine("   This may cause issues in QGIS. Consider regenerating with fixed geometry.");
            }
            else if (geometryStats["Polygon"] == totalCount)
            {
                Console.WriteLine("? All geometries are valid polygons and should work perfectly in QGIS!");
            }

            // Display attribute statistics
            if (windSpeeds.Any())
            {
                Console.WriteLine("\nWind Speed Statistics (m/s):");
                Console.WriteLine($"  Min: {windSpeeds.Min():F1}, Max: {windSpeeds.Max():F1}, Avg: {windSpeeds.Average():F1}");
            }
            
            if (scentAreas.Any())
            {
                Console.WriteLine("\nScent Area Statistics (m²):");
                Console.WriteLine($"  Min: {scentAreas.Min():F1}, Max: {scentAreas.Max():F1}, Avg: {scentAreas.Average():F1}");
            }
            
            if (maxDistances.Any())
            {
                Console.WriteLine("\nMax Detection Distance Statistics (m):");
                Console.WriteLine($"  Min: {maxDistances.Min():F1}, Max: {maxDistances.Max():F1}, Avg: {maxDistances.Average():F1}");
            }

            // Wind direction distribution
            if (windDirections.Any())
            {
                Console.WriteLine("\nWind Direction Distribution:");
                var directionBins = new int[8]; // N, NE, E, SE, S, SW, W, NW
                var directionNames = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
                
                foreach (var dir in windDirections)
                {
                    var bin = ((dir + 22) / 45) % 8; // Convert degrees to 8-direction bins
                    directionBins[bin]++;
                }
                
                for (int i = 0; i < 8; i++)
                {
                    var percentage = (directionBins[i] * 100.0) / windDirections.Count;
                    Console.WriteLine($"  {directionNames[i]}: {directionBins[i]} ({percentage:F1}%)");
                }
            }

            // Sample polygons with detailed geometry info
            Console.WriteLine("\nSample Wind Polygons:");
            var sampleReadOptions = new ReadOptions(IncludeGeometry: true, OrderBy: "sequence ASC", Limit: 5);
            
            int count = 0;
            await foreach (var feature in layer.ReadFeaturesAsync(sampleReadOptions))
            {
                var sequence = feature.Attributes["sequence"];
                var windSpeed = feature.Attributes["wind_speed_mps"];
                var windDir = feature.Attributes["wind_direction_deg"];
                var area = feature.Attributes["scent_area_m2"];
                var distance = feature.Attributes["max_distance_m"];
                
                Console.WriteLine($"  {++count}. Seq: {sequence}, Wind: {windSpeed}m/s @ {windDir}°, Area: {area}m², MaxDist: {distance}m");
                
                if (feature.Geometry != null)
                {
                    var geomType = feature.Geometry.GeometryType;
                    var isValid = feature.Geometry.IsValid ? "? Valid" : "? Invalid";
                    var srid = feature.Geometry.SRID;
                    
                    Console.WriteLine($"     Geometry: {geomType}, SRID: {srid}, {isValid}");
                    
                    if (feature.Geometry is Polygon polygon)
                    {
                        var envelope = polygon.EnvelopeInternal;
                        Console.WriteLine($"     Vertices: {polygon.NumPoints}, Centroid: ({polygon.Centroid.X:F6}, {polygon.Centroid.Y:F6})");
                        Console.WriteLine($"     Envelope: ({envelope.MinX:F6}, {envelope.MinY:F6}) to ({envelope.MaxX:F6}, {envelope.MaxY:F6})");
                        
                        if (!polygon.IsValid)
                        {
                            try
                            {
                                var validator = new IsValidOp(polygon);
                                var validationError = validator.ValidationError;
                                if (validationError != null)
                                {
                                    Console.WriteLine($"     ??  Geometry error: {validationError.Message} at {validationError.Coordinate}");
                                }
                            }
                            catch
                            {
                                Console.WriteLine($"     ??  Geometry is invalid (validation details unavailable)");
                            }
                        }
                    }
                    else if (feature.Geometry is Point point)
                    {
                        Console.WriteLine($"     ?? POINT DETECTED: ({point.X:F6}, {point.Y:F6}) - This should be a polygon!");
                    }
                    else
                    {
                        Console.WriteLine($"     Unexpected geometry type: {geomType}");
                    }
                }
            }
            
            Console.WriteLine($"\n? Verification complete! Analyzed {totalCount} features.");
            
            // QGIS compatibility analysis and tips
            Console.WriteLine("\n???  QGIS Compatibility Analysis:");
            
            if (geometryStats["Point"] > 0)
            {
                Console.WriteLine("? PROBLEM: Layer contains Point geometries instead of Polygons");
                Console.WriteLine("   ROOT CAUSE: The polygon creation or insertion process failed");
                Console.WriteLine("   SOLUTION: Regenerate the GeoPackage with corrected polygon creation");
                Console.WriteLine("\n?? Immediate workarounds:");
                Console.WriteLine("   1. Use the GeoJSON file instead (rover_windpolygon.geojson)");
                Console.WriteLine("   2. Recreate the layer with CreateLayerAsync using sample polygon geometry");
            }
            else
            {
                Console.WriteLine("? Layer geometry types are correct for QGIS");
                Console.WriteLine("\n?? QGIS Visualization Steps:");
                Console.WriteLine("1. Layer ? Add Layer ? Add Vector Layer ? select rover_windpolygon.gpkg");
                Console.WriteLine("2. Right-click layer ? Properties ? Symbology");
                Console.WriteLine("3. Set Fill opacity to 50% to see overlapping areas");
                Console.WriteLine("4. Use Graduated symbols with 'wind_speed_mps' for color coding");
                Console.WriteLine("5. Add rover_data.gpkg as points layer for comparison");
            }
            
            if (geometryStats["Invalid"] > 0)
            {
                Console.WriteLine("\n??  If QGIS shows errors with invalid geometries:");
                Console.WriteLine("   - Vector ? Geometry Tools ? Fix Geometries");
                Console.WriteLine("   - Or regenerate polygons with stricter validation");
            }
            
            // File recommendations
            Console.WriteLine("\n?? File Format Recommendations:");
            Console.WriteLine("   ?? Best for QGIS: GeoPackage (.gpkg) - if geometries are correct");
            Console.WriteLine("   ?? Alternative: GeoJSON (.geojson) - always works but larger file size");
            Console.WriteLine("   ?? Backup: Export as Shapefile from QGIS if needed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error verifying wind polygons: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}