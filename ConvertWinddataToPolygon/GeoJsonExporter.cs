using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.Globalization;
using System.Text.Json;

namespace ConvertWinddataToPolygon;

/// <summary>
/// Utility to export wind polygons to GeoJSON format for web visualization
/// </summary>
public static class GeoJsonExporter
{
    public static async Task ExportToGeoJsonAsync(string geoPackagePath, string outputPath)
    {
        Console.WriteLine($"\n?? Exporting to GeoJSON: {outputPath}");
        
        if (!File.Exists(geoPackagePath))
        {
            Console.WriteLine($"Wind polygon file not found: {geoPackagePath}");
            return;
        }

        try
        {
            using var geoPackage = await GeoPackage.OpenAsync(geoPackagePath, 4326);
            var layer = await geoPackage.EnsureLayerAsync("wind_scent_polygons", new Dictionary<string, string>(), 4326);
            
            var totalCount = await layer.CountAsync();
            if (totalCount == 0)
            {
                Console.WriteLine("No wind polygons to export.");
                return;
            }

            var geoJsonWriter = new GeoJsonWriter();
            var features = new List<object>();
            
            var readOptions = new ReadOptions(IncludeGeometry: true, OrderBy: "sequence ASC");
            
            await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
            {
                if (feature.Geometry is Polygon polygon && polygon.IsValid)
                {
                    // Ensure SRID is set for proper GeoJSON export
                    polygon.SRID = 4326;
                    
                    var geoJsonGeometry = JsonSerializer.Deserialize<object>(geoJsonWriter.Write(polygon));
                    
                    var geoJsonFeature = new
                    {
                        type = "Feature",
                        properties = new
                        {
                            session_id = feature.Attributes["session_id"],
                            sequence = int.Parse(feature.Attributes["sequence"] ?? "0", CultureInfo.InvariantCulture),
                            recorded_at = feature.Attributes["recorded_at"],
                            wind_direction_deg = int.Parse(feature.Attributes["wind_direction_deg"] ?? "0", CultureInfo.InvariantCulture),
                            wind_speed_mps = double.Parse(feature.Attributes["wind_speed_mps"] ?? "0", CultureInfo.InvariantCulture),
                            scent_area_m2 = double.Parse(feature.Attributes["scent_area_m2"] ?? "0", CultureInfo.InvariantCulture),
                            max_distance_m = double.Parse(feature.Attributes["max_distance_m"] ?? "0", CultureInfo.InvariantCulture)
                        },
                        geometry = geoJsonGeometry
                    };
                    
                    features.Add(geoJsonFeature);
                }
            }

            var geoJsonCollection = new
            {
                type = "FeatureCollection",
                name = "Wind Scent Polygons",
                crs = new
                {
                    type = "name",
                    properties = new { name = "urn:ogc:def:crs:EPSG::4326" }
                },
                features = features
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var geoJsonString = JsonSerializer.Serialize(geoJsonCollection, jsonOptions);
            await File.WriteAllTextAsync(outputPath, geoJsonString);
            
            var fileInfo = new FileInfo(outputPath);
            Console.WriteLine($"? Exported {features.Count} polygons to GeoJSON ({fileInfo.Length / 1024.0:F1} KB)");
            Console.WriteLine("You can now:");
            Console.WriteLine("1. Open in web mapping libraries (Leaflet, OpenLayers, Mapbox)");
            Console.WriteLine("2. Load in QGIS as a vector layer");
            Console.WriteLine("3. Validate geometry online at geojsonlint.com");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting to GeoJSON: {ex.Message}");
        }
    }
}