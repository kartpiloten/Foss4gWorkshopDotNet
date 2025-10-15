// ====== Fullscreen Leaflet Map (Single Rover Trail Version) ======
// Loading Rover Tracker with a Single Persistent Trail

console.log('??? Loading Rover Tracker (single trail mode)...');

let roverTrailPolyline = null;
let roverTrailCoords = []; // Persist coordinates client-side
let lastTrailPointCount = 0;

window.initLeafletMap = async function() {
    console.log('??? Initializing map (trail only)...');

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
    } catch {}

    // Create the Leaflet map without default controls that might interfere with overlays
    const map = L.map('map', { zoomControl: false }).setView(mapCenter, mapZoom);
    
    // Add zoom control in a position that doesn't conflict with overlays
    L.control.zoom({ position: 'bottomleft' }).addTo(map);
    
    // Add OpenStreetMap tiles
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '© OpenStreetMap'
    }).addTo(map);

    // Create map layers
    const forestLayer = L.layerGroup().addTo(map);
    const coverageLayer = L.layerGroup().addTo(map);
    const currentPositionLayer = L.layerGroup().addTo(map);

    // Load initial data
    await loadForestBoundary(forestLayer);
    await loadCombinedCoverage(coverageLayer);
    await loadInitialTrail(); // loads historical points once
    await loadLatestPosition(currentPositionLayer); // latest marker

    // Periodically update the trail and latest position
    setInterval(async () => {
        await appendNewTrailPoints();
        await loadLatestPosition(currentPositionLayer);
        await loadCombinedCoverage(coverageLayer);
    }, 8000);

    // ====== Helper Functions ======
    
    async function loadForestBoundary(layer) {
        try {
            console.log('?? Loading forest boundary...');
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
            }).addTo(layer);
            
            console.log('? Forest boundary loaded');
        } catch (error) {
            console.log('? Could not load forest boundary:', error.message);
        }
    }
    
    async function loadInitialTrail() {
        try {
            console.log('??? Loading initial rover trail...');
            const response = await fetch('/api/rover-trail?limit=5000'); // big number; server limits if needed
            if (!response.ok) return;
            
            const geo = await response.json();
            
            if (!geo.features?.length) {
                console.log('? No trail features returned');
                return;
            }
            
            const ls = geo.features[0];
            
            if (ls.geometry?.type !== 'LineString') {
                console.log('? Trail geometry is not a LineString:', ls.geometry?.type);
                return;
            }
            
            roverTrailCoords = ls.geometry.coordinates.map(c => [c[1], c[0]]); // Leaflet uses [lat,lng]
            
            console.log(`?? Trail coordinates loaded: ${roverTrailCoords.length} points`);
            console.log(`?? First point: [${roverTrailCoords[0]}]`);
            console.log(`?? Last point: [${roverTrailCoords[roverTrailCoords.length - 1]}]`);
            
            // Create the polyline for the rover trail
            roverTrailPolyline = L.polyline(roverTrailCoords, {
                color: '#ffb347',
                weight: 4,
                opacity: 0.85
            }).addTo(map);
            
            lastTrailPointCount = roverTrailCoords.length;
            
            console.log(`? Initial trail loaded (${lastTrailPointCount} points)`);
        } catch (e) {
            console.log('? Trail init failed', e);
        }
    }

    async function appendNewTrailPoints() {
        try {
            // Fetch the latest trail points (reuse /api/rover-trail for now)
            const response = await fetch('/api/rover-trail?limit=5000');
            if (!response.ok) return;
            
            const geo = await response.json();
            
            if (!geo.features?.length) return;
            
            const ls = geo.features[0];
            
            if (ls.geometry?.type !== 'LineString') return;
            
            const coords = ls.geometry.coordinates.map(c => [c[1], c[0]]);
            
            console.log(`?? Checking for new points: current=${lastTrailPointCount}, fetched=${coords.length}`);
            
            if (coords.length <= lastTrailPointCount) {
                console.log('?? No new points to add');
                return; // nothing new
            }

            // Add new points to the existing trail
            const newSeg = coords.slice(lastTrailPointCount); // new tail points
            console.log(`? Adding ${newSeg.length} new points to trail`);
            roverTrailCoords.push(...newSeg);
            lastTrailPointCount = coords.length;
            
            // Update the polyline with new coordinates
            if (roverTrailPolyline) {
                roverTrailPolyline.setLatLngs(roverTrailCoords);
                console.log(`? Trail updated. Total points: ${lastTrailPointCount}`);
                console.log(`?? New last point: [${roverTrailCoords[roverTrailCoords.length - 1]}]`);
            } else {
                console.log('? Trail polyline is null!');
            }
        } catch (e) {
            console.log('? Append trail failed', e);
        }
    }

    async function loadLatestPosition(layer) {
        try {
            const statsResp = await fetch('/api/rover-stats');
            if (!statsResp.ok) return;
            
            const stats = await statsResp.json();
            
            if (!stats.latestPosition) return;
            
            layer.clearLayers();
            
            const p = stats.latestPosition;
            
            console.log(`?? Latest position: [${p.lat}, ${p.lng}] (seq: ${stats.latestSequence})`);
            
            const marker = L.circleMarker([p.lat, p.lng], {
                radius: 12,  // Doubled from 6
                color: '#d00000',
                weight: 4,   // Doubled from 2
                fillColor: '#ff4d4d',
                fillOpacity: 0.9
            });
            
            marker.bindPopup(`
                <strong style="font-size: 2em;">Current Rover</strong><br/>
                <span style="font-size: 1.8em;">Seq: ${stats.latestSequence}<br/>
                Wind: ${p.windSpeed} m/s<br/>
                Direction: ${p.windDirection} degrees<br/>
                Points: ${lastTrailPointCount}</span>
            `);
            
            // Permanent tooltip label with wind info - TWO ROWS, DOUBLED SIZE
            const labelHtml = `
                <div style="font-size: 24px; font-weight: bold; line-height: 1.3;">
                    <div>${Number(p.windSpeed).toFixed(1)} m/s</div>
                    <div>${p.windDirection} degrees</div>
                </div>
            `;
            marker.bindTooltip(labelHtml, { 
                permanent: true, 
                direction: 'right', 
                offset: [20, 0], 
                className: 'rover-wind-label-large' 
            });
            
            layer.addLayer(marker);
        } catch (error) {
            console.log('? Could not load latest position:', error);
        }
    }

    async function loadCombinedCoverage(layer) {
        try {
            const response = await fetch('/api/combined-coverage');
            if (!response.ok) return;
            
            const geoJson = await response.json();
            
            layer.clearLayers();
            
            L.geoJSON(geoJson, {
                style: {
                    color: '#8B4513',
                    fillColor: '#DEB887',
                    weight: 2,
                    opacity: 0.8,
                    fillOpacity: 0.20,
                    dashArray: '8,4'
                }
            }).addTo(layer);
            
        } catch (error) {
            console.log('? Could not load combined coverage:', error.message);
        }
    }
};
