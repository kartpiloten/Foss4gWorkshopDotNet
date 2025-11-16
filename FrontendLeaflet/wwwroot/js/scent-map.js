// Scent Map - Binary JS Interop wrapper for Leaflet
// Provides efficient map updates from Blazor components with multi-rover support

const maps = {};

// Color palette for different rovers
const ROVER_COLORS = [
    '#FF4444', // Red
    '#44FF44', // Green
    '#4444FF', // Blue
    '#FF44FF', // Magenta
    '#44FFFF', // Cyan
    '#AA44FF',  // Purple
    '#FF8844',  // Coral
    '#88FF44',  // Lime
    '#4488FF',  // Sky Blue
    '#FF4488',  // Pink
    '#44FF88',  // Mint
];

function getRoverColor(roverId, roverIndex) {
    // Use hash of roverId for consistent colors
    let hash = 0;
    for (let i = 0; i < roverId.length; i++) {
        hash = ((hash << 5) - hash) + roverId.charCodeAt(i);
        hash = hash & hash;
    }
    return ROVER_COLORS[Math.abs(hash) % ROVER_COLORS.length];
}

export function initMap(id) {
    if (maps[id]) return;

    const map = L.map(id, {
        center: [-36.72, 174.58],
        zoom: 13,
        zoomControl: false
    });

    // Add zoom control to bottom left
    L.control.zoom({ position: 'bottomleft' }).addTo(map);

    // Add OSM tiles
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap contributors',
        maxZoom: 19
    }).addTo(map);

    const paneConfig = {
        coveragePane: 400,     // Below trails
        roverTrailPane: 600,   // Above coverage, below markers
        roverMarkerPane: 650   // On top
    };

    Object.entries(paneConfig).forEach(([name, zIndex]) => {
        map.getPane(name) || map.createPane(name);
        map.getPanes()[name].style.zIndex = zIndex;
        map.getPanes()[name].style.pointerEvents = 'none'; // Allow clicks to pass through if needed
    });

    // Create reusable layers
    const layers = {
        forest: null,
        coverage: null,
        rovers: {}  // { roverId: { trail: L.polyline, position: L.marker, name: string, color: string } }
    };

    maps[id] = { map, layers };
    console.log(`Map initialized: ${id}`);
}

export function updateRoverTrail(id, roverId, roverName, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    const coords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        coords.push([floatArray[i + 1], floatArray[i]]); // [lat, lng]
    }

    // Get or create rover entry
    if (!mapData.layers.rovers[roverId]) {
        const color = getRoverColor(roverId, Object.keys(mapData.layers.rovers).length);
        mapData.layers.rovers[roverId] = {
            trail: null,
            position: null,
            name: roverName,
            color: color
        };
    }

    const rover = mapData.layers.rovers[roverId];
    
    if (rover.trail) {
        rover.trail.setLatLngs(coords);
    } else {
        rover.trail = L.polyline(coords, {
            color: rover.color,
            weight: 3,
            opacity: 0.7,
            pane: 'roverTrailPane'
        }).addTo(mapData.map);
        
        rover.trail.bindPopup(`<strong>${roverName}</strong>`);
    }
}

export function updateCoverageGeoJson(id, geoJsonString) {
    const mapData = maps[id];
    if (!mapData) return;

    try {
        const geo = JSON.parse(geoJsonString);

        if (mapData.layers.coverage) {
            mapData.layers.coverage.clearLayers();
            mapData.layers.coverage.addData(geo);
        } else {
            mapData.layers.coverage = L.geoJSON(geo, {
                style: {
                    color: '#fed240',
                    weight: 2,
                    fillOpacity: 0.3
                },
                pane: 'coveragePane'
            }).addTo(mapData.map);
        }

        
    }
    catch (e) {
        console.error('updateCoverageGeoJson error', e);
    }
}

export function updateForestBoundaryGeoJson(id, geoJsonString) {
    const mapData = maps[id];
    if (!mapData) return;

    try {
        const geo = JSON.parse(geoJsonString);

        if (mapData.layers.forest) {
            mapData.map.removeLayer(mapData.layers.forest);
            mapData.layers.forest = null;
        }

        mapData.layers.forest = L.geoJSON(geo, {
            style: {
                color: '#228B22',
                weight: 2,
                fillOpacity: 0.1
            }
        }).addTo(mapData.map);
    }
    catch (e) {
        console.error('updateForestBoundaryGeoJson error', e);
    }
}

export function updateRoverPosition(id, roverId, roverName, lng, lat, windSpeed, windDirection) {
    const mapData = maps[id];
    if (!mapData) return;

    const latLng = [lat, lng];

    // Get or create rover entry
    if (!mapData.layers.rovers[roverId]) {
        const color = getRoverColor(roverId, Object.keys(mapData.layers.rovers).length);
        mapData.layers.rovers[roverId] = {
            trail: null,
            position: null,
            name: roverName,
            color: color
        };
    }

    const rover = mapData.layers.rovers[roverId];

    if (rover.position) {
        rover.position.setLatLng(latLng);
        rover.position.bindTooltip(
            `<strong>${roverName}</strong><br/>Wind: ${windSpeed.toFixed(1)} m/s @ ${windDirection}°`,
            { permanent: true, direction: 'top' }
        );
    } else {
        rover.position = L.circleMarker(latLng, {
            radius: 8,
            color: rover.color,
            fillColor: rover.color,
            fillOpacity: 0.8,
            weight: 2,
            pane: 'roverMarkerPane'
        }).addTo(mapData.map);

        rover.position.bindTooltip(
            `<strong>${roverName}</strong><br/>Wind: ${windSpeed.toFixed(1)} m/s @ ${windDirection}°`,
            { permanent: true, direction: 'top' }
        );
    }
}

export function appendRoverTrail(id, roverId, roverName, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    // Get or create rover entry
    if (!mapData.layers.rovers[roverId]) {
        const color = getRoverColor(roverId, Object.keys(mapData.layers.rovers).length);
        mapData.layers.rovers[roverId] = {
            trail: null,
            position: null,
            name: roverName,
            color: color
        };
    }

    const rover = mapData.layers.rovers[roverId];
    if (!rover.trail) return;

    const existingCoords = rover.trail.getLatLngs();
    const newCoords = [];
    
    for (let i = 0; i < floatArray.length; i += 2) {
        newCoords.push([floatArray[i + 1], floatArray[i]]); // [lat, lng]
    }

    rover.trail.setLatLngs([...existingCoords, ...newCoords]);
}
