# 🌲 RiverHead Forest Rover Tracker (Learning Version)

A simplified Blazor application for learning geospatial web development concepts.

## 🎓 What You'll Learn

### Frontend Technologies
- **Blazor Server**: Building interactive web UIs with C#
- **JavaScript Interop**: Calling JavaScript functions from C#  
- **Leaflet.js**: Creating interactive web maps
- **Real-time Updates**: Refreshing data automatically

### Geospatial Concepts
- **GeoJSON**: Standard format for geographic data
- **Polygons**: Representing areas on maps
- **Wind Direction**: How wind affects scent detection
- **Coordinate Systems**: Working with latitude/longitude

## 🚀 Getting Started

### Prerequisites
1. .NET 9 SDK
2. Either:
   - PostgreSQL database with rover data, OR
   - GeoPackage files in `C:\temp\Rover1\`

### Running the Application
```bash
cd FrontendVersion2
dotnet run
```

Then open your browser to `https://localhost:5001`

## 🗺️ Understanding the Map

### Map Layers (from background to foreground):
1. **🌲 Forest Boundary** (Green) - The operational area
2. **🐕 Combined Coverage** (Tan, dashed) - Total scent detection area  
3. **💨 Wind Polygons** (Colored) - Individual scent zones
4. **📍 Rover Trail** (Colored dots) - Rover movement path

### Color Coding by Wind Speed:
- **Light Blue**: 0-2 m/s (Calm)
- **Blue**: 2-4 m/s (Light breeze)
- **Yellow**: 4-8 m/s (Moderate)
- **Orange**: 8-10 m/s (Fresh breeze)
- **Red**: 10+ m/s (Strong wind)

## 📁 Key Files

### Backend (C#)
- `Program.cs` - Main application setup and API endpoints
- `Pages/Index.razor` - Main UI page

### Frontend (JavaScript)
- `wwwroot/js/leafletInit.js` - Map initialization and data loading

## 🔄 Data Flow

1. **Rover** → Collects GPS + wind measurements
2. **Database** → Stores measurement data
3. **ScentPolygonService** → Calculates scent detection areas
4. **API Endpoints** → Serve data as GeoJSON
5. **Leaflet Map** → Visualizes data with interactive layers

## 🛠️ Key Concepts Demonstrated

### Blazor Server
- Server-side rendering with C#
- JavaScript interop for map initialization
- Real-time updates without page refreshes

### Geospatial Processing
- Converting rover measurements to scent polygons
- Combining individual polygons into unified coverage
- Proper handling of coordinate systems (WGS84/EPSG:4326)

### Web Mapping
- Loading different data types as map layers
- Styling features based on attributes
- Interactive popups with contextual information

## 🎯 Learning Exercises

1. **Modify Colors**: Change the wind speed color scheme
2. **Add Tooltips**: Show information on hover instead of click
3. **Filter Data**: Add controls to show/hide certain measurements
4. **Animation**: Animate the rover's movement over time
5. **Export Data**: Add buttons to download GeoJSON data

## 🔧 Troubleshooting

### Map Not Loading
- Check browser console (F12) for JavaScript errors
- Ensure Leaflet.js is loading from CDN
- Verify API endpoints return data

### No Data Visible
- Check if RoverSimulator has generated data
- Verify database connection
- Look for errors in application logs

### Performance Issues  
- Reduce the number of polygons displayed
- Increase the refresh interval
- Use data pagination for large datasets

## 📚 Next Steps

Once comfortable with this simplified version, explore:
- Adding authentication and user management
- Implementing real-time SignalR connections
- Creating mobile-responsive designs
- Adding data export capabilities
- Building production deployment pipelines

## 🤝 Contributing

This is a learning project! Feel free to:
- Add comments explaining complex concepts
- Create additional example exercises
- Improve error messages and user feedback
- Add more detailed documentation
