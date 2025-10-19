# ScentPolygonTester

A console application for testing the **ScentPolygonLibrary** functionality. This application demonstrates real-time scent polygon generation from rover measurements and outputs unified polygons to a GeoPackage file.

## Features

- **Real-time Monitoring**: Continuously polls rover database every second
- **GeoPackage Output**: Writes unified scent polygons to a GeoPackage file for visualization in QGIS
- **Event Handling**: Shows live updates as new polygons are generated
- **Automatic Updates**: Updates GeoPackage automatically as new rover data arrives

## Usage

### Running the Application

```bash
dotnet run --project ScentPolygonTester
```

### Stopping the Application

Press **Ctrl+C** to stop the service and exit gracefully.

### Sample Output

#### Startup
```
========================================
     SCENT POLYGON SERVICE TESTER
     WITH GEOPACKAGE OUTPUT
========================================
Database type preference: POSTGRES
Output GeoPackage: /tmp/ScentPolygons.gpkg

============================================================
DATABASE CONNECTION SETUP (SCENT POLYGON SERVICE)
============================================================
Database type: POSTGRES
Testing PostgreSQL connection for scent polygon generation...
Target: 192.168.1.254:5432, Database: AucklandRoverData, User: anders
Timeout: 10 seconds
SUCCESS: PostgreSQL connection successful - using PostgreSQL database
============================================================

Initializing scent polygon service...

[GeoPackage] Created new GeoPackage: ScentPolygons.gpkg
[GeoPackage] Ensured 'unified' layer exists with proper schema

Latest Scent Polygon:
  Sequence: 1249
  Location: (-36.741165, 174.632611)
  Wind: 3.1 m/s @ 138°
  Area: 18234 m²
  Polygon: POLYGON(174.632611,-36.741165) (174.633298,-36.740798) ...

Unified Scent Polygon (All Polygons):
  Combines: 1250 individual polygons
  Total Area: 2847392 m² (284.74 hectares)
  Coverage Efficiency: 13.0%
  Time Range: 20.8 minutes
  ...

GeoPackage output: /tmp/ScentPolygons.gpkg
Layer name: unified
The GeoPackage will be updated automatically as new rover data arrives.

NOTE: If the file is locked by QGIS or another application, updates will be skipped.
Close QGIS to allow updates, or use a different filename.

Press Ctrl+C to exit...
```

#### Live Updates
```
[GeoPackage] Polygon update received: 3 new polygons, total: 1250
[GeoPackage] Updating unified polygon (attempt 2): 1250 polygons, 2847392 m², version 2
[GeoPackage] ? Updated unified polygon: 284.74 hectares, 127 vertices

[Status] Total polygons: 1250, Latest: 1249, Source: PostgresRoverData
```

## Architecture

The tester demonstrates the following ScentPolygonLibrary features:

### Service Configuration
```csharp
var scentConfig = new ScentPolygonConfiguration
{
    OmnidirectionalRadiusMeters = 30.0,
    FanPolygonPoints = 15,
    MinimumDistanceMultiplier = 0.4
};

var scentService = new ScentPolygonService(
    dataReader, 
    scentConfig,
    pollIntervalMs: 1000 // Poll every second
);
```

### Event Handling
```csharp
scentService.PolygonsUpdated += async (sender, args) =>
{
    // Handle new polygon events
    await geoPackageUpdater.UpdateUnifiedPolygonAsync(scentService);
};

scentService.StatusUpdate += (sender, args) =>
{
    // Handle periodic status updates
    Console.WriteLine($"Status: {args.TotalPolygonCount} polygons");
};
```

### GeoPackage Output

The application automatically writes unified scent polygons to a GeoPackage file with the following attributes:

- **polygon_count**: Number of individual polygons combined
- **total_area_m2**: Total coverage area in square meters
- **total_area_hectares**: Total coverage area in hectares
- **coverage_efficiency**: Ratio indicating overlap (lower = more overlap)
- **average_wind_speed_mps**: Average wind speed across measurements
- **min_wind_speed_mps** / **max_wind_speed_mps**: Wind speed range
- **earliest_measurement** / **latest_measurement**: Time range
- **session_count**: Number of rover sessions included
- **vertex_count**: Number of vertices in the polygon
- **created_at**: Timestamp of when the record was created
- **unified_version**: Version number for tracking updates

## Use Cases

This tester is useful for:

1. **Library Development**: Testing ScentPolygonLibrary functionality
2. **Algorithm Validation**: Verifying scent polygon calculations
3. **Performance Testing**: Monitoring real-time polygon generation
4. **GIS Integration**: Generating GeoPackage files for visualization in QGIS
5. **Coverage Analysis**: Understanding unified polygon behavior over time
6. **Debugging**: Troubleshooting polygon generation issues

## Understanding Unified Polygons

### Coverage Efficiency

The **Coverage Efficiency** metric helps understand polygon overlap:

- **High Efficiency (60-90%)**: Less overlap, more spread out coverage
- **Medium Efficiency (30-60%)**: Moderate overlap, good coverage balance  
- **Low Efficiency (10-30%)**: High overlap, concentrated coverage area

### Performance Characteristics

- **Small datasets (1-50 polygons)**: Unified operations complete in <10ms
- **Medium datasets (50-500 polygons)**: Operations take 10-100ms
- **Large datasets (500+ polygons)**: Operations may take 100ms-1s
- **Memory usage**: Temporary increase during union operations

## Requirements

- **RoverSimulator** running or having run to generate measurement data
- **Database connectivity** (PostgreSQL server or GeoPackage file)
- **.NET 9** runtime

## Troubleshooting

### No Polygons Generated
- Ensure RoverSimulator has created measurement data
- Check database connection settings
- Verify the database contains rover_measurements table/layer

### Connection Issues
- For PostgreSQL: Check server availability and credentials
- For GeoPackage: Verify file exists and is not locked by QGIS

### Unified Polygon Errors
- "No polygons available": No individual polygons exist yet
- "Failed to create unified polygon": Geometry union operation failed
- Check polygon validity in individual polygon output

### GeoPackage File Locked
- Close QGIS or other GIS applications that may have the file open
- The application will create an alternative timestamped file if needed
- Updates will be skipped until the lock is released

### Performance Issues
- Reduce polling frequency (increase pollIntervalMs in code)
- Check database query performance
- Monitor memory usage with large datasets

## Integration Example

This tester shows how to integrate ScentPolygonLibrary into other applications:

```csharp
// 1. Create service with configuration
using var service = new ScentPolygonService(dataReader, config);

// 2. Subscribe to events
service.PolygonsUpdated += HandleNewPolygons;
service.StatusUpdate += HandleStatusUpdates;

// 3. Start monitoring
await service.StartAsync();

// 4. Wait for Ctrl+C
await Task.Delay(-1, cancellationToken);
```

The same pattern can be used in Blazor applications, WPF desktop apps, or web APIs to provide real-time scent polygon functionality with unified coverage area visualization.
