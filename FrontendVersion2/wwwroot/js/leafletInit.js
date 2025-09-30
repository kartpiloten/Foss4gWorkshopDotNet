// Debug: Test if this file is loading properly
console.log('leafletInit.js loading...');

// Ensure the function is available immediately
(function() {
    'use strict';
    
    console.log('Setting up initLeafletMap function...');
    
    window.initLeafletMap = async function () {
        console.log('Starting Leaflet map initialization...');
        
        // Global variables for rover tracking and wind visualization
        let roverLayer = null;
        let roverTrailLayer = null;
        let windArrowsLayer = null;
        let windPolygonsLayer = null;        // New: Individual wind feather polygons
        let combinedCoverageLayer = null;    // New: Combined coverage area
        let clientId = 'client_' + Math.random().toString(36).substr(2, 9);
        let updateInterval = null;
        let lastUnifiedVersion = -1;
        let lastUnifiedDrawTime = 0;
        let lastRoverSeqSeen = -1;

        try {
            // Check if Leaflet is available
            if (typeof L === 'undefined') {
                throw new Error('Leaflet library is not loaded');
            }
            
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
            let forestLayer = null;
            try {
                console.log('Fetching forest polygon...');
                const forestResp = await fetch('/api/riverhead-forest');
                if (forestResp.ok) {
                    try {
                        const geoJson = await forestResp.json();
                        console.log('Forest polygon received:', geoJson);
                        
                        forestLayer = L.geoJSON(geoJson, {
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

            // Initialize all layers with proper ordering (background to foreground)
            console.log('Initializing all data layers...');
            
            // Layer 1: Combined coverage (background - shows total scent detection area)
            combinedCoverageLayer = L.layerGroup();
            
            // Layer 2: Individual wind feather polygons (middle layer)
            windPolygonsLayer = L.layerGroup();
            
            // Layer 3: Rover trail (historical path)
            roverTrailLayer = L.layerGroup();
            
            // Layer 4: Wind arrows (detailed wind data)
            windArrowsLayer = L.layerGroup();
            
            // Layer 5: Current rover position (top layer)
            roverLayer = L.layerGroup();

            // Add layers to map in order (background to foreground)
            combinedCoverageLayer.addTo(map);
            windPolygonsLayer.addTo(map);
            roverTrailLayer.addTo(map);
            windArrowsLayer.addTo(map);
            roverLayer.addTo(map);

            // Helper function definitions (local to this function scope)
            const getWindSpeedColor = (speed) => {
                if (speed <= 2) return '#4dabf7';      // Light blue
                if (speed <= 4) return '#1c7ed6';      // Blue
                if (speed <= 8) return '#ffd43b';      // Yellow
                if (speed <= 10) return '#ff922b';     // Orange
                return '#e03131';                      // Red
            };

            const formatDateTime = (dateString) => {
                if (!dateString) return 'N/A';
                try {
                    return new Date(dateString).toLocaleString();
                } catch {
                    return dateString;
                }
            };

            const formatTimeRange = (timeRange) => {
                if (!timeRange || !timeRange.start || !timeRange.end) return 'N/A';
                try {
                    const start = new Date(timeRange.start).toLocaleString();
                    const end = new Date(timeRange.end).toLocaleString();
                    return `${start} - ${end}`;
                } catch {
                    return 'Invalid date range';
                }
            };

            const createWindArrowSvg = (direction, speed, isCurrentPosition = false) => {
                const color = getWindSpeedColor(speed);
                const size = isCurrentPosition ? 24 : 16;
                const strokeWidth = isCurrentPosition ? 3 : 2;
                
                return `
                    <svg width="${size}" height="${size}" viewBox="0 0 24 24" style="transform: rotate(${direction}deg)">
                        <path d="M12 2 L12 20 M12 2 L8 6 M12 2 L16 6" 
                             stroke="${color}" 
                             stroke-width="${strokeWidth}" 
                             fill="none" 
                             stroke-linecap="round" 
                             stroke-linejoin="round"/>
                        ${isCurrentPosition ? `<circle cx="12" cy="12" r="8" fill="${color}" fill-opacity="0.2" />` : ''}
                    </svg>
                `;
            };

            const addWindArrows = (features) => {
                // Add wind arrows for new measurements
                features.forEach(feature => {
                    if (!feature.geometry) return;
                    
                    const coords = feature.geometry.coordinates;
                    const props = feature.properties || {};
                    
                    const windIcon = L.divIcon({
                        className: 'wind-arrow-icon',
                        html: createWindArrowSvg(props.windDirectionDeg || 0, props.windSpeedMps || 0),
                        iconSize: [20, 20],
                        iconAnchor: [10, 10]
                    });
                    
                    const marker = L.marker([coords[1], coords[0]], { icon: windIcon });
                    marker.bindPopup(`
                        <b>Wind Measurement</b><br/>
                        Speed: ${props.windSpeedMps || 0} m/s<br/>
                        Direction: ${props.windDirectionDeg || 0}°<br/>
                        Time: ${formatDateTime(props.recordedAt)}<br/>
                        Sequence: ${props.sequence || 'N/A'}
                    `);
                    
                    windArrowsLayer.addLayer(marker);
                });
            };

            const updateCurrentRoverPosition = (latestFeature) => {
                if (!latestFeature || !latestFeature.geometry) return;
                
                const coords = latestFeature.geometry.coordinates;
                const props = latestFeature.properties || {};
                
                // Clear existing current position
                roverLayer.clearLayers();
                
                // Create rover icon with wind arrow
                const roverIcon = L.divIcon({
                    className: 'rover-current-icon',
                    html: createWindArrowSvg(props.windDirectionDeg || 0, props.windSpeedMps || 0, true),
                    iconSize: [30, 30],
                    iconAnchor: [15, 15]
                });
                
                const marker = L.marker([coords[1], coords[0]], { icon: roverIcon });
                marker.bindPopup(`
                    <b>Current Rover Position</b><br/>
                    Sequence: ${props.sequence || 'N/A'}<br/>
                    Wind: ${props.windSpeedMps || 0} m/s @ ${props.windDirectionDeg || 0}°<br/>
                    Time: ${formatDateTime(props.recordedAt)}<br/>
                    Location: ${coords[1].toFixed(6)}, ${coords[0].toFixed(6)}
                `);
                
                roverLayer.addLayer(marker);
            };

            const loadCombinedCoverage = async (forceRefresh = false) => {
                try {
                    console.log(`[loadCombinedCoverage] Loading unified scent coverage (force=${forceRefresh})`);
                    
                    const url = '/api/combined-coverage' + (forceRefresh ? '?_=' + Date.now() : '');
                    const resp = await fetch(url, { 
                        cache: forceRefresh ? 'no-store' : 'default',
                        headers: forceRefresh ? { 'Cache-Control': 'no-cache' } : {}
                    });
                    
                    if (!resp.ok) {
                        console.warn('Combined coverage not available:', resp.status, resp.statusText);
                        return;
                    }
                    
                    const geoJson = await resp.json();
                    console.log('Combined coverage data received:', geoJson);
                    
                    // Clear existing combined coverage layer
                    combinedCoverageLayer.clearLayers();
                    
                    if (geoJson.features && geoJson.features.length > 0) {
                        const feature = geoJson.features[0];
                        const props = feature.properties || {};
                        
                        // Create the unified coverage polygon
                        const geoJsonLayer = L.geoJSON(geoJson, {
                            style: {
                                color: '#8B4513',        // Brown border
                                fillColor: '#DEB887',    // Tan/beige fill
                                weight: 2,
                                opacity: 0.8,
                                fillOpacity: 0.15,       // Very transparent background
                                dashArray: '8, 4'        // Dashed border
                            },
                            onEachFeature: (feature, layer) => {
                                const props = feature.properties || {};
                                const popupContent = `
                                    <b>Combined Scent Coverage</b><br/>
                                    Total Polygons: ${props.totalPolygons || 'N/A'}<br/>
                                    Coverage Area: ${(props.totalAreaHectares || props.totalAreaM2 / 10000 || 0).toFixed(1)} hectares<br/>
                                    Efficiency: ${(props.coverageEfficiency * 100 || 0).toFixed(1)}%<br/>
                                    Sessions: ${props.sessionCount || 'N/A'}<br/>
                                    Time Range: ${formatTimeRange(props.timeRange)}<br/>
                                    Version: ${props.unifiedVersion || 'N/A'}
                                `;
                                layer.bindPopup(popupContent);
                            }
                        });
                        
                        combinedCoverageLayer.addLayer(geoJsonLayer);
                        console.log(`? Combined coverage loaded: ${props.totalPolygons || 0} polygons, ${(props.totalAreaHectares || 0).toFixed(1)} hectares`);
                    }
                } catch (error) {
                    console.error('Error loading combined coverage:', error);
                }
            };

            const loadWindPolygons = async () => {
                try {
                    console.log('[loadWindPolygons] Loading individual wind polygons');
                    
                    const resp = await fetch('/api/wind-polygons');
                    if (!resp.ok) {
                        console.warn('Wind polygons not available:', resp.status, resp.statusText);
                        return;
                    }
                    
                    const geoJson = await resp.json();
                    console.log('Wind polygons data received:', geoJson);
                    
                    // Clear existing wind polygons
                    windPolygonsLayer.clearLayers();
                    
                    if (geoJson.features && geoJson.features.length > 0) {
                        const geoJsonLayer = L.geoJSON(geoJson, {
                            style: (feature) => {
                                const windSpeed = feature.properties?.windSpeedMps || 0;
                                const color = getWindSpeedColor(windSpeed);
                                
                                return {
                                    color: color,
                                    fillColor: color,
                                    weight: windSpeed > 5 ? 2 : 1,
                                    opacity: 0.8,
                                    fillOpacity: 0.3,
                                    dashArray: windSpeed > 5 ? '' : '4, 2'
                                };
                            },
                            onEachFeature: (feature, layer) => {
                                const props = feature.properties || {};
                                const popupContent = `
                                    <b>Wind Feather Polygon</b><br/>
                                    Sequence: ${props.sequence || 'N/A'}<br/>
                                    Wind: ${props.windSpeedMps || 0} m/s @ ${props.windDirectionDeg || 0}°<br/>
                                    Area: ${(props.scentAreaM2 || 0).toFixed(0)} m²<br/>
                                    Max Distance: ${(props.maxDistanceM || 0).toFixed(0)} m<br/>
                                    Time: ${formatDateTime(props.recordedAt)}
                                `;
                                layer.bindPopup(popupContent);
                            }
                        });
                        
                        windPolygonsLayer.addLayer(geoJsonLayer);
                        console.log(`? Wind polygons loaded: ${geoJson.features.length} polygons`);
                    }
                } catch (error) {
                    console.error('Error loading wind polygons:', error);
                }
            };

            const loadInitialRoverData = async () => {
                try {
                    console.log('[loadInitialRoverData] Loading initial rover trail');
                    
                    const resp = await fetch('/api/rover-data');
                    if (!resp.ok) {
                        console.warn('Initial rover data not available:', resp.status, resp.statusText);
                        return;
                    }
                    
                    const geoJson = await resp.json();
                    console.log('Initial rover data received:', geoJson);
                    
                    if (geoJson.features && geoJson.features.length > 0) {
                        // Add rover trail
                        const trailLayer = L.geoJSON(geoJson, {
                            pointToLayer: (feature, latlng) => {
                                return L.circleMarker(latlng, {
                                    radius: 3,
                                    fillColor: '#ff6b35',
                                    color: '#ff6b35',
                                    weight: 1,
                                    opacity: 0.8,
                                    fillOpacity: 0.6
                                });
                            },
                            onEachFeature: (feature, layer) => {
                                const props = feature.properties || {};
                                const popupContent = `
                                    <b>Rover Measurement</b><br/>
                                    Sequence: ${props.sequence || 'N/A'}<br/>
                                    Wind: ${props.windSpeedMps || 0} m/s @ ${props.windDirectionDeg || 0}°<br/>
                                    Time: ${formatDateTime(props.recordedAt)}<br/>
                                    Session: ${props.sessionId || 'N/A'}
                                `;
                                layer.bindPopup(popupContent);
                            }
                        });
                        
                        roverTrailLayer.addLayer(trailLayer);
                        
                        // Add wind arrows
                        addWindArrows(geoJson.features);
                        
                        console.log(`? Initial rover data loaded: ${geoJson.features.length} measurements`);
                    }
                } catch (error) {
                    console.error('Error loading initial rover data:', error);
                }
            };

            const startRoverDataUpdates = () => {
                console.log('[startRoverDataUpdates] Starting real-time rover data updates');
                
                // Poll for rover data updates every 2 seconds
                updateInterval = setInterval(async () => {
                    try {
                        const resp = await fetch(`/api/rover-data/updates?clientId=${clientId}`, { cache: 'no-store' });
                        if (!resp.ok) return;
                        
                        const geoJson = await resp.json();
                        if (geoJson.features && geoJson.features.length > 0) {
                            console.log(`[RoverUpdate] ${geoJson.features.length} new measurements`);
                            
                            // Add new trail points
                            const newTrailLayer = L.geoJSON(geoJson, {
                                pointToLayer: (feature, latlng) => {
                                    return L.circleMarker(latlng, {
                                        radius: 3,
                                        fillColor: '#ff6b35',
                                        color: '#ff6b35',
                                        weight: 1,
                                        opacity: 0.8,
                                        fillOpacity: 0.6
                                    });
                                },
                                onEachFeature: (feature, layer) => {
                                    const props = feature.properties || {};
                                    const popupContent = `
                                        <b>Rover Measurement</b><br/>
                                        Sequence: ${props.sequence || 'N/A'}<br/>
                                        Wind: ${props.windSpeedMps || 0} m/s @ ${props.windDirectionDeg || 0}°<br/>
                                        Time: ${formatDateTime(props.recordedAt)}<br/>
                                        Session: ${props.sessionId || 'N/A'}
                                    `;
                                    layer.bindPopup(popupContent);
                                }
                            });
                            
                            roverTrailLayer.addLayer(newTrailLayer);
                            
                            // Add new wind arrows
                            addWindArrows(geoJson.features);
                            
                            // Update current rover position (use the latest measurement)
                            const latest = geoJson.features[geoJson.features.length - 1];
                            if (latest) {
                                updateCurrentRoverPosition(latest);
                            }
                        }
                    } catch (error) {
                        console.warn('Error in rover data update:', error);
                    }
                }, 2000);
            };

            // Load initial data
            await loadCombinedCoverage();
            await loadWindPolygons();
            await loadInitialRoverData();

            // Start real-time updates
            startRoverDataUpdates();

            // Periodic unified status poll independent of rover updates
            setInterval(async () => {
                try {
                    const resp = await fetch('/api/unified-status', { cache: 'no-store' });
                    if (!resp.ok) return;
                    const st = await resp.json();
                    const version = st.unifiedVersion ?? -1;
                    const latestSeq = st.latestSequence ?? -1;
                    
                    console.log(`[UnifiedPoll] Status: version=${version}, seq=${latestSeq}, polygons=${st.polygonCount}`);

                    // If version changed -> fetch & redraw
                    if (version !== lastUnifiedVersion) {
                        console.log('?? Unified version changed (status poll). old=', lastUnifiedVersion, 'new=', version);
                        lastUnifiedVersion = version;
                        await loadCombinedCoverage(true);
                        lastUnifiedDrawTime = Date.now();
                        lastRoverSeqSeen = latestSeq;
                        return;
                    }

                    // If rover sequence advanced but version did not for 10s -> force fetch
                    if (latestSeq > lastRoverSeqSeen && (Date.now() - lastUnifiedDrawTime) > 10000) {
                        console.log('?? Rover seq advanced without unified change. Forcing coverage fetch.');
                        await loadCombinedCoverage(true);
                        lastUnifiedDrawTime = Date.now();
                        lastRoverSeqSeen = latestSeq;
                        return;
                    }

                    // Fallback: If no redraw in 30s but we have polygons, try a forced recompute
                    if ((Date.now() - lastUnifiedDrawTime) > 30000 && latestSeq >= 0) {
                        console.log('?? Forcing unified recompute (stale >30s)');
                        try { await fetch('/api/unified-recompute', { method: 'POST' }); } catch {}
                        await loadCombinedCoverage(true);
                        lastUnifiedDrawTime = Date.now();
                        lastRoverSeqSeen = latestSeq;
                    }
                } catch (e) { console.warn('Unified status poll error', e); }
            }, 5000); // every 5s

            console.log('Map initialization completed successfully');

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
    };

    console.log('initLeafletMap function set up successfully');
    
})();

// Debug: Confirm function is registered
console.log('leafletInit.js loaded successfully, function available:', typeof window.initLeafletMap === 'function');
