
window.initLeafletMap = async function () {
  const map = L.map('map').setView([63.1792, 14.6357], 12);
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; OpenStreetMap contributors'
  }).addTo(map);

  const marker = L.marker([63.1792, 14.6357]).addTo(map);
  marker.bindPopup('<b>Hello FOSS4G!</b><br/>Blazor + Leaflet').openPopup();

  try {
    const resp = await fetch('/api/hello-geojson');
    if (resp.ok) {
      const gj = await resp.json();
      L.geoJSON(gj, {
        onEachFeature: (f, layer) => {
          const name = f.properties?.name ?? 'Feature';
          layer.bindPopup(`GeoJSON: ${name}`);
        },
        pointToLayer: (feature, latlng) => L.circleMarker(latlng, { radius: 6 })
      }).addTo(map);
    }
  } catch (e) {
    console.error('Failed to load GeoJSON', e);
  }
}
