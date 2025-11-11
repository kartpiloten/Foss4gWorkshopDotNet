# Forest Boundary Reader

## Overview
The `ForestBoundaryReader` class provides simple access to forest boundary polygons stored in OGC GeoPackage files.

## Usage

### Configuration (appsettings.json)

```json
{
  "DatabaseConfiguration": {
    "DatabaseType": "postgres",
    "PostgresConnectionString": "Host=localhost;Database=rover;...",
    "BoundaryFile": "Solutionresources/RiverHeadForest.gpkg",
    "ConnectionTimeoutSeconds": 10,
    "MaxRetryAttempts": 3,
    "RetryDelayMs": 2000
  }
}
```

### Code Example

```csharp
using RoverData.Repository;

// Create the reader
var boundaryReader = new ForestBoundaryReader(
    "Solutionresources/RiverHeadForest.gpkg",
    layerName: "riverheadforest");

// Get the boundary polygon
var boundary = await boundaryReader.GetBoundaryPolygonAsync();

if (boundary != null)
{
    // Check if a point is inside the boundary
    bool isInside = await boundaryReader.IsPointInBoundaryAsync(
        latitude: -36.8485, 
        longitude: 174.7633);
    
    // Get bounding box
    var bbox = await boundaryReader.GetBoundingBoxAsync();
    Console.WriteLine($"Bounds: ({bbox.MinX}, {bbox.MinY}) to ({bbox.MaxX}, {bbox.MaxY})");
    
    // Get centroid
    var centroid = await boundaryReader.GetCentroidAsync();
    Console.WriteLine($"Center: ({centroid.Y}, {centroid.X})");
}

// Dispose when done
boundaryReader.Dispose();
```

### Resolving the File Path

The boundary file path is typically relative to the solution root. Helper method:

```csharp
public static string ResolveBoundaryFilePath(string relativePath)
{
    // Walk up from current directory to find solution root
    var currentDir = Directory.GetCurrentDirectory();
    var directory = new DirectoryInfo(currentDir);

    while (directory != null && !directory.GetFiles("*.sln").Any())
    {
        directory = directory.Parent;
    }

    var solutionRoot = directory?.FullName ?? currentDir;
    return Path.Combine(solutionRoot, relativePath);
}

// Usage
var boundaryPath = ResolveBoundaryFilePath(
    config.GetValue<string>("DatabaseConfiguration:BoundaryFile") 
    ?? "Solutionresources/RiverHeadForest.gpkg");

var reader = new ForestBoundaryReader(boundaryPath);
```

## Features

- **Lazy Loading**: Boundary is loaded only when first accessed.
- **Caching**: Once loaded, the boundary polygon and bounding box are cached in memory for the lifetime of the `ForestBoundaryReader` instance.
- **Async/Await**: All public methods are asynchronous and support non-blocking usage.
- **Cancellation Support**: Methods accept an optional `CancellationToken` for cooperative cancellation.
- **Silent Errors**: The implementation returns `null` on missing files or on internal failures (it intentionally swallows exceptions). Callers should treat `null` as "boundary unavailable" and handle it appropriately.

> Note: The reader intentionally avoids throwing for missing or malformed GeoPackage files; this keeps downstream sample apps simple but means callers must check for `null` and handle that case.

## Dependencies

- `MapPiloteGeopackageHelper` - Async GeoPackage reader used to open and read layers/features.
- `NetTopologySuite` - Geometry types and operations (Polygon, Point, Envelope, Centroid).

Ensure these packages are referenced in the project that consumes `ForestBoundaryReader`.

## File Format

- **Format**: OGC GeoPackage (.gpkg)
- **Coordinate System**: EPSG:4326 (WGS84)
- **Geometry Type**: Polygon
- **Layer Name**: Configurable (default: "riverheadforest")

## Example GeoPackage Structure

```
RiverHeadForest.gpkg
??? riverheadforest (layer)
    ??? Polygon geometry (EPSG:4326)
```

## Use Cases

1. **Rover Boundary Validation**: Check if rover position is within operational area
2. **Coverage Calculation**: Intersect scent polygons with forest boundary
3. **Map Visualization**: Display forest boundary on maps
4. **Spatial Queries**: Perform spatial operations with forest geometry
