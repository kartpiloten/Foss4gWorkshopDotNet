# Rover Scent Detection System
## PowerPoint Slide Content

---

## SLIDE 1: Title Slide

**Rover Scent Detection System**
**Architecture Overview**

*Real-time Geospatial Processing for K9 Search & Rescue*

.NET 9 | Blazor Server | NetTopologySuite | PostgreSQL/GeoPackage

---

## SLIDE 2: System Overview

### High-Level Architecture (4 Layers)

```
??????????????????????????????????????
?   ?? PRESENTATION LAYER            ?
?   Blazor Server + Leaflet.js       ?
??????????????????????????????????????
              ? GeoJSON/WKT
??????????????????????????????????????
?   ?? SERVICE LAYER                 ?
?   ScentPolygonService              ?
??????????????????????????????????????
              ? Measurements
??????????????????????????????????????
?   ?? DATA ACCESS LAYER             ?
?   IRoverDataReader                 ?
??????????????????????????????????????
              ? SQL/Spatial
??????????????????????????????????????
?   ??? DATA STORAGE LAYER            ?
?   PostgreSQL / GeoPackage          ?
??????????????????????????????????????
```

**Key Principle**: Separation of concerns with clear boundaries

---

## SLIDE 3: Core Components

### 1. **Presentation Layer** - FrontendVersion2
- **Blazor Server** web application
- **Leaflet.js** for interactive mapping
- **JavaScript Interop** for map integration
- Real-time GeoJSON API endpoints

### 2. **Service Layer** - ScentPolygonLibrary
- **IHostedService** background worker
- Polls database every **1 second**
- **In-memory** polygon storage (ConcurrentDictionary)
- **Event-driven** architecture

### 3. **Data Access Layer** - ReadRoverDBStubLibrary
- **IRoverDataReader** interface
- Supports PostgreSQL & GeoPackage
- Factory pattern for provider selection

### 4. **Data Storage**
- **PostgreSQL** with PostGIS (enterprise)
- **GeoPackage** (portable, QGIS-compatible)

---

## SLIDE 4: ScentPolygonService - The Heart of the System

### Background Service (IHostedService)

**Responsibilities:**
- ?? Continuous monitoring (1 second polling)
- ?? Generate scent polygons on-demand
- ?? Store polygons in-memory (thread-safe)
- ?? Emit events for subscribers
- ?? Combine polygons into unified coverage

**Key Methods:**
```csharp
• GetAllPolygons()
• GetLatestPolygons(count)
• GetUnifiedScentPolygon()        ? NEW!
• GetUnifiedScentPolygonCached()  ? Optimized
• GeneratePolygonForMeasurement()
```

**Events:**
```csharp
• PolygonsUpdated    ? New polygons generated
• StatusUpdate       ? Periodic health check
```

---

## SLIDE 5: Scent Polygon Algorithm

### Step 1: Downwind Fan ???
- Wind direction + 180° (scent travels downwind)
- Distance: **60m - 280m** (based on wind speed)
- Angle: **±5° to ±30°** (narrower in strong wind)

### Step 2: Omnidirectional Circle ?
- **30 meter** radius around dog
- Close-range detection (no wind dependency)

### Step 3: Union Operation ??
- **NetTopologySuite** geometry operations
- Combines fan + circle ? single polygon
- Validates and fixes invalid geometries

### Wind Speed Modeling ??
| Speed | Distance | Condition |
|-------|----------|-----------|
| 0-0.5 m/s | 60m | Very light |
| 0.5-2 m/s | 100-130m | Good transport |
| 2-5 m/s | 130-205m | Optimal |
| 5-8 m/s | 205-280m | Some dilution |
| >8 m/s | Decreasing | High dilution |

---

## SLIDE 6: NEW Feature - Unified Polygons

### Combining Multiple Scent Polygons

**Purpose**: Show total coverage area of all scent detections

**Methods:**
```csharp
• GetUnifiedScentPolygon()              ? All polygons
• GetUnifiedScentPolygonForLatest(n)    ? Recent n polygons
• GetUnifiedScentPolygonForSession(id)  ? By session
• GetUnifiedScentPolygonForTimeRange()  ? Time window
```

**Algorithm:**
1. **Progressive Union** - Batch processing for performance
2. **Statistics** - Coverage efficiency, overlap analysis
3. **Simplification** - Douglas-Peucker smoothing
4. **Fallback** - Convex hull for problematic geometries

**Benefits:**
- Visualize total search coverage
- Identify gaps in coverage
- Analyze search effectiveness
- Plan next search areas

---

## SLIDE 7: Data Flow Sequence

```
1??  RoverSimulator
    ? Generates GPS + wind measurements
    
2??  PostgreSQL / GeoPackage
    ? Stores rover_measurements table
    
3??  ScentPolygonService (Background)
    ? Polls every 1 second
    ? Generates polygons
    
4??  In-Memory Storage
    ? ConcurrentDictionary<int, ScentPolygonResult>
    
5??  Events ? PolygonsUpdated
    ? Notifies subscribers
    
6??  Blazor API Endpoints
    ? Converts to GeoJSON
    
7??  Leaflet.js Map
    ? Renders on map
    
8??  User Browser
    ? Interactive visualization
```

---

## SLIDE 8: Technology Stack

| Layer | Technology | Purpose |
|-------|-----------|---------|
| **Frontend** | Blazor Server (.NET 9) | Web UI with C# |
| **Mapping** | Leaflet.js | Interactive maps |
| **Geometry** | NetTopologySuite | Spatial operations |
| **Database** | PostgreSQL + PostGIS | Spatial storage |
| **Alternative** | GeoPackage (SQLite) | Portable files |
| **Background** | IHostedService | Continuous monitoring |
| **Format** | GeoJSON, WKT | Interoperability |

**All .NET 9 & C# 13**

---

## SLIDE 9: Design Patterns

### 1. **Hosted Service Pattern**
- Lifecycle management (StartAsync/StopAsync)
- Background processing
- Graceful shutdown (Ctrl+C)

### 2. **Repository Pattern**
- IRoverDataReader interface
- Multiple implementations (PostgreSQL, GeoPackage)
- Testability and flexibility

### 3. **Event-Driven Architecture**
- Loose coupling
- Real-time notifications
- Scalable subscriber model

### 4. **Factory Pattern**
- Automatic connection testing
- Provider selection
- Error handling

### 5. **Singleton Service**
- Blazor DI container
- Shared state
- Thread-safe operations

---

## SLIDE 10: Key Data Types

### ScentPolygonResult (Individual)
```csharp
• Polygon (NetTopologySuite.Polygon)
• SessionId, Sequence, RecordedAt
• Latitude, Longitude
• WindDirectionDeg, WindSpeedMps
• ScentAreaM2, MaxDistanceM
• IsValid
```

### UnifiedScentPolygon (Combined) ? NEW
```csharp
• Polygon (Combined geometry)
• PolygonCount, TotalAreaM2
• CoverageEfficiency
• EarliestMeasurement, LatestMeasurement
• AverageWindSpeedMps, WindSpeedRange
• SessionIds, VertexCount
• IsValid
```

---

## SLIDE 11: Performance Characteristics

### Timing
- **Polling**: 1 second intervals
- **Polygon Generation**: ~1-5ms per polygon
- **Union Operations**: ~10-100ms (50-500 polygons)
- **Event Overhead**: Minimal

### Memory
- **All polygons** stored in-memory
- **ConcurrentDictionary** for thread safety
- **No persistence** - service-only architecture

### Scalability
- Lock-free concurrent operations
- Suitable for real-time scenarios
- Efficient for single rover tracking

---

## SLIDE 12: Projects in Solution

| Project | Type | Purpose |
|---------|------|---------|
| **RoverSimulator** | Console | Generate test data |
| **ReadRoverDBStubLibrary** | Class Library | Data access abstraction |
| **ScentPolygonLibrary** | Class Library | Core polygon logic |
| **ScentPolygonTester** | Console | Testing & monitoring |
| **FrontendVersion2** | Blazor Server | Web visualization |
| **FrontendVersion1** | Blazor Server | Learning version |

**All targeting .NET 8/9**

---

## SLIDE 13: Testing & Monitoring

### ScentPolygonTester
**Console application for validation**

**Features:**
- ? Real-time monitoring display
- ? Outputs to GeoPackage (QGIS-compatible)
- ? Live statistics and diagnostics
- ? Tests unified polygon functionality
- ? File locking detection (QGIS compatibility)

**Output:**
```
C:\temp\Rover1\ScentPolygons.gpkg
  ?? unified layer (with all attributes)
```

**Usage:**
- Run alongside RoverSimulator
- Monitor in console
- Visualize in QGIS simultaneously

---

## SLIDE 14: Blazor Integration

### Dependency Injection Setup
```csharp
// Program.cs
builder.Services.AddSingleton<ScentPolygonService>();
```

### Component Usage
```csharp
// Index.razor
@inject ScentPolygonService ScentService

protected override async Task OnInitializedAsync()
{
    ScentService.PolygonsUpdated += OnPolygonsUpdated;
    await ScentService.StartAsync();
}

private async Task OnPolygonsUpdated(object sender, ...)
{
    await InvokeAsync(StateHasChanged);
}
```

### API Endpoints
```csharp
app.MapGet("/api/scent-polygons", ...);
app.MapGet("/api/scent-coverage/unified", ...);
```

---

## SLIDE 15: Map Visualization

### Leaflet.js Layers (Bottom to Top)

**1. Forest Boundary** ??
- Green polygon
- Operational area

**2. Unified Coverage** ??
- Tan/orange, dashed border
- Combined scent detection area

**3. Individual Wind Polygons** ??
- Color-coded by wind speed
- Light blue ? Red (calm to strong)

**4. Rover Trail** ??
- Colored dots showing path
- Time-sequenced

**Interactive:**
- Click polygons for details
- Zoom/pan controls
- Layer toggles

---

## SLIDE 16: Color Coding by Wind Speed

| Color | Wind Speed | Condition |
|-------|-----------|-----------|
| ?? Light Blue | 0-2 m/s | Calm |
| ?? Blue | 2-4 m/s | Light breeze |
| ?? Yellow | 4-8 m/s | Moderate |
| ?? Orange | 8-10 m/s | Fresh breeze |
| ?? Red | 10+ m/s | Strong wind |

**Visual feedback** helps operators understand wind conditions at a glance

---

## SLIDE 17: Configuration Points

### Database Selection
- PostgreSQL (enterprise, networked)
- GeoPackage (portable, file-based)

### Service Configuration
```csharp
new ScentPolygonConfiguration
{
    OmnidirectionalRadiusMeters = 30.0,
    FanPolygonPoints = 15,
    MinimumDistanceMultiplier = 0.4
}
```

### Polling Interval
```csharp
pollIntervalMs: 1000  // 1 second default
```

### Unified Options
- All polygons
- Latest N
- By session
- By time range

---

## SLIDE 18: Key Benefits

### ? Real-Time Processing
- 1 second polling for near-instant updates
- Event-driven notifications

### ? Flexible Data Sources
- PostgreSQL or GeoPackage
- Automatic provider selection

### ? Unified Coverage Analysis ?
- Combine multiple polygons
- Identify coverage gaps
- Optimize search patterns

### ? Web-Based Visualization
- No desktop GIS required
- Accessible from any device
- Interactive and responsive

### ? QGIS Integration
- Export to GeoPackage
- Professional GIS analysis
- Standard OGC formats

---

## SLIDE 19: Future Enhancements

### Planned Features
- ?? **SignalR** for real-time push (no polling)
- ?? **Authentication** and role-based access
- ?? **Playback/Animation** of historical data
- ?? **Mobile-responsive** design
- ?? **Export** capabilities (KML, Shapefile)
- ???? **Multi-rover** support
- ?? **Advanced filtering** and search
- ?? **Analytics dashboard**

---

## SLIDE 20: Conclusion

### System Highlights

**Architecture Strengths:**
- Clean separation of concerns
- Event-driven, loosely coupled
- Thread-safe, concurrent operations
- Flexible data sources

**Technical Excellence:**
- Modern .NET 9 / C# 13
- Industry-standard geospatial libraries
- OGC-compliant data formats
- Scalable design patterns

**Practical Benefits:**
- Real-time visualization
- Easy integration (Blazor/API)
- QGIS-compatible outputs
- Testable and maintainable

**Perfect for K9 search & rescue operations!** ????

---

## SLIDE 21: Q&A

**Questions?**

**Contact Information:**
- GitHub: https://github.com/kartpiloten/Foss4gWorkshopDotNet
- Project: Rover Scent Detection System

**Key Repositories:**
- ScentPolygonLibrary
- FrontendVersion2
- ReadRoverDBStubLibrary

**Technologies:**
- .NET 9, Blazor Server
- NetTopologySuite
- PostgreSQL/PostGIS
- Leaflet.js

---
