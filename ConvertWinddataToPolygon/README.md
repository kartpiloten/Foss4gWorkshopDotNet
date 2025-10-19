# Wind Data to Polygon Converter

This project converts rover wind measurement data into comprehensive scent detection polygons that represent areas where a dog at the rover's position could detect human scent based on wind direction, speed, and the dog's natural omnidirectional scent detection ability.

## Overview

The converter reads rover data from `rover_data.gpkg` and creates a new GeoPackage file `rover_windpolygon.gpkg` containing polygon features that represent realistic canine scent detection areas.

## Enhanced Scent Model

Each polygon represents a **comprehensive scent detection zone** combining two key aspects:

### 1. Omnidirectional Detection (30m Buffer)
- **30-meter circular buffer** around the dog's position
- Represents baseline scent detection ability in all directions
- Accounts for the dog's natural ability to detect nearby scents regardless of wind

### 2. Upwind Enhanced Detection (Wind-Dependent Fan)
- **Fan-shaped extension** from dog's nose expanding upwind
- **Wind speed affects both distance and angle**:
  - Light wind (< 2 m/s): 50-80m range, wide ±30° dispersion
  - Moderate wind (2-5 m/s): 80-125m range, optimal conditions  
  - Strong wind (5-8 m/s): 125-155m range, narrower ±10-16° cone
  - Very strong wind (> 8 m/s): Reduced range due to dilution

### Combined Detection Zone
- **Union operation** combines the circular buffer and upwind fan
- Results in a **teardrop or comet-shaped polygon**
- Narrow tail pointing downwind, wide bulbous area upwind
- Realistic representation of canine scent detection capabilities

## Output Schema

The wind polygon GeoPackage contains the following attributes:

- `session_id`: Rover session identifier
- `sequence`: Measurement sequence number
- `recorded_at`: Timestamp of measurement
- `wind_direction_deg`: Wind direction in degrees (0° = North)
- `wind_speed_mps`: Wind speed in meters per second
- `scent_area_m2`: Total scent detection area in square meters
- `max_distance_m`: Maximum scent detection distance in meters

## Usage

```bash
dotnet run
```

The program will:
1. Read rover measurements from the path configured via `Converter:GeoPackageFilePath` (defaults to `C:\temp\Rover1\rover_data.gpkg`)
2. Generate comprehensive scent polygons for each measurement
3. Save results to the folder specified by `Converter:OutputFolderPath` (defaults to `C:\temp\Rover1\`)
4. Export alternative GeoJSON format for web compatibility

## Visualization

Open the output GeoPackage in QGIS, ArcGIS, or other GIS software to visualize:
- The rover track (points from rover_data.gpkg)
- Comprehensive scent detection areas (polygons from rover_windpolygon.gpkg)
- Wind vectors and enhanced detection zones
- Overlapping scent areas showing cumulative coverage

## Scientific Basis

This model combines:
- **Atmospheric scent transport** (wind-dependent dispersion)
- **Canine olfactory capabilities** (omnidirectional detection)
- **Environmental factors** (wind speed effects on scent dilution)
- **Realistic detection distances** based on search and rescue research

Perfect for search and rescue applications, environmental monitoring, and understanding comprehensive scent dispersion patterns in the field! ????
