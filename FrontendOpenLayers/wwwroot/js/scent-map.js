// Scent Map - Binary JS Interop wrapper for OpenLayers
// Provides efficient map updates from Blazor components with multi-rover support

const maps = {};

// Color palette for different rovers
const ROVER_COLORS = [
    '#FF4444', // Red
    '#44FF44', // Green
    '#4444FF', // Blue
    '#FFAA44', // Orange
    '#FF44FF', // Magenta
    '#44FFFF', // Cyan
    '#FFFF44', // Yellow
    '#AA44FF'  // Purple
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

    const mapCenter = ol.proj.fromLonLat([174.577555, -36.718362]);
    const mapZoom = 13;

    // Create vector sources for different layers
    const forestSource = new ol.source.Vector();
    const coverageSource = new ol.source.Vector();

    // Forest layer styling
    const forestLayer = new ol.layer.Vector({
        source: forestSource,
        style: new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: '#228B22',
                width: 2
            }),
            fill: new ol.style.Fill({
                color: 'rgba(34, 139, 34, 0.1)'
            })
        }),
        zIndex: 1
    });

    // Coverage layer styling
    const coverageLayer = new ol.layer.Vector({
        source: coverageSource,
        style: new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: '#4444FF',
                width: 2
            }),
            fill: new ol.style.Fill({
                color: 'rgba(68, 68, 255, 0.2)'
            })
        }),
        zIndex: 2
    });

    // Create the OpenLayers map
    const map = new ol.Map({
        target: id,
        layers: [
            new ol.layer.Tile({
                source: new ol.source.OSM()
            }),
            forestLayer,
            coverageLayer
        ],
        view: new ol.View({
            center: mapCenter,
            zoom: mapZoom
        }),
        controls: ol.control.defaults.defaults({
            zoom: true,
            attribution: true
        })
    });

    // Move zoom control to bottom left
    map.getControls().forEach(control => {
        if (control instanceof ol.control.Zoom) {
            control.element.style.position = 'absolute';
            control.element.style.bottom = '10px';
            control.element.style.left = '10px';
            control.element.style.top = 'auto';
        }
    });

    // Store references
    const layers = {
        forest: forestSource,
        coverage: coverageSource,
        rovers: {}  // { roverId: { trailLayer, positionLayer, trailSource, positionSource, name, color } }
    };

    const features = {
        coverage: null
    };

    maps[id] = { map, layers, features };

    console.log(`Map initialized: ${id}`);
}

export function updateRoverTrail(id, roverId, roverName, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    // Convert flat array [lng, lat, lng, lat, ...] to OpenLayers line coordinates
    const coords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        const transformed = ol.proj.fromLonLat([floatArray[i], floatArray[i + 1]]);
        coords.push(transformed);
    }

    // Get or create rover entry
    if (!mapData.layers.rovers[roverId]) {
        const color = getRoverColor(roverId, Object.keys(mapData.layers.rovers).length);
        const trailSource = new ol.source.Vector();
        const trailLayer = new ol.layer.Vector({
            source: trailSource,
            style: new ol.style.Style({
                stroke: new ol.style.Stroke({
                    color: color,
                    width: 3
                })
            }),
            zIndex: 3
        });
        mapData.map.addLayer(trailLayer);

        const positionSource = new ol.source.Vector();
        const positionLayer = new ol.layer.Vector({
            source: positionSource,
            zIndex: 4
        });
        mapData.map.addLayer(positionLayer);

        mapData.layers.rovers[roverId] = {
            trailLayer,
            positionLayer,
            trailSource,
            positionSource,
            trailFeature: null,
            positionFeature: null,
            name: roverName,
            color: color
        };
    }

    const rover = mapData.layers.rovers[roverId];

    if (rover.trailFeature) {
        // Update existing feature
        rover.trailFeature.getGeometry().setCoordinates(coords);
    } else {
        // Create new feature
        rover.trailFeature = new ol.Feature({
            geometry: new ol.geom.LineString(coords),
            name: roverName
        });
        rover.trailSource.addFeature(rover.trailFeature);
    }
}



export function updateCoverageGeoJson(id, geoJsonString) {
    const mapData = maps[id];
    if (!mapData) return;

    try {
        const format = new ol.format.GeoJSON();
        const geo = JSON.parse(geoJsonString);
        const features = format.readFeatures(geo, { featureProjection: mapData.map.getView().getProjection() });

        // Clear previous coverage features
        mapData.layers.coverage.clear();
        features.forEach(f => mapData.layers.coverage.addFeature(f));
    }
    catch (e) {
        console.error('updateCoverageGeoJson error', e);
    }
}



export function updateForestBoundaryGeoJson(id, geoJsonString) {
    const mapData = maps[id];
    if (!mapData) return;

    try {
        const format = new ol.format.GeoJSON();
        const geo = JSON.parse(geoJsonString);
        const features = format.readFeatures(geo, { featureProjection: mapData.map.getView().getProjection() });

        mapData.layers.forest.clear();
        features.forEach(f => mapData.layers.forest.addFeature(f));
    }
    catch (e) {
        console.error('updateForestBoundaryGeoJson error', e);
    }
}

export function updateRoverPosition(id, roverId, roverName, lng, lat, windSpeed, windDirection) {
    const mapData = maps[id];
    if (!mapData) return;

    const coords = ol.proj.fromLonLat([lng, lat]);

    // Get or create rover entry
    if (!mapData.layers.rovers[roverId]) {
        const color = getRoverColor(roverId, Object.keys(mapData.layers.rovers).length);
        const trailSource = new ol.source.Vector();
        const trailLayer = new ol.layer.Vector({
            source: trailSource,
            style: new ol.style.Style({
                stroke: new ol.style.Stroke({
                    color: color,
                    width: 3
                })
            }),
            zIndex: 3
        });
        mapData.map.addLayer(trailLayer);

        const positionSource = new ol.source.Vector();
        const positionLayer = new ol.layer.Vector({
            source: positionSource,
            zIndex: 4
        });
        mapData.map.addLayer(positionLayer);

        mapData.layers.rovers[roverId] = {
            trailLayer,
            positionLayer,
            trailSource,
            positionSource,
            trailFeature: null,
            positionFeature: null,
            name: roverName,
            color: color
        };
    }

    const rover = mapData.layers.rovers[roverId];

    const style = new ol.style.Style({
        image: new ol.style.Circle({
            radius: 8,
            fill: new ol.style.Fill({
                color: rover.color
            }),
            stroke: new ol.style.Stroke({
                color: rover.color,
                width: 2
            })
        }),
        text: new ol.style.Text({
            text: `${roverName}\nWind: ${windSpeed.toFixed(1)} m/s @ ${windDirection}°`,
            offsetY: -20,
            font: 'bold 12px sans-serif',
            fill: new ol.style.Fill({
                color: '#000'
            }),
            stroke: new ol.style.Stroke({
                color: '#fff',
                width: 3
            }),
            backgroundFill: new ol.style.Fill({
                color: 'rgba(255, 255, 255, 0.9)'
            }),
            padding: [4, 6, 4, 6]
        })
    });

    if (rover.positionFeature) {
        // Update existing feature
        rover.positionFeature.getGeometry().setCoordinates(coords);
        rover.positionFeature.setStyle(style);
    } else {
        // Create new feature
        rover.positionFeature = new ol.Feature({
            geometry: new ol.geom.Point(coords),
            name: roverName
        });
        rover.positionFeature.setStyle(style);
        rover.positionSource.addFeature(rover.positionFeature);
    }
}

export function appendRoverTrail(id, roverId, roverName, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    // Get or create rover entry
    if (!mapData.layers.rovers[roverId]) {
        const color = getRoverColor(roverId, Object.keys(mapData.layers.rovers).length);
        const trailSource = new ol.source.Vector();
        const trailLayer = new ol.layer.Vector({
            source: trailSource,
            style: new ol.style.Style({
                stroke: new ol.style.Stroke({
                    color: color,
                    width: 3
                })
            }),
            zIndex: 3
        });
        mapData.map.addLayer(trailLayer);

        const positionSource = new ol.source.Vector();
        const positionLayer = new ol.layer.Vector({
            source: positionSource,
            zIndex: 4
        });
        mapData.map.addLayer(positionLayer);

        mapData.layers.rovers[roverId] = {
            trailLayer,
            positionLayer,
            trailSource,
            positionSource,
            trailFeature: null,
            positionFeature: null,
            name: roverName,
            color: color
        };
    }

    const rover = mapData.layers.rovers[roverId];
    if (!rover.trailFeature) return;

    // Get existing coordinates
    const existingCoords = rover.trailFeature.getGeometry().getCoordinates();
    
    // Convert new coordinates
    const newCoords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        const transformed = ol.proj.fromLonLat([floatArray[i], floatArray[i + 1]]);
        newCoords.push(transformed);
    }

    // Append and update
    rover.trailFeature.getGeometry().setCoordinates([...existingCoords, ...newCoords]);
}
