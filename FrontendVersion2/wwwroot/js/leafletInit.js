window.initLeafletMap = async function () {
  console.log('Starting Leaflet map initialization...');
  
  // Global variables for rover tracking
  let roverLayer = null;
  let roverTrailLayer = null;
  let windArrowsLayer = null;
  let clientId = 'client_' + Math.random().toString(36).substr(2, 9);
  let updateInterval = null;
  
  try {
    // Test API connectivity first
    console.log('Testing API connectivity...');
    try {
      const testResp = await fetch('/api/test');
      if (testResp.ok) {
        const testData = await testResp.json();
        console.log('API test successful:', testData);
      } else {
        console.warn('API test failed:', testResp.status, testResp.statusText);
      }
    } catch (testError) {
      console.warn('API test exception:', testError.message);
    }

    // First, get the forest bounds to center the map
    console.log('Fetching forest bounds...');
    const boundsResp = await fetch('/api/forest-bounds');
    let initialView = [-36.718362, 174.577555]; // Auckland coordinates as fallback
    let initialZoom = 12; // fallback
    let boundsData = null;

    if (boundsResp.ok) {
      try {
        boundsData = await boundsResp.json();
        console.log('Forest bounds received:', boundsData);
        initialView = [boundsData.center.lat, boundsData.center.lng];
        
        // Calculate appropriate zoom level based on bounds size
        const latDiff = boundsData.bounds.maxLat - boundsData.bounds.minLat;
        const lngDiff = boundsData.bounds.maxLng - boundsData.bounds.minLng;
        const maxDiff = Math.max(latDiff, lngDiff);
        
        console.log('Bounds difference:', { latDiff, lngDiff, maxDiff });
        
        // Rough zoom calculation - adjust as needed
        if (maxDiff > 0.1) initialZoom = 10;
        else if (maxDiff > 0.05) initialZoom = 12;
        else if (maxDiff > 0.02) initialZoom = 14;
        else initialZoom = 15;
        
        console.log('Calculated zoom level:', initialZoom);
      } catch (jsonError) {
        console.error('Failed to parse bounds JSON:', jsonError.message);
      }
    } else {
      console.error('Failed to get forest bounds:', boundsResp.status, boundsResp.statusText);
    }

    // Initialize the map with calculated center and zoom
    console.log('Initializing map with view:', initialView, 'zoom:', initialZoom);
    const map = L.map('map').setView(initialView, initialZoom);
    
    // Add tile layer
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap contributors'
    }).addTo(map);

    console.log('Base map initialized, now loading forest polygon...');

    // Load and display the RiverHead Forest polygon
    try {
      console.log('Fetching forest polygon...');
      const forestResp = await fetch('/api/riverhead-forest');
      if (forestResp.ok) {
        try {
          const geoJson = await forestResp.json();
          console.log('Forest polygon received:', geoJson);
          
          const forestLayer = L.geoJSON(geoJson, {
            style: {
              color: '#2d5a27',      // Dark green border
              fillColor: '#4a7c59',  // Forest green fill
              weight: 2,
              opacity: 0.8,
              fillOpacity: 0.2       // More transparent to see rover points
            },
            onEachFeature: (feature, layer) => {
              const name = feature.properties?.name ?? 'Forest Area';
              const description = feature.properties?.description ?? '';
              const pointCount = feature.properties?.pointCount ?? 'Unknown';
              layer.bindPopup(`<b>${name}</b><br/>${description}<br/>Points: ${pointCount}`);
            }
          }).addTo(map);

          // Fit the map to the forest bounds with some padding if we have bounds data
          if (boundsData) {
            const bounds = [
              [boundsData.bounds.minLat, boundsData.bounds.minLng],
              [boundsData.bounds.maxLat, boundsData.bounds.maxLng]
            ];
            console.log('Fitting map to bounds:', bounds);
            map.fitBounds(bounds, { padding: [20, 20] });
          }

          console.log('RiverHead Forest polygon loaded and displayed successfully');
        } catch (jsonError) {
          console.error('Failed to parse forest polygon JSON:', jsonError.message);
        }
      } else {
        console.error('Failed to load forest polygon:', forestResp.status, forestResp.statusText);
      }
    } catch (fetchError) {
      console.error('Exception while fetching RiverHead Forest polygon:', fetchError);
    }

    // Initialize rover layers
    console.log('Initializing rover data layers...');
    
    // Layer for rover trail (all historical points)
    roverTrailLayer = L.layerGroup().addTo(map);
    
    // Layer for wind arrows
    windArrowsLayer = L.layerGroup().addTo(map);
    
    // Layer for current rover position
    roverLayer = L.layerGroup().addTo(map);

    // Load initial rover data
    await loadInitialRoverData();

    // Start real-time updates
    startRoverDataUpdates();

    // Add layer control
    const overlayMaps = {
      "Forest Boundary": forestLayer,
      "Rover Trail": roverTrailLayer,
      "Wind Arrows": windArrowsLayer,
      "Current Rover": roverLayer
    };
    
    L.control.layers(null, overlayMaps).addTo(map);

    console.log('Map initialization completed with rover tracking and wind visualization');

    // Helper function to get wind speed color
    function getWindSpeedColor(windSpeed) {
      // Color scale from blue (calm) to red (strong wind)
      if (windSpeed < 2) return '#4dabf7';      // Light blue - calm
      if (windSpeed < 4) return '#1c7ed6';      // Blue - light breeze
      if (windSpeed < 6) return '#1971c2';      // Dark blue - gentle breeze
      if (windSpeed < 8) return '#ffd43b';      // Yellow - moderate breeze
      if (windSpeed < 10) return '#ff922b';     // Orange - fresh breeze
      if (windSpeed < 12) return '#ff6b35';     // Red-orange - strong breeze
      return '#e03131';                         // Red - near gale
    }

    // Helper function to load initial rover data
    async function loadInitialRoverData() {
      try {
        console.log('Loading initial rover data...');
        const roverResp = await fetch('/api/rover-data');
        
        if (roverResp.ok) {
          const roverData = await roverResp.json();
          console.log('Initial rover data loaded:', roverData.features.length, 'points');
          
          if (roverData.features.length > 0) {
            displayRoverTrail(roverData.features);
            displayWindArrows(roverData.features);
            displayCurrentRoverPosition(roverData.features[roverData.features.length - 1]);
          }
        } else {
          console.warn('Failed to load initial rover data:', roverResp.status);
        }
      } catch (error) {
        console.error('Error loading initial rover data:', error);
      }
    }

    // Helper function to start real-time updates
    function startRoverDataUpdates() {
      console.log('Starting real-time rover data updates...');
      
      updateInterval = setInterval(async () => {
        try {
          const updatesResp = await fetch(`/api/rover-data/updates?clientId=${clientId}`);
          
          if (updatesResp.ok) {
            const updatesData = await updatesResp.json();
            
            if (updatesData.features.length > 0) {
              console.log('Received', updatesData.features.length, 'new rover points');
              
              // Add new points to trail
              addPointsToTrail(updatesData.features);
              
              // Add new wind arrows
              addWindArrows(updatesData.features);
              
              // Update current position
              const latestPoint = updatesData.features[updatesData.features.length - 1];
              displayCurrentRoverPosition(latestPoint);
            }
          } else {
            console.warn('Failed to get rover updates:', updatesResp.status);
          }
        } catch (error) {
          console.error('Error fetching rover updates:', error);
        }
      }, 2000); // Check for updates every 2 seconds
    }

    // Helper function to display rover trail
    function displayRoverTrail(features) {
      const trailPoints = features.map(f => [
        f.geometry.coordinates[1], // latitude
        f.geometry.coordinates[0]  // longitude
      ]);
      
      // Create polyline for the trail
      const trail = L.polyline(trailPoints, {
        color: '#ff6b35',
        weight: 2,
        opacity: 0.7
      });
      
      roverTrailLayer.clearLayers();
      roverTrailLayer.addLayer(trail);
      
      // Add markers for significant points (every 20th point to avoid clutter)
      features.forEach((feature, index) => {
        if (index % 20 === 0) { // Every 20th point
          const marker = L.circleMarker([
            feature.geometry.coordinates[1],
            feature.geometry.coordinates[0]
          ], {
            radius: 3,
            fillColor: '#ff6b35',
            color: '#ff6b35',
            weight: 1,
            opacity: 0.8,
            fillOpacity: 0.8
          });
          
          const props = feature.properties;
          marker.bindPopup(`
            <b>Rover Measurement</b><br/>
            Sequence: ${props.sequence}<br/>
            Time: ${new Date(props.recordedAt).toLocaleString()}<br/>
            Wind: ${props.windSpeedMps.toFixed(1)} m/s @ ${props.windDirectionDeg}°
          `);
          
          roverTrailLayer.addLayer(marker);
        }
      });
    }

    // Helper function to display wind arrows for all points - COMPLETELY NEW APPROACH
    function displayWindArrows(features) {
      windArrowsLayer.clearLayers();
      
      // Show wind arrows for every 15th point to avoid overcrowding
      features.forEach((feature, index) => {
        if (index % 15 === 0) { // Every 15th point for better spacing
          addWindArrow(feature);
        }
      });
    }

    // Helper function to add wind arrows for new points
    function addWindArrows(newFeatures) {
      newFeatures.forEach(feature => {
        addWindArrow(feature);
      });
    }

    // Helper function to add a single wind arrow - COMPLETELY NEW APPROACH using polylines
    function addWindArrow(feature) {
      const props = feature.properties;
      const windSpeed = props.windSpeedMps;
      const windDirection = props.windDirectionDeg;
      const lat = feature.geometry.coordinates[1];
      const lng = feature.geometry.coordinates[0];
      
      // Skip if wind speed is very low
      if (windSpeed < 0.5) return;
      
      const color = getWindSpeedColor(windSpeed);
      
      // Calculate arrow length in meters (will be converted to map units)
      const arrowLengthMeters = Math.max(20, Math.min(100, windSpeed * 8)); // 20-100 meters
      
      // Convert wind direction to radians (add 180° to show where wind is going)
      const angleRad = ((windDirection + 180) % 360) * Math.PI / 180;
      
      // Calculate end point using approximate lat/lng conversion
      // 1 degree latitude ? 111,320 meters
      // 1 degree longitude ? 111,320 * cos(latitude) meters
      const latOffset = (arrowLengthMeters * Math.sin(angleRad)) / 111320;
      const lngOffset = (arrowLengthMeters * Math.cos(angleRad)) / (111320 * Math.cos(lat * Math.PI / 180));
      
      const endLat = lat + latOffset;
      const endLng = lng + lngOffset;
      
      // Create arrow shaft as a polyline
      const arrowShaft = L.polyline([[lat, lng], [endLat, endLng]], {
        color: color,
        weight: 3,
        opacity: 0.9
      });
      
      // Create arrowhead using a small triangle polygon
      const headLength = arrowLengthMeters * 0.3; // 30% of shaft length
      const headAngle = 25 * Math.PI / 180; // 25 degree angle for arrowhead
      
      // Calculate arrowhead points
      const backAngle1 = angleRad + Math.PI - headAngle;
      const backAngle2 = angleRad + Math.PI + headAngle;
      
      const head1LatOffset = (headLength * Math.sin(backAngle1)) / 111320;
      const head1LngOffset = (headLength * Math.cos(backAngle1)) / (111320 * Math.cos(endLat * Math.PI / 180));
      const head2LatOffset = (headLength * Math.sin(backAngle2)) / 111320;
      const head2LngOffset = (headLength * Math.cos(backAngle2)) / (111320 * Math.cos(endLat * Math.PI / 180));
      
      const arrowHead = L.polygon([
        [endLat, endLng],
        [endLat + head1LatOffset, endLng + head1LngOffset],
        [endLat + head2LatOffset, endLng + head2LngOffset]
      ], {
        color: color,
        fillColor: color,
        weight: 1,
        opacity: 0.9,
        fillOpacity: 0.9
      });
      
      // Create a center point marker
      const centerPoint = L.circleMarker([lat, lng], {
        radius: 2,
        fillColor: 'white',
        color: color,
        weight: 1,
        opacity: 1,
        fillOpacity: 1
      });
      
      // Group all arrow parts together
      const arrowGroup = L.layerGroup([arrowShaft, arrowHead, centerPoint]);
      
      // Add popup to the entire arrow group
      arrowGroup.bindPopup(`
        <b>Wind Measurement</b><br/>
        Speed: ${windSpeed.toFixed(1)} m/s<br/>
        Direction: ${windDirection}° (${getWindDirectionName(windDirection)})<br/>
        Time: ${new Date(props.recordedAt).toLocaleString()}<br/>
        Sequence: ${props.sequence}
      `);
      
      windArrowsLayer.addLayer(arrowGroup);
    }

    // Helper function to get wind direction name
    function getWindDirectionName(degrees) {
      const directions = ['N', 'NNE', 'NE', 'ENE', 'E', 'ESE', 'SE', 'SSE', 'S', 'SSW', 'SW', 'WSW', 'W', 'WNW', 'NW', 'NNW'];
      const index = Math.round(degrees / 22.5) % 16;
      return directions[index];
    }

    // Helper function to add new points to trail
    function addPointsToTrail(newFeatures) {
      newFeatures.forEach(feature => {
        const marker = L.circleMarker([
          feature.geometry.coordinates[1],
          feature.geometry.coordinates[0]
        ], {
          radius: 3,
          fillColor: '#ff6b35',
          color: '#ff6b35',
          weight: 1,
          opacity: 0.8,
          fillOpacity: 0.8
        });
        
        const props = feature.properties;
        marker.bindPopup(`
          <b>Rover Measurement</b><br/>
          Sequence: ${props.sequence}<br/>
          Time: ${new Date(props.recordedAt).toLocaleString()}<br/>
          Wind: ${props.windSpeedMps.toFixed(1)} m/s @ ${props.windDirectionDeg}°
        `);
        
        roverTrailLayer.addLayer(marker);
      });
    }

    // Helper function to display current rover position - UPDATED to use polyline arrows
    function displayCurrentRoverPosition(feature) {
      if (!feature) return;
      
      roverLayer.clearLayers();
      
      // Create a larger, more prominent marker for current position
      const currentMarker = L.marker([
        feature.geometry.coordinates[1],
        feature.geometry.coordinates[0]
      ], {
        icon: L.divIcon({
          className: 'rover-current-icon',
          html: '<div style="background-color: red; width: 16px; height: 16px; border-radius: 50%; border: 3px solid white; box-shadow: 0 0 8px rgba(0,0,0,0.7);"></div>',
          iconSize: [22, 22],
          iconAnchor: [11, 11] // Center the icon
        })
      });
      
      const props = feature.properties;
      currentMarker.bindPopup(`
        <b>Current Rover Position</b><br/>
        Sequence: ${props.sequence}<br/>
        Time: ${new Date(props.recordedAt).toLocaleString()}<br/>
        Location: ${feature.geometry.coordinates[1].toFixed(6)}, ${feature.geometry.coordinates[0].toFixed(6)}<br/>
        Wind: ${props.windSpeedMps.toFixed(1)} m/s @ ${props.windDirectionDeg}° (${getWindDirectionName(props.windDirectionDeg)})
      `).openPopup();
      
      roverLayer.addLayer(currentMarker);
      
      // Add current wind arrow using the same polyline approach
      if (props.windSpeedMps >= 0.5) {
        const lat = feature.geometry.coordinates[1];
        const lng = feature.geometry.coordinates[0];
        const windSpeed = props.windSpeedMps;
        const windDirection = props.windDirectionDeg;
        const color = '#dc2626'; // Red for current position
        
        // Larger arrow for current position
        const arrowLengthMeters = Math.max(30, Math.min(120, windSpeed * 10)); // 30-120 meters
        
        const angleRad = ((windDirection + 180) % 360) * Math.PI / 180;
        
        const latOffset = (arrowLengthMeters * Math.sin(angleRad)) / 111320;
        const lngOffset = (arrowLengthMeters * Math.cos(angleRad)) / (111320 * Math.cos(lat * Math.PI / 180));
        
        const endLat = lat + latOffset;
        const endLng = lng + lngOffset;
        
        // Current arrow shaft
        const currentArrowShaft = L.polyline([[lat, lng], [endLat, endLng]], {
          color: color,
          weight: 4,
          opacity: 1
        });
        
        // Current arrowhead
        const headLength = arrowLengthMeters * 0.35;
        const headAngle = 25 * Math.PI / 180;
        
        const backAngle1 = angleRad + Math.PI - headAngle;
        const backAngle2 = angleRad + Math.PI + headAngle;
        
        const head1LatOffset = (headLength * Math.sin(backAngle1)) / 111320;
        const head1LngOffset = (headLength * Math.cos(backAngle1)) / (111320 * Math.cos(endLat * Math.PI / 180));
        const head2LatOffset = (headLength * Math.sin(backAngle2)) / 111320;
        const head2LngOffset = (headLength * Math.cos(backAngle2)) / (111320 * Math.cos(endLat * Math.PI / 180));
        
        const currentArrowHead = L.polygon([
          [endLat, endLng],
          [endLat + head1LatOffset, endLng + head1LngOffset],
          [endLat + head2LatOffset, endLng + head2LngOffset]
        ], {
          color: color,
          fillColor: color,
          weight: 2,
          opacity: 1,
          fillOpacity: 1
        });
        
        // Current center point
        const currentCenterPoint = L.circleMarker([lat, lng], {
          radius: 3,
          fillColor: 'white',
          color: color,
          weight: 2,
          opacity: 1,
          fillOpacity: 1
        });
        
        const currentArrowGroup = L.layerGroup([currentArrowShaft, currentArrowHead, currentCenterPoint]);
        
        currentArrowGroup.bindPopup(`
          <b>Current Wind Conditions</b><br/>
          Speed: ${props.windSpeedMps.toFixed(1)} m/s<br/>
          Direction: ${props.windDirectionDeg}° (${getWindDirectionName(props.windDirectionDeg)})<br/>
          Time: ${new Date(props.recordedAt).toLocaleString()}
        `);
        
        roverLayer.addLayer(currentArrowGroup);
      }
    }

    // Cleanup function
    window.addEventListener('beforeunload', () => {
      if (updateInterval) {
        clearInterval(updateInterval);
      }
    });

  } catch (e) {
    console.error('Failed to initialize map:', e);
    
    // Try to create a basic map as fallback
    try {
      console.log('Attempting fallback map initialization...');
      const map = L.map('map').setView([-36.718362, 174.577555], 12);
      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; OpenStreetMap contributors'
      }).addTo(map);
      
      L.popup()
        .setLatLng([-36.718362, 174.577555])
        .setContent(`<b>Map Error</b><br/>Failed to load forest data<br/>Error: ${e.message}`)
        .openOn(map);
        
      console.log('Fallback map created');
    } catch (fallbackError) {
      console.error('Even fallback map failed:', fallbackError);
    }
  }
}
