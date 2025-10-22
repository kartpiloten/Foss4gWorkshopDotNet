# Wind Polygon Visualization Features (OpenLayers)

## Overview
The FrontendOpenLayers project includes comprehensive wind polygon visualization 
using OpenLayers with the following layers:

### 1. Combined Coverage Layer
- **API Endpoint**: `/api/combined-coverage`
- **Description**: Shows the unified scent detection area combining all rover measurements
- **Styling**: Tan/beige fill with brown dashed border, semi-transparent (20% opacity)
- **Layer Type**: Vector layer with Polygon geometry
- **Z-Index**: 2 (above forest, below trail)

### 2. Rover Trail (LineString)
- **API Endpoint**: `/api/rover-trail`
- **Description**: Continuous path showing rover movement over time
- **Styling**: Orange line (4px width, 85% opacity)
- **Layer Type**: Vector layer with LineString geometry
- **Z-Index**: 3
- **Updates**: Dynamically appends new points every 8 seconds

### 3. Current Position Marker
- **API Endpoint**: `/api/rover-stats`
- **Description**: Latest rover location with wind information
- **Styling**: Red circle marker with permanent wind speed/direction label
- **Layer Type**: Vector layer with Point geometry
- **Z-Index**: 4 (topmost)
- **Interactive**: Click to show popup with details

## Layer Ordering (Back to Front)
1. **Base Map** (OpenStreetMap tiles) - Background
2. **Forest Boundary** (Green) - Operational area
3. **Combined Coverage** (Tan/Beige) - Total scent detection area
4. **Rover Trail** (Orange line) - Historical path
5. **Current Position** (Red marker) - Live location with label

## OpenLayers Implementation Details

### Vector Layers
Each layer uses OpenLayers Vector layers with dedicated sources:
```javascript
const layer = new ol.layer.Vector({
    source: new ol.source.Vector(),
    style: new ol.style.Style({ /* styling */ }),
    zIndex: 2
});
```

### Coordinate Projection
All coordinates are transformed from EPSG:4326 (lat/lng) to EPSG:3857 (Web Mercator):
```javascript
const coords = ol.proj.fromLonLat([lng, lat]);
```

### Feature Updates
Trail updates use geometry modification:
```javascript
roverTrailFeature.getGeometry().setCoordinates(roverTrailCoords);
```

## Data Requirements
- Rover measurement data in PostgreSQL database or GeoPackage files
- `ScentPolygonService` running to generate unified coverage polygons
- Forest boundary data in `Solutionresources/RiverHeadForest.gpkg`
- Optional: Turf.js for client-side intersection area calculations (see Coverage Metrics section)

## Features
- **Real-time Updates**: Map updates every 8 seconds with new data
- **Persistent Trail**: Trail grows continuously without trimming old points
- **Interactive Markers**: Click current position for detailed information
- **Permanent Labels**: Wind speed and direction always visible on current position
- **Vector Rendering**: Efficient rendering of complex geometries
- **Coordinate Projection**: Proper handling of geographic projections
- **Coverage Metrics Panel (optional)**: Show areas for Unified (scent), Forest, and Intersection in m²

## Technical Implementation

### Stack
- **Backend**: ASP.NET Core with Blazor Server
- **Frontend**: OpenLayers 9.2.4 for advanced mapping
- **Data Format**: GeoJSON for web compatibility
- **Spatial Processing**: NetTopologySuite for geometry operations (server)
- **Projections**: EPSG:4326 (input) ? EPSG:3857 (display)

### API Endpoints
1. `/api/forest` - Forest boundary polygon
2. `/api/forest-bounds` - Map centering coordinates
3. `/api/rover-trail` - Historical rover path as LineString
4. `/api/rover-stats` - Latest rover position and statistics
5. `/api/combined-coverage` - Unified scent detection polygon

## Coverage Metrics (Areas and Intersection)
The backend tester now logs coverage sizes to the console whenever the unified scent polygon changes:
- Unified (scent) area in m²
- RiverHead forest area in m²
- Intersection (Unified ? Forest) area in m²

You can optionally display the same metrics in the OpenLayers frontend.

### Option A: Compute on the client with Turf.js
- Include Turf.js in `Pages/_Host.cshtml` (or the corresponding host page):
```html
<script src="https://cdn.jsdelivr.net/npm/@turf/turf@6/turf.min.js"></script>
```

- In `wwwroot/js/openlayersInit.js`, after you load both the forest and the combined coverage GeoJSON features:
```javascript
// Assume you have GeoJSON Feature objects for forestFeature and coverageFeature
// Areas in m² using Turf
const forestAreaM2 = turf.area(forestFeature);
const unifiedAreaM2 = turf.area(coverageFeature);

// Intersection using Turf
const intersection = turf.intersect(forestFeature, coverageFeature);
const intersectionAreaM2 = intersection ? turf.area(intersection) : 0;

// Render to a simple on-map panel
const el = document.getElementById('coverage-stats');
if (el) {
  el.innerText = `Unified: ${unifiedAreaM2.toLocaleString()} m²\n` +
                 `Forest: ${forestAreaM2.toLocaleString()} m²\n` +
                 `Intersection: ${intersectionAreaM2.toLocaleString()} m²`;
}
```

- Add a small panel to `Pages/Index.razor` (or a component) to display the values:
```html
<div id="coverage-stats" style="position:absolute; right:12px; bottom:12px; background:#fff; padding:8px 12px; border-radius:6px; box-shadow: 0 2px 6px rgba(0,0,0,.2); font-family: system-ui; font-size: 13px; white-space: pre-line;">
  Loading coverage metrics…
</div>
```

- Update the panel whenever you refresh the layers (e.g., inside the same interval that calls `loadCombinedCoverage(...)`).

### Option B: Compute areas with OpenLayers Sphere utilities (no intersection)
If you only need raw areas (not intersection), you can use OpenLayers sphere utilities:
```javascript
import {getArea as getAreaSphere} from 'ol/sphere';

// geometry is an ol.geom.Polygon in EPSG:3857 (Web Mercator)
const areaM2 = Math.abs(getAreaSphere(geometry));
```
Note: Client-side intersection between polygons is easiest with Turf.js. If you prefer server-side, expose an API that returns the three numbers.

## OpenLayers Advantages

### Compared to Leaflet
- • **Better Vector Performance**: More efficient with large datasets
- • **Built-in Projections**: Automatic coordinate transformation
- • **Advanced Styling**: Text labels, complex styles, z-index control
- • **Feature Management**: Better control over feature lifecycle
- • **GIS-Ready**: Professional-grade mapping capabilities

### Styling Features
- Stroke styles with custom widths and colors
- Fill styles with opacity control
- Line dash patterns for dashed borders
- Text styles for permanent labels
- Circle markers with custom radius and colors

## Usage Instructions
1. **Start RoverSimulator** to generate measurement data
2. **Start FrontendOpenLayers** to view the visualization:
   ```bash
   cd FrontendOpenLayers
   dotnet run
   ```
3. **Open Browser** to `https://localhost:5009`
4. **Observe** real-time trail updates and coverage changes
5. **Click** on current position marker for detailed information
6. Optional: **Enable Coverage Metrics Panel** by adding Turf.js and the `coverage-stats` element as shown above

## Customization Examples

### Change Trail Color
In `wwwroot/js/openlayersInit.js`, modify the rover trail layer style:
```javascript
const roverTrailLayer = new ol.layer.Vector({
    source: roverTrailSource,
    style: new ol.style.Style({
        stroke: new ol.style.Stroke({
            color: '#00ff00',  // Change to green
            width: 6           // Make thicker
        })
    }),
    zIndex: 3
});
```

### Adjust Update Interval
Change the refresh rate (in milliseconds):
```javascript
setInterval(async () => {
    await appendNewTrailPoints();
    await loadLatestPosition(currentPositionSource, popupOverlay, popup);
    await loadCombinedCoverage(coverageSource);
    // Also recompute coverage metrics here if enabled
}, 5000); // Update every 5 seconds instead of 8
```

### Modify Coverage Style
Adjust the combined coverage appearance:
```javascript
const coverageLayer = new ol.layer.Vector({
    source: coverageSource,
    style: new ol.style.Style({
        stroke: new ol.style.Stroke({
            color: '#ff0000',  // Red border
            width: 3,
            lineDash: [10, 5]  // Different dash pattern
        }),
        fill: new ol.style.Fill({
            color: 'rgba(255, 0, 0, 0.3)'  // Red fill, 30% opacity
        })
    }),
    zIndex: 2
});
```

## File Structure
- `Program.cs` - API endpoints and service configuration
- `Pages/Index.razor` - Blazor component with map initialization
- `Pages/_Host.cshtml` - HTML page with OpenLayers CDN references
- `wwwroot/js/openlayersInit.js` - OpenLayers map logic
- `wwwroot/css/fullscreen-map.css` - Fullscreen map styling

## Troubleshooting

### Map Not Rendering
- Check browser console for OpenLayers load errors
- Verify `ol` object is available globally
- Ensure `openlayersInit.js` is loaded after OpenLayers library

### Trail Not Updating
- Check API endpoint `/api/rover-trail` returns valid GeoJSON
- Verify RoverSimulator is generating new data
- Look for JavaScript errors in browser console

### Projection Issues
- Ensure all coordinates use `ol.proj.fromLonLat([lng, lat])`
- Verify GeoJSON uses EPSG:4326 standard
- Check featureProjection is set to 'EPSG:3857' when reading features

### Performance Problems
- Reduce trail limit in API call (`/api/rover-trail?limit=500`)
- Increase update interval (currently 8000ms)
- Consider feature simplification for very large datasets

### Coverage Metrics Not Visible
- Ensure Turf.js script tag is included if using client-side intersection
- Confirm that both forest and combined coverage features are loaded before computing metrics
- Log intermediate results (areas, intersect presence) to the browser console for debugging