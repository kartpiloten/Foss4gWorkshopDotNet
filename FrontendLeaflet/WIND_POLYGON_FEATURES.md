# Wind Polygon Visualization Features (Leaflet)

## Overview
The FrontendLeaflet project renders a clean, high‑level view of the rover activity and scent coverage. 
It focuses on a single, unified coverage polygon (no individual wind polygons) for clarity and performance.

## Current Layers (Back to Front)
1. Base Map (OpenStreetMap tiles)
2. Forest Boundary (Green stroke, light green fill)
3. Combined Coverage (Unified scent polygon; tan/beige fill, brown dashed stroke)
4. Rover Trail (Single LineString; orange stroke)
5. Current Rover Position (Red circle marker with permanent wind label)
6. Overlay UI (top‑left) with a CircleDiagram showing forest coverage percentage

## API Endpoints Used
- GET `/api/forest` — RiverHeadForest boundary polygon (GeoPackage)
- GET `/api/forest-bounds` — Center + bounds for initial map view
- GET `/api/rover-trail?limit=500` — Rover trail as a single LineString (server limits as needed)
- GET `/api/rover-stats` — Latest rover position and wind data
- GET `/api/combined-coverage` — Unified scent coverage polygon (union of all scent polygons)
- GET `/api/coverage-stats` — Coverage statistics (percentage of forest covered + area details)

## Coverage Statistics (CircleDiagram)
- The `CircleDiagram` Blazor component overlays the map (below the header) and shows:
  - Coverage percent (center text)
  - Forest area (ha)
  - Scent area (ha)
- Update cadence: every 2 seconds (uses a cached calculator service for efficiency).
- Backed by `ScentPolygonLibrary.ForestCoverageCalculator`, which:
  - Loads and caches the RiverHeadForest polygon once (from GeoPackage)
  - Uses the unified scent polygon from `ScentPolygonService`
  - Recomputes only when the unified polygon version changes

## Update Intervals
- Map JS (Leaflet): trail, coverage, and latest position refresh every 8 seconds
- CircleDiagram (Blazor): refresh every 2 seconds via dependency‑injected calculator

## Data Requirements
- Rover measurements available (via `ReadRoverDBStubLibrary`)
- `ScentPolygonService` running to produce the unified scent polygon
- GeoPackage `Solutionresources/RiverHeadForest.gpkg` present and readable (EPSG:4326)

## Technical Implementation
- Frontend: Blazor Server + Leaflet.js
- Geometry/Spatial:
  - `ScentPolygonLibrary` for scent polygon generation and unification
  - `NetTopologySuite` (intersection/area calculations)
  - `MapPiloteGeopackageHelper` (GeoPackage read, async APIs)
- Minimal APIs in `FrontendLeaflet/Program.cs` expose GeoJSON + stats
- Client JS in `wwwroot/js/leafletInit.js` initializes layers and schedules refreshes
- UI overlay styling in `wwwroot/css/fullscreen-map.css`

## Usage
1. Run Rover data source (e.g., simulator) so measurements are available
2. Start FrontendLeaflet (`dotnet run` in the project directory)
3. Open the app and verify:
   - Forest boundary and combined coverage are visible
   - Trail and current position update

## Troubleshooting
- Map not visible:
  - Check browser console for JS errors
  - Ensure `js/leafletInit.js` is served and `initLeafletMap` exists
- Coverage stuck at 0%:
  - Verify `ScentPolygonService` logs show unified version increments
  - Confirm `RiverHeadForest.gpkg` exists in `Solutionresources/`
- Performance:
  - The app displays a single unified polygon to avoid clutter and keep rendering fast
  - Sampling/limits are applied server‑side for the trail endpoint

## File Pointers
- `FrontendLeaflet/Program.cs` — Minimal APIs + service registrations (scent service, coverage calculator)
- `FrontendLeaflet/Pages/Index.razor` — Map host, overlay UI, CircleDiagram placement
- `FrontendLeaflet/wwwroot/js/leafletInit.js` — Leaflet init + periodic refresh logic
- `FrontendLeaflet/Components/CircleDiagram.razor` — Coverage pie visualization (Blazor)
- `ScentPolygonLibrary/ForestCoverageCalculator.cs` — Cached coverage stats (intersection/areas)