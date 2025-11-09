// Scent Map - Binary JS Interop wrapper for Leaflet
// Provides efficient map updates from Blazor components

const maps = {};

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

    // Create reusable layers
    const layers = {
        forest: null,
        trail: null,
        coverage: null,
        position: null
    };

    // Load forest boundary once
    fetch('/api/forest')
        .then(r => r.json())
        .then(data => {
            layers.forest = L.geoJSON(data, {
                style: {
                    color: '#228B22',
                    weight: 2,
                    fillOpacity: 0.1
                }
            }).addTo(map);
        })
        .catch(err => console.error('Failed to load forest:', err));

    maps[id] = { map, layers };
    console.log(`Map initialized: ${id}`);
}

export function updateTrail(id, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    const coords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        coords.push([floatArray[i + 1], floatArray[i]]); // [lat, lng]
    }

    if (mapData.layers.trail) {
        mapData.layers.trail.setLatLngs(coords);
    } else {
        mapData.layers.trail = L.polyline(coords, {
            color: '#FF4444',
            weight: 3,
            opacity: 0.7
        }).addTo(mapData.map);
    }
}

export function updateCoverage(id, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    const coords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        coords.push([floatArray[i + 1], floatArray[i]]); // [lat, lng]
    }

    if (mapData.layers.coverage) {
        mapData.layers.coverage.setLatLngs([coords]);
    } else {
        mapData.layers.coverage = L.polygon([coords], {
            color: '#4444FF',
            weight: 2,
            fillOpacity: 0.2
        }).addTo(mapData.map);
    }
}

export function updatePosition(id, lng, lat, windSpeed, windDirection) {
    const mapData = maps[id];
    if (!mapData) return;

    const latLng = [lat, lng];

    if (mapData.layers.position) {
        mapData.layers.position.setLatLng(latLng);
        mapData.layers.position.bindTooltip(
            `<strong>Wind: ${windSpeed.toFixed(1)} m/s @ ${windDirection}°</strong>`,
            { permanent: true, direction: 'top' }
        );
    } else {
        mapData.layers.position = L.circleMarker(latLng, {
            radius: 8,
            color: '#FF0000',
            fillColor: '#FF0000',
            fillOpacity: 0.8,
            weight: 2
        }).addTo(mapData.map);

        mapData.layers.position.bindTooltip(
            `<strong>Wind: ${windSpeed.toFixed(1)} m/s @ ${windDirection}°</strong>`,
            { permanent: true, direction: 'top' }
        );
    }
}

export function appendTrail(id, floatArray) {
    const mapData = maps[id];
    if (!mapData || !mapData.layers.trail) return;

    const existingCoords = mapData.layers.trail.getLatLngs();
    const newCoords = [];
    
    for (let i = 0; i < floatArray.length; i += 2) {
        newCoords.push([floatArray[i + 1], floatArray[i]]); // [lat, lng]
    }

    mapData.layers.trail.setLatLngs([...existingCoords, ...newCoords]);
}
