Här är en komplett plan med alla ändringar, inklusive full avveckling av kartrelaterade API:er och övergång till ren DI-baserad Blazor-styrning med binär JS Interop. Planen är uppdelad i faser för maximal tydlighet och minimal risk. Alla ändringar bygger på din befintliga kodbas och kräver inga externa beroenden.

---

### FAS 1: Skapa ny Blazor-komponent för kartan

Skapa filen Components/ScentMap.razor med följande innehåll:

- En div med dynamiskt ID
- @inject IJSRuntime JS
- Parametrar: Id (default Guid)
- Privata fält: IJSObjectReference _module, LineString _trail, Polygon _coverage, Point _latestPosition, float _windSpeed, short _windDirection
- OnAfterRenderAsync(firstRender): importera "./js/scent-map.js" och anropa initMap(Id)
- Metoder:
  - UpdateTrailAsync(LineString trail): konvertera till float[] och anropa updateTrail
  - UpdateCoverageAsync(Polygon coverage): konvertera ExteriorRing till float[] och anropa updateCoverage
  - UpdatePositionAsync(Point position, float windSpeed, short windDirection): anropa updatePosition med X, Y, windSpeed, windDirection
  - AppendTrailCoordsAsync(Coordinate[] newCoords): konvertera till float[] och anropa appendTrail
- Extension-metod: Coordinate[].ToFloatArray() → float[] med X,Y som lng,lat
- DisposeAsync: frigör JS-modul

---

### FAS 2: Skapa JS-wrapper för Leaflet (binär, återanvändande lager)

Skapa filen wwwroot/js/scent-map.js:

- Globalt objekt maps = {}
- initMap(id): skapa L.map, lägg till OSM-tiles, zoom bottomleft, ladda /api/forest en gång med L.geoJSON
- updateTrail(id, floatArray): konvertera till [lat,lng], setLatLngs på befintlig polyline eller skapa ny
- updateCoverage(id, floatArray): samma för polygon, återanvänd lager
- updatePosition(id, lng, lat, windSpeed, windDirection): setLatLng på cirkelmarkör, bindTooltip med stor text
- appendTrail(id, floatArray): lägg till nya koordinater i slutet av befintlig trail, setLatLngs

---

### FAS 3: Omskriv Index.razor till DI-baserad polling

Ersätt hela innehållet i Pages/Index.razor:

- @page "/", @inject IRoverDataReader Reader, @inject ScentPolygonService ScentService, @inject IJSRuntime JS
- Behåll CSS och overlay-top-left med ForestCoveragePie
- Lägg till <ScentMap @ref="_map" />
- @code:
  - Privat: ScentMap _map, Timer _timer, string _error, int _lastSequence = -1
  - OnInitializedAsync: anropa LoadInitialDataAsync(), starta Timer var 8000 ms med LoadIncrementalDataAsync()
  - LoadInitialDataAsync():
    - Hämta alla mätningar → LineString → UpdateTrailAsync
    - ScentService.GetUnifiedScentPolygonCached()?.Polygon → UpdateCoverageAsync
    - Reader.GetLatestMeasurementAsync() → UpdatePositionAsync
  - LoadIncrementalDataAsync():
    - Hämta senaste → jämför Sequence
    - Om ny: GetNewMeasurementsAsync(_lastSequence) → Coordinate[] → AppendTrailCoordsAsync
    - Uppdatera _lastSequence
    - Alltid: uppdatera coverage och position
  - Dispose: _timer.Dispose()

---

### FAS 4: Avveckla kartrelaterade API:er

I Program.cs, ta bort följande endpoints helt:

- app.MapGet("/api/rover-trail", ...)
- app.MapGet("/api/combined-coverage", ...)
- app.MapGet("/api/rover-stats", ...)
- app.MapGet("/api/rover-data", ...)
- app.MapGet("/api/rover-sample", ...)

Behåll endast:

- /api/test
- /api/forest
- /api/forest-bounds

---

### FAS 5: Inaktivera JS-polling i leafletInit.js

I wwwroot/js/leafletInit.js:

- Kommentera ut eller ta bort:
  - setInterval(...)
  - appendNewTrailPoints()
  - loadLatestPosition(currentPositionLayer)
  - loadCombinedCoverage(coverageLayer)
- Behåll endast initLeafletMap med initial loadForestBoundary och init av map
- Alternativt: ta bort filen helt och ta bort <script>-referens i Index.razor (om du inte använder den längre)

---
