// Scent Map - Binary JS Interop wrapper for OpenLayers
// Provides efficient map updates from Blazor components

const maps = {};

export function initMap(id) {
    if (maps[id]) return;

    const mapCenter = ol.proj.fromLonLat([174.577555, -36.718362]);
    const mapZoom = 13;

    // Create vector sources for different layers
    const forestSource = new ol.source.Vector();
    const trailSource = new ol.source.Vector();
    const coverageSource = new ol.source.Vector();
    const positionSource = new ol.source.Vector();

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

    // Trail layer styling
    const trailLayer = new ol.layer.Vector({
        source: trailSource,
        style: new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: '#FF4444',
                width: 3
            })
        }),
        zIndex: 3
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

    // Position layer styling
    const positionLayer = new ol.layer.Vector({
        source: positionSource,
        style: new ol.style.Style({
            image: new ol.style.Circle({
                radius: 8,
                fill: new ol.style.Fill({
                    color: '#FF0000'
                }),
                stroke: new ol.style.Stroke({
                    color: '#FF0000',
                    width: 2
                })
            })
        }),
        zIndex: 4
    });

    // Create the OpenLayers map
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
        trail: trailSource,
        coverage: coverageSource,
        position: positionSource
    };

    const features = {
        trail: null,
        coverage: null,
        position: null
    };

    maps[id] = { map, layers, features };

    // Load forest boundary once
    fetch('/api/forest')
        .then(r => r.json())
        .then(data => {
            const features = new ol.format.GeoJSON().readFeatures(data, {
                featureProjection: 'EPSG:3857'
            });
            forestSource.addFeatures(features);
        })
        .catch(err => console.error('Failed to load forest:', err));

    console.log(`Map initialized: ${id}`);
}

export function updateTrail(id, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    // Convert flat array [lng, lat, lng, lat, ...] to OpenLayers coordinates
    const coords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        const transformed = ol.proj.fromLonLat([floatArray[i], floatArray[i + 1]]);
        coords.push(transformed);
    }

    if (mapData.features.trail) {
        // Update existing feature
        mapData.features.trail.getGeometry().setCoordinates(coords);
    } else {
        // Create new feature
        mapData.features.trail = new ol.Feature({
            geometry: new ol.geom.LineString(coords)
        });
        mapData.layers.trail.addFeature(mapData.features.trail);
    }
}

export function updateCoverage(id, floatArray) {
    const mapData = maps[id];
    if (!mapData) return;

    // Convert flat array [lng, lat, lng, lat, ...] to OpenLayers polygon coordinates
    const coords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        const transformed = ol.proj.fromLonLat([floatArray[i], floatArray[i + 1]]);
        coords.push(transformed);
    }

    if (mapData.features.coverage) {
        // Update existing feature
        mapData.features.coverage.getGeometry().setCoordinates([coords]);
    } else {
        // Create new feature
        mapData.features.coverage = new ol.Feature({
            geometry: new ol.geom.Polygon([coords])
        });
        mapData.layers.coverage.addFeature(mapData.features.coverage);
    }
}

export function updatePosition(id, lng, lat, windSpeed, windDirection) {
    const mapData = maps[id];
    if (!mapData) return;

    const coords = ol.proj.fromLonLat([lng, lat]);

    if (mapData.features.position) {
        // Update existing feature
        mapData.features.position.getGeometry().setCoordinates(coords);
        
        // Update label
        mapData.features.position.setStyle(new ol.style.Style({
            image: new ol.style.Circle({
                radius: 8,
                fill: new ol.style.Fill({
                    color: '#FF0000'
                }),
                stroke: new ol.style.Stroke({
                    color: '#FF0000',
                    width: 2
                })
            }),
            text: new ol.style.Text({
                text: `Wind: ${windSpeed.toFixed(1)} m/s @ ${windDirection}°`,
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
        }));
    } else {
        // Create new feature
        mapData.features.position = new ol.Feature({
            geometry: new ol.geom.Point(coords)
        });
        
        mapData.features.position.setStyle(new ol.style.Style({
            image: new ol.style.Circle({
                radius: 8,
                fill: new ol.style.Fill({
                    color: '#FF0000'
                }),
                stroke: new ol.style.Stroke({
                    color: '#FF0000',
                    width: 2
                })
            }),
            text: new ol.style.Text({
                text: `Wind: ${windSpeed.toFixed(1)} m/s @ ${windDirection}°`,
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
        }));
        
        mapData.layers.position.addFeature(mapData.features.position);
    }
}

export function appendTrail(id, floatArray) {
    const mapData = maps[id];
    if (!mapData || !mapData.features.trail) return;

    // Get existing coordinates
    const existingCoords = mapData.features.trail.getGeometry().getCoordinates();
    
    // Convert new coordinates
    const newCoords = [];
    for (let i = 0; i < floatArray.length; i += 2) {
        const transformed = ol.proj.fromLonLat([floatArray[i], floatArray[i + 1]]);
        newCoords.push(transformed);
    }

    // Append and update
    mapData.features.trail.getGeometry().setCoordinates([...existingCoords, ...newCoords]);
}
