/* The functionallity in this file is:
 - Provide a clean JavaScript module for OpenLayers map operations
 - Use reusable vector sources and features to avoid recreating geometries
 - Accept binary float arrays from Blazor for efficient data transfer
 - Support incremental trail updates (append) for performance
*/

// Global map storage (keyed by map ID)
const maps = {};

/**
 * Initialize an OpenLayers map with OSM tiles and forest boundary
 * @param {string} id - The DOM element ID for the map container
 */
export async function initMap(id) {
    console.log(`Initializing OpenLayers map: ${id}`);
    
    let mapCenter = ol.proj.fromLonLat([174.577555, -36.718362]); // [lng, lat]
    let mapZoom = 12;

    // Try to get forest bounds for better centering
    try {
        const boundsResponse = await fetch('/api/forest-bounds');
        if (boundsResponse.ok) {
            const bounds = await boundsResponse.json();
            mapCenter = ol.proj.fromLonLat([bounds.center.lng, bounds.center.lat]);
            mapZoom = 14;
        }
    } catch (error) {
        console.log('Using default map center');
    }

    // Create vector sources for different layers
    const forestSource = new ol.source.Vector();
    const trailSource = new ol.source.Vector();
    const coverageSource = new ol.source.Vector();
    const positionSource = new ol.source.Vector();

    // Create vector layers
    const forestLayer = new ol.layer.Vector({
        source: forestSource,
        style: new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: '#2d5a27',
                width: 2
            }),
            fill: new ol.style.Fill({
                color: 'rgba(74, 124, 89, 0.15)'
            })
        }),
        zIndex: 1
    });

    const trailLayer = new ol.layer.Vector({
        source: trailSource,
        style: new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: '#ffb347',
                width: 4
            })
        }),
        zIndex: 3
    });

    const coverageLayer = new ol.layer.Vector({
        source: coverageSource,
        style: new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: '#8B4513',
                width: 2,
                lineDash: [8, 4]
            }),
            fill: new ol.style.Fill({
                color: 'rgba(222, 184, 135, 0.20)'
            })
        }),
        zIndex: 2
    });

    const positionLayer = new ol.layer.Vector({
        source: positionSource,
        style: new ol.style.Style({
            image: new ol.style.Circle({
                radius: 12,
                fill: new ol.style.Fill({
                    color: '#ff4d4d'
                }),
                stroke: new ol.style.Stroke({
                    color: '#d00000',
                    width: 4
                })
            })
        }),
        zIndex: 4
    });

    // Create the map
    const map = new ol.Map({
        target: id,
        layers: [
            new ol.layer.Tile({
                source: new ol.source.OSM()
            }),
            forestLayer,
            coverageLayer,
            trailLayer,
            positionLayer
        ],
        view: new ol.View({
            center: mapCenter,
            zoom: mapZoom
        }),
        controls: ol.control.defaults.defaults({
            zoom: true,
            attribution: true
        }).extend([
            new ol.control.Zoom({
                target: document.createElement('div'),
                className: 'ol-zoom ol-unselectable ol-control'
            })
        ])
    });

    // Store map and sources for reuse
    maps[id] = {
        map,
        forestSource,
        trailSource,
        coverageSource,
        positionSource,
        trailFeature: null,
        trailCoords: [],
        coverageFeature: null
    };

    // Load forest boundary once
    await loadForestBoundary(id);
    
    console.log(`OpenLayers map ${id} initialized`);
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
        
        // Parse GeoJSON and add to forest source
        const format = new ol.format.GeoJSON();
        const features = format.readFeatures(geoJson, {
            featureProjection: 'EPSG:3857' // OpenLayers default projection
        });
        
        mapData.forestSource.addFeatures(features);
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

    // Convert flat float array to OpenLayers coordinates [lng, lat] and project
    const coords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        coords.push(ol.proj.fromLonLat([floatArray[i], floatArray[i + 1]]));
    }

    mapData.trailCoords = coords;

    // Update or create LineString feature
    if (mapData.trailFeature) {
        mapData.trailFeature.getGeometry().setCoordinates(coords);
    } else {
        const lineString = new ol.geom.LineString(coords);
        mapData.trailFeature = new ol.Feature({
            geometry: lineString
        });
        mapData.trailSource.addFeature(mapData.trailFeature);
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

    // Convert flat float array to OpenLayers coordinates and project
    const newCoords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        newCoords.push(ol.proj.fromLonLat([floatArray[i], floatArray[i + 1]]));
    }

    // Append to existing coordinates
    mapData.trailCoords.push(...newCoords);

    // Update LineString feature
    if (mapData.trailFeature) {
        mapData.trailFeature.getGeometry().setCoordinates(mapData.trailCoords);
    } else {
        const lineString = new ol.geom.LineString(mapData.trailCoords);
        mapData.trailFeature = new ol.Feature({
            geometry: lineString
        });
        mapData.trailSource.addFeature(mapData.trailFeature);
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

    // Convert flat float array to OpenLayers coordinates and project
    const coords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        coords.push(ol.proj.fromLonLat([floatArray[i], floatArray[i + 1]]));
    }

    // Update or create Polygon feature
    if (mapData.coverageFeature) {
        mapData.coverageFeature.getGeometry().setCoordinates([coords]); // Polygon expects array of rings
    } else {
        const polygon = new ol.geom.Polygon([coords]);
        mapData.coverageFeature = new ol.Feature({
            geometry: polygon
        });
        mapData.coverageSource.addFeature(mapData.coverageFeature);
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
    mapData.positionSource.clear();

    // Create point feature
    const coordinate = ol.proj.fromLonLat([lng, lat]);
    const pointFeature = new ol.Feature({
        geometry: new ol.geom.Point(coordinate),
        windSpeed: windSpeed,
        windDirection: windDirection,
        position: `${lat.toFixed(5)}, ${lng.toFixed(5)}`
    });

    mapData.positionSource.addFeature(pointFeature);
    
    // Create overlay for tooltip (text label)
    const overlay = document.getElementById(`${id}-overlay`);
    if (overlay) {
        overlay.remove();
    }
    
    const overlayDiv = document.createElement('div');
    overlayDiv.id = `${id}-overlay`;
    overlayDiv.className = 'rover-wind-label-large';
    overlayDiv.style.cssText = `
        position: absolute;
        background: white;
        padding: 8px;
        border-radius: 4px;
        font-size: 24px;
        font-weight: bold;
        line-height: 1.3;
        pointer-events: none;
        box-shadow: 0 2px 4px rgba(0,0,0,0.3);
    `;
    overlayDiv.innerHTML = `
        <div>${windSpeed.toFixed(1)} m/s</div>
        <div>${windDirection}°</div>
    `;
    
    const olOverlay = new ol.Overlay({
        element: overlayDiv,
        position: coordinate,
        positioning: 'center-left',
        offset: [20, 0]
    });
    
    mapData.map.addOverlay(olOverlay);
    
    console.log(`Position updated: [${lat}, ${lng}], wind: ${windSpeed} m/s @ ${windDirection}°`);
}
