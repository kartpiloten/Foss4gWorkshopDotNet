# ScentPolygonTester

A console application for testing the **ScentPolygonLibrary** functionality. This application demonstrates real-time scent polygon generation from rover measurements, including the new unified polygon features.

## Features

- **Real-time Monitoring**: Continuously polls rover database every second
- **Interactive Commands**: Query polygons and view statistics
- **Text Output**: Displays polygons in human-readable text format
- **Event Handling**: Shows live updates as new polygons are generated
- **Statistics**: Provides service statistics and polygon analysis
- **Unified Polygons**: Demonstrates combined coverage area functionality

## Usage

### Running the Application

```bash
dotnet run --project ScentPolygonTester
```

### Configuration

The application uses the same database configuration as other applications in the solution:

- **PostgreSQL** (default): Connects to `192.168.1.254:5432`
- **GeoPackage** (fallback): Reads from `C:\temp\Rover1\rover_data.gpkg`

To change the database type, modify `DEFAULT_DATABASE_TYPE` in the `TesterConfiguration` class.

### Interactive Commands

Once running, you can enter the following commands:

#### Basic Commands
- **`latest`** - Show detailed information about the latest scent polygon
- **`count`** - Display current polygon count
- **`last N`** - Show summary of the last N polygons (e.g., `last 5`, `last 10`)
- **`stats`** - Display comprehensive service statistics

#### Unified Polygon Commands (NEW)
- **`unified`** - Show unified scent polygon combining all individual polygons
- **`unified N`** - Show unified polygon for the latest N polygons (e.g., `unified 10`, `unified 50`)
- **`unified session`** - Show unified polygons grouped by rover sessions

#### Control Commands
- **`Ctrl+C`** - Stop the service and exit

### Sample Output

#### Startup
```
========================================
     SCENT POLYGON SERVICE TESTER
========================================
Database type preference: POSTGRES
Press Ctrl+C to stop monitoring...

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

Scent polygon service is now running!
The service will continuously monitor rover measurements and generate scent polygons.
Data source: PostgresRoverData
Initial polygons loaded: 1247
Configuration: 30m omnidirectional radius, 15 fan points
```

#### Live Updates
```
[POLYGON UPDATE] Added 3 new scent polygons. Total: 1250
  Sequence 1248: Wind 2.8m/s @ 142deg
    Location: (-36.741198, 174.632589)
    Scent Area: 16789 m² (Max Distance: 148m)
    Polygon: POLYGON(174.632589,-36.741198) (174.633245,-36.740876) (174.633156,-36.741487) ... and 13 more points)

  Sequence 1249: Wind 3.1m/s @ 138deg
    Location: (-36.741165, 174.632611)
    Scent Area: 18234 m² (Max Distance: 152m)
    Polygon: POLYGON(174.632611,-36.741165) (174.633298,-36.740798) (174.633201,-36.741521) ... and 13 more points)

[STATUS] Total polygons: 1250, Latest: Seq=1249, Location=(-36.741165, 174.632611), Wind=3.1m/s @ 138deg, Area=18234m², Time=14:35:42
```

#### Command Examples

**`latest` command:**
```
Latest Scent Polygon Details:
  Sequence: 1249
  Session ID: 8b4f2d1e-7c3a-4e5f-9b2a-1d8e6f4c2a5b
  Recorded: 2024-01-15 14:35:42
  Location: (-36.741165, 174.632611)
  Wind: 3.1 m/s @ 138°
  Scent Area: 18234 m² (1.82 hectares)
  Max Distance: 152 meters
  Polygon Valid: True
  Polygon Text: POLYGON(174.632611,-36.741165) (174.633298,-36.740798) (174.633201,-36.741521) ... and 13 more points)
```

**`stats` command:**
```
Scent Polygon Service Statistics:
  Total Polygons: 1250
  Time Range: 20.8 minutes
  Total Coverage Area: 21847362 m² (2184.74 hectares)
  Average Area per Polygon: 17478 m²
  Area Range: 8942 - 31256 m²
  Average Wind Speed: 3.7 m/s
  Valid Polygons: 1249 / 1250
```

**`unified` command (NEW):**
```
============================================================
UNIFIED SCENT POLYGON (ALL POLYGONS)
============================================================
UNIFIED SCENT POLYGON:
  Combines: 1250 individual polygons
  Total Area: 2847392 m² (284.74 hectares)
  Coverage Efficiency: 13.0% (lower = more overlap)
  Time Range: 20.8 minutes
  Wind Speed: 3.7 m/s avg (range: 1.2-7.3)
  Sessions: 3
  Vertices: 127
  Valid: True
  Geometry: POLYGON(174.628456,-36.745231) (174.634782,-36.737895) (174.639124,-36.741567) ... and 117 more points)
============================================================
```

**`unified 10` command:**
```
============================================================
UNIFIED SCENT POLYGON (LATEST 10 POLYGONS)
============================================================
UNIFIED SCENT POLYGON:
  Combines: 10 individual polygons
  Total Area: 98734 m² (9.87 hectares)
  Coverage Efficiency: 56.2% (lower = more overlap)
  Time Range: 0.2 minutes
  Wind Speed: 3.1 m/s avg (range: 2.8-3.4)
  Sessions: 1
  Vertices: 45
  Valid: True
  Geometry: POLYGON(174.632234,-36.741876) (174.633567,-36.740234) (174.633123,-36.742134) ... and 35 more points)
============================================================
```

**`unified session` command:**
```
============================================================
UNIFIED SCENT POLYGONS BY SESSION (3 SESSIONS)
============================================================

SESSION: 8b4f2d1e-7c3a-4e5f-9b2a-1d8e6f4c2a5b
Measurements: 456
Time Range: 14:15:32 - 14:28:45
Unified Area: 1234567 m² (123.46 hectares)
Coverage Efficiency: 45.2%
Wind Speed: 3.5 m/s avg (range: 2.1-6.8)
Vertices: 89
Valid: True
Polygon: POLYGON(174.629123,-36.744567) (174.635234,-36.738901) ... and 79 more points)
----------------------------------------

SESSION: a2c4e6f8-1b3d-5f7h-9j2k-4l6n8p0q2r4t
Measurements: 523
Time Range: 14:29:12 - 14:41:56
Unified Area: 987654 m² (98.77 hectares)
Coverage Efficiency: 38.7%
Wind Speed: 4.1 m/s avg (range: 1.8-7.3)
Vertices: 74
Valid: True
Polygon: POLYGON(174.631456,-36.743210) (174.637890,-36.739654) ... and 64 more points)
----------------------------------------

SESSION: f4h6j8k0-2l4n-6p8r-0t2v-4x6z8a0c2e4g
Measurements: 271
Time Range: 14:42:23 - 14:52:18
Unified Area: 625031 m² (62.50 hectares)
Coverage Efficiency: 52.3%
Wind Speed: 2.9 m/s avg (range: 1.2-4.9)
Vertices: 61
Valid: True
Polygon: POLYGON(174.633789,-36.742345) (174.638234,-36.740123) ... and 51 more points)
----------------------------------------
============================================================
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
scentService.PolygonsUpdated += (sender, e) =>
{
    // Handle new polygon events
    foreach (var polygon in e.NewPolygons)
    {
        Console.WriteLine($"New polygon: {ScentPolygonCalculator.PolygonToText(polygon.Polygon)}");
    }
};

scentService.StatusUpdate += (sender, e) =>
{
    // Handle periodic status updates
    Console.WriteLine($"Status: {e.TotalPolygonCount} polygons");
};
```

### Individual Polygon Queries
```csharp
// Various ways to query the service
var latest = service.LatestPolygon;
var count = service.Count;
var lastFive = service.GetLatestPolygons(5);
var allPolygons = service.GetAllPolygons();
```

### Unified Polygon Queries (NEW)
```csharp
// Get unified coverage areas
var unifiedAll = service.GetUnifiedScentPolygon();
var unifiedRecent = service.GetUnifiedScentPolygonForLatest(10);
var unifiedBySession = service.GetUnifiedScentPolygonForSession(sessionId);

// Display unified polygon information
if (unified != null)
{
    Console.WriteLine(ScentPolygonCalculator.UnifiedPolygonToText(unified));
}
```

## Use Cases

This tester is useful for:

1. **Library Development**: Testing ScentPolygonLibrary functionality
2. **Algorithm Validation**: Verifying scent polygon calculations
3. **Performance Testing**: Monitoring real-time polygon generation
4. **Coverage Analysis**: Understanding unified polygon behavior
5. **Integration Planning**: Understanding library behavior before integration
6. **Debugging**: Troubleshooting polygon generation issues

## Understanding Unified Polygons

### Coverage Efficiency

The **Coverage Efficiency** metric helps understand polygon overlap:

- **High Efficiency (60-90%)**: Less overlap, more spread out coverage
- **Medium Efficiency (30-60%)**: Moderate overlap, good coverage balance  
- **Low Efficiency (10-30%)**: High overlap, concentrated coverage area

### Practical Applications

1. **Total Coverage**: Use `unified` to see complete search area coverage
2. **Recent Activity**: Use `unified 20` to see current search focus
3. **Session Analysis**: Use `unified session` to compare different search periods
4. **Planning**: Identify gaps or overlaps in scent detection coverage

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

### Performance Issues
- Reduce polling frequency (increase pollIntervalMs)
- Check database query performance
- Monitor memory usage with large datasets
- Use smaller counts for unified operations (e.g., `unified 50` instead of `unified`)

## Integration Example

This tester shows how to integrate ScentPolygonLibrary into other applications:

```csharp
// 1. Create service with configuration
using var service = new ScentPolygonService(dataReader, config);

// 2. Subscribe to events
service.PolygonsUpdated += HandleNewPolygons;

// 3. Start monitoring
await service.StartAsync();

// 4. Query individual polygons
var recentPolygons = service.GetLatestPolygons(10);

// 5. Query unified coverage (NEW)
var totalCoverage = service.GetUnifiedScentPolygon();
```

The same pattern can be used in Blazor applications, WPF desktop apps, or web APIs to provide real-time scent polygon functionality with unified coverage area visualization.