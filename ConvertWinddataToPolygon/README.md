# Wind Data to Polygon Converter

This project converts rover wind measurement data into scent detection polygons that represent areas where a dog at the rover's position could detect human scent based on wind direction and speed.

## Overview

The converter reads rover data from `rover_data.gpkg` and creates a new GeoPackage file `rover_windpolygon.gpkg` containing polygon features that represent scent detection areas.

## Scent Model

Each polygon represents the upwind area where a dog standing at the rover's position could potentially detect human scent. The model considers:

### Wind Speed Effects
- **Light wind (< 0.5 m/s)**: Limited scent transport, ~30m range
- **Light wind (0.5-2.0 m/s)**: Good scent transport, 50-80m range  
- **Moderate wind (2.0-5.0 m/s)**: Optimal conditions, 80-125m range
- **Strong wind (5.0-8.0 m/s)**: Some dilution, 125-155m range
- **Very strong wind (> 8.0 m/s)**: Significant dilution, decreasing range

### Fan Shape
- The polygon starts narrow at the dog's position (apex)
- Expands in the upwind direction based on scent dispersion
- Fan angle varies with wind speed:
  - Light wind: ±45° (wide dispersion)
  - Moderate wind: ±25-35° 
  - Strong wind: ±10-16° (narrow, focused cone)

## Output Schema

The wind polygon GeoPackage contains the following attributes:

- `session_id`: Rover session identifier
- `sequence`: Measurement sequence number
- `recorded_at`: Timestamp of measurement
- `wind_direction_deg`: Wind direction in degrees (0° = North)
- `wind_speed_mps`: Wind speed in meters per second
- `scent_area_m2`: Approximate scent detection area in square meters
- `max_distance_m`: Maximum scent detection distance in meters

## Usage

```bash
dotnet run
```

The program will:
1. Read rover measurements from `C:\temp\Rover1\rover_data.gpkg`
2. Generate scent polygons for each measurement
3. Save results to `C:\temp\Rover1\rover_windpolygon.gpkg`

## Visualization

Open the output GeoPackage in QGIS, ArcGIS, or other GIS software to visualize:
- The rover track (points from rover_data.gpkg)
- Scent detection areas (polygons from rover_windpolygon.gpkg)
- Wind vectors and detection zones

This is useful for search and rescue applications, environmental monitoring, and understanding scent dispersion patterns in the field.