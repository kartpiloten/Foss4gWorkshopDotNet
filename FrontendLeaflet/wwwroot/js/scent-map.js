/* The functionallity in this file is:
 - Provide a clean JavaScript module for Leaflet map operations
 - Use reusable layers to avoid recreating geometries on each update
 - Accept binary float arrays from Blazor for efficient data transfer
 - Support incremental trail updates (append) for performance
*/

// Global map storage (keyed by map ID)
const maps = {};

/**
 * Initialize a Leaflet map with OpenStreetMap tiles and forest boundary
 * @param {string} id - The DOM element ID for the map container
 */
export async function initMap(id) {
    console.log(`Initializing map: ${id}`);
    
    let mapCenter = [-36.718362, 174.577555];
    let mapZoom = 12;

    // Try to get forest bounds for better centering
    try {
        const boundsResponse = await fetch('/api/forest-bounds');
        if (boundsResponse.ok) {
            const bounds = await boundsResponse.json();
            mapCenter = [bounds.center.lat, bounds.center.lng];
            mapZoom = 14;
        }
    } catch (error) {
        console.log('Using default map center');
    }

    // Create the Leaflet map
    const map = L.map(id, { zoomControl: false }).setView(mapCenter, mapZoom);
    
    // Add zoom control in bottom left
    L.control.zoom({ position: 'bottomleft' }).addTo(map);
    
    // Add OpenStreetMap tiles
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '© OpenStreetMap'
    }).addTo(map);

    // Create reusable layers
    const forestLayer = L.layerGroup().addTo(map);
    const trailLayer = L.layerGroup().addTo(map);
    const coverageLayer = L.layerGroup().addTo(map);
    const positionLayer = L.layerGroup().addTo(map);

    // Store map and layers for reuse
    maps[id] = {
        map,
        forestLayer,
        trailLayer,
        coverageLayer,
        positionLayer,
        trailPolyline: null,
        trailCoords: [],
        coveragePolygon: null
    };

    // Load forest boundary once
    await loadForestBoundary(id);
    
    console.log(`Map ${id} initialized`);
}

/**
 * Load forest boundary from API (called once during initialization)
 */
async function loadForestBoundary(id) {
    const mapData = maps[id];
    if (!mapData) return;

    try {
        const response = await fetch('/api/forest');
        if (!response.ok) return;
        
        const geoJson = await response.json();
        
        L.geoJSON(geoJson, {
            style: {
                color: '#2d5a27',
                fillColor: '#4a7c59',
                weight: 2,
                opacity: 0.8,
                fillOpacity: 0.15
            }
        }).addTo(mapData.forestLayer);
        
        console.log('Forest boundary loaded');
    } catch (error) {
        console.log('Could not load forest boundary:', error.message);
    }
}

/**
 * Update the complete rover trail (replaces existing)
 * @param {string} id - Map ID
 * @param {Float32Array} floatArray - Flattened coordinate array [lng1, lat1, lng2, lat2, ...]
 */
export function updateTrail(id, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    // Convert flat float array to Leaflet [lat, lng] pairs
    const coords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        coords.push([floatArray[i + 1], floatArray[i]]); // [lat, lng] from [lng, lat]
    }

    mapData.trailCoords = coords;

    // Update or create polyline
    if (mapData.trailPolyline) {
        mapData.trailPolyline.setLatLngs(coords);
    } else {
        mapData.trailPolyline = L.polyline(coords, {
            color: '#ffb347',
            weight: 4,
            opacity: 0.85
        }).addTo(mapData.trailLayer);
    }

    console.log(`Trail updated: ${coords.length} points`);
}

/**
 * Append new coordinates to existing trail (incremental update)
 * @param {string} id - Map ID
 * @param {Float32Array} floatArray - New coordinates to append
 */
export function appendTrail(id, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    // Convert flat float array to Leaflet [lat, lng] pairs
    const newCoords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        newCoords.push([floatArray[i + 1], floatArray[i]]); // [lat, lng] from [lng, lat]
    }

    // Append to existing coordinates
    mapData.trailCoords.push(...newCoords);

    // Update polyline
    if (mapData.trailPolyline) {
        mapData.trailPolyline.setLatLngs(mapData.trailCoords);
    } else {
        mapData.trailPolyline = L.polyline(mapData.trailCoords, {
            color: '#ffb347',
            weight: 4,
            opacity: 0.85
        }).addTo(mapData.trailLayer);
    }

    console.log(`Trail appended: ${newCoords.length} new points (total: ${mapData.trailCoords.length})`);
}

/**
 * Update the coverage polygon (replaces existing)
 * @param {string} id - Map ID
 * @param {Float32Array} floatArray - Polygon exterior ring coordinates
 */
export function updateCoverage(id, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    // Convert flat float array to Leaflet [lat, lng] pairs
    const coords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        coords.push([floatArray[i + 1], floatArray[i]]); // [lat, lng] from [lng, lat]
    }

    // Update or create polygon
    if (mapData.coveragePolygon) {
        mapData.coveragePolygon.setLatLngs([coords]); // Leaflet polygon expects array of rings
    } else {
        mapData.coveragePolygon = L.polygon([coords], {
            color: '#8B4513',
            fillColor: '#DEB887',
            weight: 2,
            opacity: 0.8,
            fillOpacity: 0.20,
            dashArray: '8,4'
        }).addTo(mapData.coverageLayer);
    }

    console.log(`Coverage updated: ${coords.length} points`);
}

/**
 * Update the rover position marker with wind information
 * @param {string} id - Map ID
 * @param {number} lng - Longitude
 * @param {number} lat - Latitude
 * @param {number} windSpeed - Wind speed in m/s
 * @param {number} windDirection - Wind direction in degrees
 */
export function updatePosition(id, lng, lat, windSpeed, windDirection) {
    const mapData = maps[id];
    if (!mapData) return;

    // Clear previous position
    mapData.positionLayer.clearLayers();

    // Create marker
    const marker = L.circleMarker([lat, lng], {
        radius: 12,
        color: '#d00000',
        weight: 4,
        fillColor: '#ff4d4d',
        fillOpacity: 0.9
    });

    // Add popup with details
    marker.bindPopup(`
        <strong style="font-size: 2em;">Current Rover</strong><br/>
        <span style="font-size: 1.8em;">
            Position: ${lat.toFixed(5)}, ${lng.toFixed(5)}<br/>
            Wind: ${windSpeed.toFixed(1)} m/s<br/>
            Direction: ${windDirection}°
        </span>
    `);

    // Permanent tooltip with wind info
    const labelHtml = `
        <div style="font-size: 24px; font-weight: bold; line-height: 1.3;">
            <div>${windSpeed.toFixed(1)} m/s</div>
            <div>${windDirection}°</div>
        </div>
    `;
    marker.bindTooltip(labelHtml, {
        permanent: true,
        direction: 'right',
        offset: [20, 0],
        className: 'rover-wind-label-large'
    });

    mapData.positionLayer.addLayer(marker);
    
    console.log(`Position updated: [${lat}, ${lng}], wind: ${windSpeed} m/s @ ${windDirection}°`);
}
