# ScentPolygonTester - Session Monitor

## Overview

The ScentPolygonTester monitors rover sessions in real-time and generates unified scent coverage polygons. It now includes **session selection** to let you choose which session to monitor.

## Quick Start

```powershell
cd ScentPolygonTester
dotnet run
```

## What Happens

### 1. Lists Available Sessions

```
Database: POSTGRES

Available sessions:
  1. auto_20251031_143022_a3b5c7
     ?? 150 measurements, last: 2025-10-31 14:35:22
  2. TeamSearch
     ?? 420 measurements, last: 2025-10-31 14:40:15

Enter session name or number to monitor:
```

### 2. You Choose

- Type `1` or `2` (number)
- Type `TeamSearch` (name)
- Press Enter (uses most recent)

### 3. Monitors in Real-Time

```
[Update] +5 polygons | Total: 155 | Affected rovers: 2

?? Coverage Statistics:
  Unified scent area:45,230 m²
  RiverHead forest:  1,250,000 m²
  Intersection:     42,100 m²
  Forest covered:        3%
  Active rovers:         2
```

## Use Cases

### Single Rover
```powershell
# Terminal 1 - Start rover
cd RoverSimulator
.\start-rover.ps1 Bella

# Terminal 2 - Monitor session
cd ScentPolygonTester
dotnet run
# Select the auto-generated session
```

### Team of Rovers
```powershell
# Computer 1
.\start-rover.ps1 Bella TeamSearch

# Computer 2
.\start-rover.ps1 Max TeamSearch

# Monitor computer
cd ScentPolygonTester
dotnet run
# Select "TeamSearch"
```

## Output GeoPackage

Creates: `C:\temp\Rover1\ScentPolygons.gpkg`

Layer: `unified` with attributes:
- `polygon_count` - Number of individual polygons combined
- `total_area_m2` - Total coverage area
- `active_rovers` - Number of active rovers
- `unified_version` - Version number (increments on updates)

## Configuration

`appsettings.json`:
```json
{
  "DatabaseConfiguration": {
    "DatabaseType": "postgres",
    "PostgresConnectionString": "Host=localhost;Database=roverdb;..."
  }
}
```

## Benefits

? **Choose which session to monitor** - no more guessing  
? **Real-time statistics** - see coverage grow live  
? **Per-rover threading** - efficient polygon processing  
? **Active rover count** - know how many rovers are working  
? **Safe to run alongside rovers** - read-only monitoring  

## Stop Monitoring

Press `Ctrl+C`

---

**New Feature:** Session selection makes it easy to monitor specific rover teams or historical sessions!
