# 🌲 FrontendOpenLayers Rover Tracker

A Blazor application demonstrating geospatial web development with OpenLayers.

## 🎓 What You'll Learn

### Frontend Technologies
- **Blazor Server**: Building interactive web UIs with C#
- **JavaScript Interop**: Calling JavaScript functions from C#  
- **OpenLayers**: Creating interactive web maps with vector layers
- **Real-time Updates**: Refreshing data automatically

### Geospatial Concepts
- **GeoJSON**: Standard format for geographic data
- **Polygons**: Representing areas on maps
- **Wind Direction**: How wind affects scent detection
- **Coordinate Systems**: Working with latitude/longitude and projections
- **Vector Layers**: Rendering geospatial features efficiently

## 🚀 Getting Started

### Prerequisites
1. .NET 9 SDK
2. Either:
   - PostgreSQL database with rover data, OR
   - GeoPackage files in `C:\temp\Rover1\`

### Running the Application
```bash
cd FrontendOpenLayers
dotnet run
```

Then open your browser to `https://localhost:5009`

## 🗺️ Understanding the Map

### Map Layers (from background to foreground):
1. **🗺️ Base Map** (OpenStreetMap) - Background tile layer
2. **🌲 Forest Boundary** (Green) - The operational area
3. **🐕 Combined Coverage** (Tan, dashed) - Total scent detection area  
4. **🛤️ Rover Trail** (Orange line) - Continuous rover movement path
5. **📍 Current Position** (Red marker) - Latest rover location with wind info

### Wind Speed Display:
The current rover position shows real-time wind information with a permanent label displaying wind speed and direction.

## 📁 Key Files

### Backend (C#)
- `Program.cs` - Main application setup and API endpoints
- `Pages/Index.razor` - Main UI page with OpenLayers initialization
- `Pages/_Host.cshtml` - HTML host page with OpenLayers CDN references

### Frontend (JavaScript)
- `wwwroot/js/openlayersInit.js` - OpenLayers map initialization and data loading
- `wwwroot/css/fullscreen-map.css` - Fullscreen map styling

## 🔄 Data Flow

1. **Rover** → Collects GPS + wind measurements
2. **Database** → Stores measurement data
3. **ScentPolygonService** → Calculates scent detection areas
4. **API Endpoints** → Serve data as GeoJSON
5. **OpenLayers Map** → Visualizes data with vector layers and features

## 🛠️ Key Concepts Demonstrated

### Blazor Server
- Server-side rendering with C#
- JavaScript interop for map initialization
- Real-time updates without page refreshes

### OpenLayers Features
- Vector layers with custom styling
- GeoJSON feature loading and parsing
- Coordinate projection (EPSG:4326 → EPSG:3857)
- Feature overlays and popups
- Permanent labels on features

### Geospatial Processing
- Converting rover measurements to scent polygons
- Combining individual polygons into unified coverage
- Proper handling of coordinate systems and projections
- LineString geometry for continuous trails

## 🎯 Learning Exercises

1. **Modify Styles**: Change the trail color or coverage area styling
2. **Add Interactions**: Implement click handlers for different features
3. **Filter Data**: Add controls to show/hide certain layers
4. **Animation**: Animate the rover's movement over time
5. **Export Data**: Add buttons to download GeoJSON data
6. **Custom Controls**: Add OpenLayers custom controls to the map

## 🔧 Troubleshooting

### Map Not Loading
- Check browser console (F12) for JavaScript errors
- Ensure OpenLayers is loading from CDN (check for `ol` object)
- Verify `openlayersInit.js` is loaded
- Check that `initOpenLayersMap` function is available

### No Data Visible
- Check if RoverSimulator has generated data
- Verify database connection in console output
- Look for errors in application logs
- Check API endpoints return valid GeoJSON

### Performance Issues  
- Reduce the trail limit parameter in API calls
- Increase the refresh interval (currently 8 seconds)
- Use vector tiles for large datasets
- Consider clustering for many point features

### Projection Errors
- Ensure coordinates are properly transformed to EPSG:3857
- Check that GeoJSON uses EPSG:4326 (WGS84)
- Verify `ol.proj.fromLonLat()` is used correctly

## 📚 OpenLayers vs Leaflet

This project uses OpenLayers instead of Leaflet because:
- **Better vector rendering**: More efficient with large datasets
- **Advanced projections**: Built-in support for coordinate transformations
- **Feature-rich**: More control over styling and interactions
- **Enterprise-ready**: Better suited for complex GIS applications

## 🤝 Contributing

This is a learning project! Feel free to:
- Add comments explaining OpenLayers concepts
- Create additional example exercises
- Improve error messages and user feedback
- Add more detailed documentation about map projections

## 🔗 Useful Resources

- [OpenLayers Documentation](https://openlayers.org/doc/)
- [OpenLayers Examples](https://openlayers.org/en/latest/examples/)
- [GeoJSON Specification](https://datatracker.ietf.org/doc/html/rfc7946)
- [EPSG Projections](https://epsg.io/)
