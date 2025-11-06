// ====== Multi-Rover Leaflet Map ======
// Displays per-rover trails and current positions + combined coverage

console.log('Loading Rover Tracker (Leaflet multi-rover mode)...');

const roverPolylines = new Map(); // roverId -> polyline
const roverCoords = new Map(); // roverId -> [[lat,lng], ...]
const roverMarkers = new Map(); // roverId -> marker

window.initLeafletMap = async function() {
    console.log('Initializing Leaflet map (multi-rover)...');

    let mapCenter = [-36.718362,174.577555];
    let mapZoom =12;

    try {
        const boundsResponse = await fetch('/api/forest-bounds');
        if (boundsResponse.ok) {
            const bounds = await boundsResponse.json();
            mapCenter = [bounds.center.lat, bounds.center.lng];
            mapZoom =14;
        }
    } catch {}

    const map = L.map('map', { zoomControl: false }).setView(mapCenter, mapZoom);
    L.control.zoom({ position: 'bottomleft' }).addTo(map);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom:19,
        attribution: '© OpenStreetMap'
    }).addTo(map);

    const forestLayer = L.layerGroup().addTo(map);
    const coverageLayer = L.layerGroup().addTo(map);
    const currentPositionsLayer = L.layerGroup().addTo(map);
    const trailsLayer = L.layerGroup().addTo(map);

    await loadForestBoundary(forestLayer);
    await loadCombinedCoverage(coverageLayer);
    await loadAllTrails(trailsLayer);
    await loadAllLatestPositions(currentPositionsLayer);

    setInterval(async () => {
        await loadAllTrails(trailsLayer); // reload per-rover trails (cheap enough)
        await loadAllLatestPositions(currentPositionsLayer);
        await loadCombinedCoverage(coverageLayer);
    },8000);

    // ====== Helper Functions ======
    
    async function loadForestBoundary(layer) {
        try {
            const response = await fetch('/api/forest');
            if (!response.ok) return;
            const geoJson = await response.json();
            layer.clearLayers();
            L.geoJSON(geoJson, { style: { color: '#2d5a27', fillColor: '#4a7c59', weight:2, opacity:0.8, fillOpacity:0.15 } }).addTo(layer);
        } catch (error) { console.log('Forest boundary error:', error.message); }
    }

    async function loadAllTrails(layer) {
        try {
            const response = await fetch('/api/rover-trails');
            if (!response.ok) return;
            const geo = await response.json();
            layer.clearLayers();
            if (!geo.features?.length) return;

            geo.features.forEach(f => {
                if (f.geometry?.type !== 'LineString') return;
                const coords = f.geometry.coordinates.map(c => [c[1], c[0]]);
                const roverId = f.properties?.roverId;
                roverCoords.set(roverId, coords);
                const color = colorForRover(roverId);
                const poly = L.polyline(coords, { color, weight:4, opacity:0.85 });
                poly.addTo(layer).bindPopup(`<strong>${escapeHtml(f.properties?.roverName || 'Rover')}</strong><br/>Points: ${coords.length}`);
                roverPolylines.set(roverId, poly);
            });
        } catch (e) { console.log('Trails load failed', e); }
    }

    async function loadAllLatestPositions(layer) {
        try {
            const resp = await fetch('/api/rovers-stats');
            if (!resp.ok) return;
            const data = await resp.json();
            if (!data.rovers) return;
            layer.clearLayers();

            data.rovers.forEach(r => {
                const pos = r.position;
                const roverId = r.roverId;
                const color = colorForRover(roverId);
                const marker = L.circleMarker([pos.lat, pos.lng], { radius:10, color, weight:4, fillColor: color, fillOpacity:0.9 });
                marker.bindPopup(`
                    <strong style="font-size:1.4em;">${escapeHtml(r.roverName)}</strong><br/>
                    <span>Seq: ${r.latestSequence}</span><br/>
                    <span>Wind: ${Number(r.windSpeed).toFixed(1)} m/s</span><br/>
                    <span>Direction: ${r.windDirection}°</span>
                `);
                const labelHtml = `
                    <div style="font-size:18px; font-weight: bold; line-height:1.3;">
                        <div>${escapeHtml(r.roverName)} — ${Number(r.windSpeed).toFixed(1)} m/s</div>
                        <div>${r.windDirection} degrees</div>
                    </div>`;
                marker.bindTooltip(labelHtml, { permanent: true, direction: 'right', offset: [20,0], className: 'rover-wind-label-large' });
                marker.addTo(layer);
                roverMarkers.set(roverId, marker);
            });
        } catch (error) { console.log('Latest positions error:', error); }
    }

    async function loadCombinedCoverage(layer) {
        try {
            const response = await fetch('/api/combined-coverage');
            if (!response.ok) return;
            const geoJson = await response.json();
            layer.clearLayers();
            L.geoJSON(geoJson, { style: { color: '#8B4513', fillColor: '#DEB887', weight:2, opacity:0.8, fillOpacity:0.20, dashArray: '8,4' } }).addTo(layer);
        } catch (error) { console.log('Combined coverage error:', error.message); }
    }

    function colorForRover(roverId) {
        const palette = ['#e41a1c', '#377eb8', '#4daf4a', '#984ea3', '#ff7f00', '#a65628'];
        const idx = Math.abs(hashCode(roverId || '')) % palette.length;
        return palette[idx];
    }

    function hashCode(str) {
        let h =0; for (let i =0; i < String(str).length; i++) { h = (h <<5) - h + String(str).charCodeAt(i); h |=0; }
        return h;
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"]+/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
    }
};
