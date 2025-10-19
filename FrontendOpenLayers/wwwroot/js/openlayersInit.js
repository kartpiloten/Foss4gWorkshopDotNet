// ====== Fullscreen OpenLayers Map (Single Rover Trail Version) ======
// Loading Rover Tracker with a Single Persistent Trail

console.log('??? Loading Rover Tracker (OpenLayers - single trail mode)...');

let map = null;
let roverTrailSource = null;
let roverTrailFeature = null;
let roverTrailCoords = []; // Persist coordinates client-side
let lastTrailPointCount = 0;

window.initOpenLayersMap = async function() {
    console.log('??? Initializing OpenLayers map (trail only)...');

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
    } catch {}

    // Create vector sources for different layers
    const forestSource = new ol.source.Vector();
    const coverageSource = new ol.source.Vector();
    roverTrailSource = new ol.source.Vector();
    const currentPositionSource = new ol.source.Vector();

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

    const roverTrailLayer = new ol.layer.Vector({
        source: roverTrailSource,
        style: new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: '#ffb347',
                width: 4
            })
        }),
        zIndex: 3
    });

    const currentPositionLayer = new ol.layer.Vector({
        source: currentPositionSource,
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

    // Create the OpenLayers map
    map = new ol.Map({
        target: 'map',
        layers: [
            new ol.layer.Tile({
                source: new ol.source.OSM()
            }),
            forestLayer,
            coverageLayer,
            roverTrailLayer,
            currentPositionLayer
        ],
        view: new ol.View({
            center: mapCenter,
            zoom: mapZoom
        }),
        controls: ol.control.defaults.defaults().extend([
            new ol.control.ZoomSlider()
        ])
    });

    // Add popup overlay for current position
    const popup = document.createElement('div');
    popup.className = 'ol-popup';
    popup.style.cssText = 'background: white; padding: 20px; border-radius: 4px; box-shadow: 0 2px 6px rgba(0,0,0,0.3); display: none; position: absolute; bottom: 12px; left: -50px; min-width: 200px; pointer-events: none; font-size: 1.8em;';
    
    const popupOverlay = new ol.Overlay({
        element: popup,
        positioning: 'bottom-center',
        stopEvent: false,
        offset: [0, -10]
    });
    map.addOverlay(popupOverlay);

    // Load initial data
    await loadForestBoundary(forestSource);
    await loadCombinedCoverage(coverageSource);
    await loadInitialTrail();
    await loadLatestPosition(currentPositionSource, popupOverlay, popup);

    // Periodically update the trail and latest position
    setInterval(async () => {
        await appendNewTrailPoints();
        await loadLatestPosition(currentPositionSource, popupOverlay, popup);
        await loadCombinedCoverage(coverageSource);
    }, 8000);

    // Click handler for current position marker
    map.on('click', function(evt) {
        const feature = map.forEachFeatureAtPixel(evt.pixel, function(feature) {
            return feature;
        }, {
            layerFilter: function(layer) {
                return layer === currentPositionLayer;
            }
        });

        if (feature && feature.get('popupContent')) {
            popup.innerHTML = feature.get('popupContent');
            popup.style.display = 'block';
            popupOverlay.setPosition(evt.coordinate);
        } else {
            popup.style.display = 'none';
        }
    });

    // ====== Helper Functions ======
    
    async function loadForestBoundary(source) {
        try {
            console.log('?? Loading forest boundary...' );
            const response = await fetch('/api/forest');
            if (!response.ok) return;
            
            const geoJson = await response.json();
            
            const features = new ol.format.GeoJSON().readFeatures(geoJson, {
                featureProjection: 'EPSG:3857'
            });
            
            source.addFeatures(features);
            
            console.log('? Forest boundary loaded');
        } catch (error) {
            console.log('? Could not load forest boundary:', error.message);
        }
    }
    
    async function loadInitialTrail() {
        try {
            console.log('??? Loading initial rover trail...');
            const response = await fetch('/api/rover-trail?limit=5000');
            if (!response.ok) return;
            
            const geo = await response.json();
            
            if (!geo.features?.length) return;
            
            const ls = geo.features[0];
            
            if (ls.geometry?.type !== 'LineString') return;
            
            // Convert to OpenLayers coordinates [lng, lat] -> [x, y] in EPSG:3857
            roverTrailCoords = ls.geometry.coordinates.map(c => 
                ol.proj.fromLonLat([c[0], c[1]])
            );
            
            // Create the line feature
            roverTrailFeature = new ol.Feature({
                geometry: new ol.geom.LineString(roverTrailCoords)
            });
            
            roverTrailSource.addFeature(roverTrailFeature);
            
            lastTrailPointCount = ls.geometry.coordinates.length;
            
            console.log(`? Initial trail loaded (${lastTrailPointCount} points)`);
        } catch (e) {
            console.log('? Trail init failed', e);
        }
    }

    async function appendNewTrailPoints() {
        try {
            const response = await fetch('/api/rover-trail?limit=5000');
            if (!response.ok) return;
            
            const geo = await response.json();
            
            if (!geo.features?.length) return;
            
            const ls = geo.features[0];
            
            if (ls.geometry?.type !== 'LineString') return;
            
            const coords = ls.geometry.coordinates;
            
            if (coords.length <= lastTrailPointCount) return; // nothing new

            // Add new points to the existing trail
            const newPoints = coords.slice(lastTrailPointCount).map(c => 
                ol.proj.fromLonLat([c[0], c[1]])
            );
            
            roverTrailCoords.push(...newPoints);
            lastTrailPointCount = coords.length;
            
            // Update the geometry with new coordinates
            if (roverTrailFeature) {
                roverTrailFeature.getGeometry().setCoordinates(roverTrailCoords);
            }
            
            console.log(`? Trail extended. Total points: ${lastTrailPointCount}`);
        } catch (e) {
            console.log('? Append trail failed', e);
        }
    }

    async function loadLatestPosition(source, overlay, popupElement) {
        try {
            const statsResp = await fetch('/api/rover-stats');
            if (!statsResp.ok) return;
            
            const stats = await statsResp.json();
            
            if (!stats.latestPosition) return;
            
            source.clear();
            
            const p = stats.latestPosition;
            const coords = ol.proj.fromLonLat([p.lng, p.lat]);
            
            const marker = new ol.Feature({
                geometry: new ol.geom.Point(coords)
            });
            
            const popupContent = `
                <strong style="font-size: 2em;">Current Rover</strong><br/>
                <span style="font-size: 1.8em;">Seq: ${stats.latestSequence}<br/>
                Wind: ${p.windSpeed} m/s<br/>
                Direction: ${p.windDirection} degrees<br/>
                Points: ${lastTrailPointCount}</span>
            `;
            
            marker.set('popupContent', popupContent);
            
            // Create a text style for the permanent label - TWO ROWS, DOUBLED SIZE
            marker.setStyle(new ol.style.Style({
                image: new ol.style.Circle({
                    radius: 12,  // Doubled from 6
                    fill: new ol.style.Fill({
                        color: '#ff4d4d'
                    }),
                    stroke: new ol.style.Stroke({
                        color: '#d00000',
                        width: 4  // Doubled from 2
                    })
                }),
                text: new ol.style.Text({
                    text: `${Number(p.windSpeed).toFixed(1)} m/s\n${p.windDirection} degrees`,
                    offsetX: 100,  // Doubled from 50
                    offsetY: 0,
                    font: 'bold 24px sans-serif',  // Doubled from 12px
                    fill: new ol.style.Fill({
                        color: '#000'
                    }),
                    stroke: new ol.style.Stroke({
                        color: '#fff',
                        width: 6  // Doubled from 3
                    }),
                    textAlign: 'left',
                    backgroundFill: new ol.style.Fill({
                        color: 'rgba(255, 255, 255, 0.95)'
                    }),
                    padding: [8, 12, 8, 12]  // Doubled from [4, 6, 4, 6]
                })
            }));
            
            source.addFeature(marker);
        } catch (error) {
            console.log('? Could not load latest position:', error);
        }
    }

    async function loadCombinedCoverage(source) {
        try {
            const response = await fetch('/api/combined-coverage');
            if (!response.ok) return;
            
            const geoJson = await response.json();
            
            source.clear();
            
            const features = new ol.format.GeoJSON().readFeatures(geoJson, {
                featureProjection: 'EPSG:3857'
            });
            
            source.addFeatures(features);
            
        } catch (error) {
            console.log('? Could not load combined coverage:', error.message);
        }
    }
};
