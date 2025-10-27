# Forest Boundary Reader - Example Usage

## ReadRoverDBStubTester Example

Add this code to `ReadRoverTesterProgram.cs` to demonstrate forest boundary validation:

```csharp
// Add at the top with other usings
using ReadRoverDBStubLibrary;

// Add after loading configuration, before creating the reader
Console.WriteLine("\n========== FOREST BOUNDARY VALIDATION ==========");

var boundaryFile = configuration.GetValue<string>("DatabaseConfiguration:BoundaryFile") 
    ?? "Solutionresources/RiverHeadForest.gpkg";

var boundaryPath = ResolveBoundaryFilePath(boundaryFile);

if (File.Exists(boundaryPath))
{
    using var boundaryReader = new ForestBoundaryReader(boundaryPath);
    var boundary = await boundaryReader.GetBoundaryPolygonAsync();
    
    if (boundary != null)
    {
        var bbox = await boundaryReader.GetBoundingBoxAsync();
        var centroid = await boundaryReader.GetCentroidAsync();
        
        Console.WriteLine($"? Forest boundary loaded:");
        Console.WriteLine($"  Bounding Box: ({bbox?.MinX:F6}, {bbox?.MinY:F6}) to ({bbox?.MaxX:F6}, {bbox?.MaxY:F6})");
        Console.WriteLine($"  Centroid: ({centroid?.Y:F6}, {centroid?.X:F6})");
        Console.WriteLine($"  Vertices: {boundary.NumPoints}");
    }
}

Console.WriteLine("==============================================\n");

// Helper function (add at the end of the file)
static string ResolveBoundaryFilePath(string relativePath)
{
    var currentDir = Directory.GetCurrentDirectory();
    var directory = new DirectoryInfo(currentDir);
    while (directory != null && !directory.GetFiles("*.sln").Any())
        directory = directory.Parent;
    var solutionRoot = directory?.FullName ?? currentDir;
    return Path.Combine(solutionRoot, relativePath);
}
```

## Validating Rover Positions

To check if rover measurements are within the forest boundary:

```csharp
var measurements = await reader.GetAllMeasurementsAsync();
using var boundaryReader = new ForestBoundaryReader(boundaryPath);

int insideCount = 0;
int outsideCount = 0;

foreach (var measurement in measurements)
{
    bool isInside = await boundaryReader.IsPointInBoundaryAsync(
        measurement.Latitude, 
        measurement.Longitude);
    
    if (isInside)
        insideCount++;
    else
        outsideCount++;
}

Console.WriteLine($"Measurements inside forest: {insideCount}");
Console.WriteLine($"Measurements outside forest: {outsideCount}");
```

## Configuration

Ensure `appsettings.json` includes:

```json
{
  "DatabaseConfiguration": {
    "BoundaryFile": "Solutionresources/RiverHeadForest.gpkg"
  }
}
```
