# ScentPolygonLibrary

A .NET 9 library for real-time scent polygon generation from rover wind measurements. This library continuously monitors rover measurement data and generates scent detection polygons using NetTopologySuite geometries.

## Overview

The ScentPolygonLibrary provides a service-based approach to generating scent polygons from rover measurements. Unlike the ConvertWinddataToPolygon console application which saves polygons to files, this library:

- **Generates polygons on-demand** using NetTopologySuite
- **Continuously monitors** rover measurements (every second)
- **Provides real-time events** for new polygon generation
- **Never persists** polygon data - serves geometries in-memory
- **Offers flexible querying** of generated polygons
- **Creates unified coverage areas** by combining multiple polygons

## Features

### Core Components

1. **ScentPolygonService** - Main service class that monitors rover data
2. **ScentPolygonCalculator** - Static utility for polygon calculations
3. **ScentPolygonConfiguration** - Configuration for polygon generation
4. **ScentPolygonResult** - Result object containing polygon and metadata
5. **UnifiedScentPolygon** - Combined coverage area from multiple polygons

### Key Capabilities

- **Real-time Monitoring**: Polls rover measurements every second
- **Event-Driven Architecture**: Emits events for new polygons and status updates
- **Multiple Query Methods**: Get all, latest N, by session, by time range
- **On-Demand Generation**: Calculate polygons for specific measurements
- **Unified Coverage Areas**: Combine multiple polygons into total coverage zones
- **Thread-Safe Operations**: Concurrent access to polygon collections
- **Configurable Parameters**: Adjustable fan angles, radii, and polling intervals

## Usage

### Basic Setup

```csharp
// Create data reader (PostgreSQL or GeoPackage)
var dataReader = new GeoPackageRoverDataReader(@"C:\temp\Rover1\");

// Create configuration (optional - uses defaults if null)
var config = new ScentPolygonConfiguration
{
    OmnidirectionalRadiusMeters = 30.0,
    FanPolygonPoints = 15,
    MinimumDistanceMultiplier = 0.4
};

// Create and start the service
using var service = new ScentPolygonService(dataReader, config, pollIntervalMs: 1000);

// Subscribe to events
service.PolygonsUpdated += (sender, e) =>
{
    Console.WriteLine($"Added {e.NewPolygons.Count} new scent polygons");
    foreach (var polygon in e.NewPolygons)
    {
        Console.WriteLine($"Polygon at ({polygon.Latitude:F6}, {polygon.Longitude:F6})");
        Console.WriteLine($"Text: {ScentPolygonCalculator.PolygonToText(polygon.Polygon)}");
    }
};

service.StatusUpdate += (sender, e) =>
{
    Console.WriteLine($"Total polygons: {e.TotalPolygonCount}");
};

// Start monitoring
await service.StartAsync();

// Keep running...
```

### Querying Polygons

```csharp
// Get all polygons
var allPolygons = service.GetAllPolygons();

// Get latest 10 polygons
var recent = service.GetLatestPolygons(10);

// Get polygons for specific session
var sessionPolygons = service.GetPolygonsForSession(sessionId);

// Get polygons in time range
var timeRangePolygons = service.GetPolygonsInTimeRange(startTime, endTime);

// Get just the latest
var latest = service.LatestPolygon;
```

### Unified Coverage Areas

**NEW**: The library now supports creating unified scent polygons that combine multiple individual polygons into single coverage areas:

```csharp
// Get unified coverage area for all polygons
var unifiedAll = service.GetUnifiedScentPolygon();
if (unifiedAll != null)
{
    Console.WriteLine($"Total coverage: {unifiedAll.TotalAreaM2:F0} m²");
    Console.WriteLine($"Combines {unifiedAll.PolygonCount} individual polygons");
    Console.WriteLine($"Coverage efficiency: {unifiedAll.CoverageEfficiency * 100:F1}%");
}

// Get unified coverage for latest 50 polygons
var unifiedRecent = service.GetUnifiedScentPolygonForLatest(50);

// Get unified coverage for specific session
var unifiedSession = service.GetUnifiedScentPolygonForSession(sessionId);

// Get unified coverage for time range
var unifiedTimeRange = service.GetUnifiedScentPolygonForTimeRange(startTime, endTime);
```

### Working with Unified Polygons

```csharp
var unified = service.GetUnifiedScentPolygon();
if (unified != null)
{
    // Geometry information
    Console.WriteLine($"Polygon: {unified.Polygon}"); // NetTopologySuite Polygon
    Console.WriteLine($"Area: {unified.TotalAreaM2:F0} m²");
    Console.WriteLine($"Vertices: {unified.VertexCount}");
    Console.WriteLine($"Valid: {unified.IsValid}");
    
    // Statistics
    Console.WriteLine($"Individual polygons: {unified.PolygonCount}");
    Console.WriteLine($"Time span: {unified.LatestMeasurement - unified.EarliestMeasurement}");
    Console.WriteLine($"Average wind: {unified.AverageWindSpeedMps:F1} m/s");
    Console.WriteLine($"Wind range: {unified.WindSpeedRange.Min:F1}-{unified.WindSpeedRange.Max:F1} m/s");
    Console.WriteLine($"Sessions: {unified.SessionIds.Count}");
    
    // Coverage analysis
    Console.WriteLine($"Coverage efficiency: {unified.CoverageEfficiency * 100:F1}%");
    // Lower efficiency = more overlap between individual polygons
    // Higher efficiency = less overlap, more spread out coverage
    
    // Text representation
    Console.WriteLine(ScentPolygonCalculator.UnifiedPolygonToText(unified));
}
```

### On-Demand Generation

```csharp
// Generate polygon for specific measurement
var measurement = await dataReader.GetLatestMeasurementAsync();
if (measurement != null)
{
    var polygon = service.GeneratePolygonForMeasurement(measurement);
    Console.WriteLine($"Generated polygon: {ScentPolygonCalculator.PolygonToText(polygon.Polygon)}");
}
```

## Configuration

### ScentPolygonConfiguration Properties

- **OmnidirectionalRadiusMeters** (default: 30.0) - Radius for circular detection around the dog
- **FanPolygonPoints** (default: 15) - Number of points in the downwind fan polygon  
- **MinimumDistanceMultiplier** (default: 0.4) - Minimum distance multiplier for fan edges

### Service Parameters

- **pollIntervalMs** - How often to check for new measurements (default: 1000ms)
- **dataReader** - IRoverDataReader implementation (PostgreSQL or GeoPackage)

## Events

### PolygonsUpdated Event

Triggered when new scent polygons are generated:

```csharp
public class ScentPolygonUpdateEventArgs
{
    public List<ScentPolygonResult> NewPolygons { get; }
    public int TotalPolygonCount { get; }
    public ScentPolygonResult? LatestPolygon { get; }
    public DateTimeOffset UpdateTime { get; }
}
```

### StatusUpdate Event  

Periodic status updates (every 10 seconds by default):

```csharp
public class ScentPolygonStatusEventArgs
{
    public int TotalPolygonCount { get; }
    public ScentPolygonResult? LatestPolygon { get; }
    public DateTimeOffset StatusTime { get; }
    public string DataSource { get; }
}
```

## Scent Polygon Algorithm

The library uses the same scent detection model as ConvertWinddataToPolygon:

1. **Downwind Fan**: Creates a fan-shaped polygon extending downwind
   - Wind direction indicates where wind comes FROM
   - Scent extends DOWNWIND (wind direction + 180°)
   - Fan angle narrows with higher wind speeds
   - Distance varies with wind speed (60m - 280m range)

2. **Omnidirectional Circle**: 30-meter radius around the dog position

3. **Union**: Combines fan and circle using NetTopologySuite Union operation

4. **Validation**: Ensures polygon validity and attempts fixes if needed

### Unified Polygon Algorithm

The unified polygon functionality combines multiple individual scent polygons:

1. **Progressive Union**: Combines polygons in batches for performance
2. **Geometry Handling**: Manages MultiPolygon results by selecting largest polygon
3. **Smoothing**: Applies Douglas-Peucker simplification for readability
4. **Statistics**: Calculates coverage efficiency and overlap analysis
5. **Error Recovery**: Uses convex hull as fallback for problematic unions

### Wind Speed Modeling

- **0-0.5 m/s**: 60m detection (very light wind, limited transport)
- **0.5-2 m/s**: 100-130m detection (good scent transport) 
- **2-5 m/s**: 130-205m detection (optimal conditions)
- **5-8 m/s**: 205-280m detection (some dilution)
- **>8 m/s**: Decreasing distance due to dilution

### Fan Angles

- **<1 m/s**: ±30° (wide dispersion)
- **1-3 m/s**: ±15-30° (moderate focus)
- **3-6 m/s**: ±9-15° (focused)
- **>6 m/s**: ±5-9° (very focused)

## Data Types

### ScentPolygonResult

Individual scent polygon with metadata:

```csharp
public class ScentPolygonResult
{
    public Polygon Polygon { get; }           // NetTopologySuite polygon geometry
    public Guid SessionId { get; }           // Rover session identifier
    public int Sequence { get; }             // Measurement sequence number
    public DateTimeOffset RecordedAt { get; } // When measurement was taken
    public double Latitude { get; }          // Rover latitude
    public double Longitude { get; }         // Rover longitude
    public double WindDirectionDeg { get; }  // Wind direction in degrees
    public double WindSpeedMps { get; }      // Wind speed in m/s
    public double ScentAreaM2 { get; }       // Polygon area in square meters
    public double MaxDistanceM { get; }      // Maximum scent detection distance
    public bool IsValid { get; }             // Whether polygon geometry is valid
}
```

### UnifiedScentPolygon

Combined coverage area from multiple polygons:

```csharp
public class UnifiedScentPolygon
{
    public Polygon Polygon { get; }                    // Combined polygon geometry
    public int PolygonCount { get; }                   // Number of polygons combined
    public double TotalAreaM2 { get; }                 // Total unified area
    public double IndividualAreasSum { get; }          // Sum of individual areas
    public DateTimeOffset EarliestMeasurement { get; } // Time range start
    public DateTimeOffset LatestMeasurement { get; }   // Time range end
    public double AverageWindSpeedMps { get; }         // Average wind speed
    public (double Min, double Max) WindSpeedRange { get; } // Wind speed range
    public List<Guid> SessionIds { get; }              // Sessions included
    public int VertexCount { get; }                    // Polygon complexity
    public bool IsValid { get; }                       // Geometry validity
    public double CoverageEfficiency { get; }          // TotalArea / IndividualSum
}
```

## Dependencies

- **.NET 9.0** - Target framework
- **NetTopologySuite** - Spatial geometry operations
- **ReadRoverDBStubLibrary** - Rover data access

## Architecture

The library follows a clean service-oriented architecture:

```
ScentPolygonService
??? IRoverDataReader (data access)
??? ScentPolygonConfiguration (settings)
??? Timer (polling)
??? ConcurrentDictionary<int, ScentPolygonResult> (storage)
??? Events (notifications)

ScentPolygonCalculator (static utilities)
??? CreateScentPolygon (individual polygon generation)
??? CreateUnifiedPolygon (polygon combination)
??? CalculateMaxScentDistance (wind modeling)
??? PolygonToText (text representation)
??? UnifiedPolygonToText (unified polygon text)
```

Key design decisions:

- **In-Memory Only**: No persistence - polygons exist only in memory
- **Thread-Safe**: ConcurrentDictionary for safe concurrent access
- **Event-Driven**: Loose coupling through events
- **Disposable**: Proper resource cleanup
- **Configurable**: Flexible polygon generation parameters
- **Union Operations**: NetTopologySuite for spatial operations

## Performance Characteristics

- **Memory Usage**: Stores all generated polygons in memory
- **Polling Frequency**: Configurable (default 1 second)
- **Polygon Generation**: ~1-5ms per polygon on modern hardware
- **Union Operations**: ~10-100ms for 50-500 polygons depending on complexity
- **Event Overhead**: Minimal - events only fire when new data available
- **Thread Safety**: Lock-free concurrent operations

## Error Handling

The service is designed to be resilient:

- **Silent Failures**: Continues operation if individual polygon generation fails
- **Connection Issues**: Continues polling even if temporary database issues occur  
- **Invalid Polygons**: Skips invalid polygons, attempts automatic fixes
- **Union Failures**: Falls back to convex hull for problematic geometry unions
- **Resource Cleanup**: Proper disposal prevents resource leaks

## Comparison with ConvertWinddataToPolygon

| Feature | ConvertWinddataToPolygon | ScentPolygonLibrary |
|---------|-------------------------|-------------------|
| **Purpose** | Batch conversion to files | Real-time service |
| **Output** | GeoPackage + GeoJSON files | NetTopologySuite geometries |
| **Storage** | Persistent files | In-memory only |
| **Operation** | One-time conversion | Continuous monitoring |
| **Usage** | Stand-alone console app | Library for integration |
| **Events** | None | Real-time update events |
| **Querying** | File-based | Multiple query methods |
| **Unified Coverage** | Single layer in GeoPackage | On-demand union operations |

## Testing

Use the **ScentPolygonTester** application to test the library:

```bash
dotnet run --project ScentPolygonTester
```

The tester provides:
- Real-time polygon generation monitoring
- Interactive commands (latest, count, last N, stats, unified)
- Detailed polygon text output
- Unified polygon demonstrations
- Service statistics and diagnostics

### New Unified Polygon Commands

- **`unified`** - Show unified polygon for all scent polygons
- **`unified 10`** - Show unified polygon for latest 10 polygons
- **`unified session`** - Show unified polygons grouped by session

## Integration Examples

### Blazor Server Integration

```csharp
// In Program.cs
builder.Services.AddSingleton<ScentPolygonService>(provider =>
{
    var dataReader = provider.GetRequiredService<IRoverDataReader>();
    var config = new ScentPolygonConfiguration();
    return new ScentPolygonService(dataReader, config);
});

// In Blazor component
@inject ScentPolygonService ScentService

protected override async Task OnInitializedAsync()
{
    ScentService.PolygonsUpdated += OnPolygonsUpdated;
    await ScentService.StartAsync();
}

private async Task OnPolygonsUpdated(object sender, ScentPolygonUpdateEventArgs e)
{
    await InvokeAsync(StateHasChanged);
}

// Get unified coverage for map display
private void UpdateMapCoverage()
{
    var unified = ScentService.GetUnifiedScentPolygon();
    if (unified != null)
    {
        // Convert to GeoJSON or other map format
        var wkt = unified.Polygon.ToText();
        // Update map layer...
    }
}
```

### API Endpoint Integration

```csharp
app.MapGet("/api/scent-polygons", (ScentPolygonService service) => 
{
    return service.GetAllPolygons().Select(p => new
    {
        p.Sequence,
        p.Latitude, 
        p.Longitude,
        p.WindSpeedMps,
        p.WindDirectionDeg,
        p.ScentAreaM2,
        Geometry = p.Polygon.ToText() // WKT format
    });
});

app.MapGet("/api/scent-polygons/latest/{count:int}", (int count, ScentPolygonService service) =>
{
    return service.GetLatestPolygons(count);
});

app.MapGet("/api/scent-coverage/unified", (ScentPolygonService service) =>
{
    var unified = service.GetUnifiedScentPolygon();
    return unified == null ? null : new
    {
        unified.PolygonCount,
        unified.TotalAreaM2,
        unified.CoverageEfficiency,
        unified.AverageWindSpeedMps,
        Geometry = unified.Polygon.ToText()
    };
});
```

This library provides a solid foundation for real-time scent polygon generation and can be easily integrated into web applications, desktop apps, or other services that need live scent detection visualization.