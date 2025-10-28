# System Application Flow

Detta diagram visar det kompletta dataflödet i systemet, från simulering till visualisering.

```mermaid
%%{init: {'theme':'dark', 'themeVariables': { 'primaryColor': '#ffffff', 'primaryTextColor': '#000000', 'primaryBorderColor': '#cccccc', 'lineColor': '#ffffff', 'secondaryColor': '#444444', 'tertiaryColor': '#333333', 'background': '#000000', 'mainBkg': '#222222', 'secondBkg': '#333333', 'tertiaryBkg': '#444444'}}}%%
flowchart TD
    %% Data Generation
    RoverSim["`**🤖 RoverSimulator**
    Data Generator
    - Generates rover positions
    - Simulates wind data
    - Writes to database`"]
    
    %% Database
    Database["`**💾 Database**
    Data Storage
    - GeoPackage files
    - PostgreSQL/PostGIS
    - Forest boundaries`"]
    
    %% Data Access
    RoverReader["`**🧾 IRoverDataReader**
    Data Access (interface)
    - GeoPackageRoverDataReader
    - PostgresRoverDataReader`"]
    
    %% Backend Services
    ScentService["`**🧪 ScentPolygonService**
    Background Service
    - Polls every 1s
    - Generates polygons
    - Caches unified result`"]
    
    %% Web Application
    Program["`**⚙️ Program.cs**
    Web Server
    - Service registration
    - API endpoints
    - Middleware`"]
    
    API["`**🔌 REST API**
    Endpoints
    /api/rover-trail
    /api/combined-coverage
    /api/rover-stats
    /api/forest`"]
    
    %% Frontend
    Browser["`**🌐 Browser**
    Leaflet Map
    - Polls API every 8s
    - Displays trail
    - Shows coverage`"]
    
    %% Main data flow
    RoverSim -->|"Inserts measurements"| Database
    RoverReader -->|"Queries database"| Database
    ScentService -->|"Reads data via"| RoverReader
    ScentService -->|"Generates & caches unified polygon"| ScentService
    
    Program -->|"Registers"| ScentService
    Program -->|"Registers"| RoverReader
    Program -->|"Exposes"| API
    
    API -->|"Reads cache"| ScentService
    API -->|"Reads data"| RoverReader
    
    Browser -->|"AJAX every 8s"| API
    
    %% Styling
    classDef generator fill:#ff9999,stroke:#000000,stroke-width:3px,color:#000000
    classDef data fill:#ffcc99,stroke:#000000,stroke-width:3px,color:#000000
    classDef service fill:#99ff99,stroke:#000000,stroke-width:3px,color:#000000
    classDef web fill:#99ccff,stroke:#000000,stroke-width:3px,color:#000000
    classDef frontend fill:#ffffff,stroke:#000000,stroke-width:3px,color:#000000
    
    class RoverSim generator
    class Database data
    class RoverReader,ScentService,ScentLib service
    class Program,API web
    class Browser frontend
```

## Komponenter och Dataflöde

### 1. Data Generation (Röd)
**RoverSimulator** - Konsolapplikation som:
- Genererar simulerad rover-position baserat på skog-gränser från GeoPackage
- Simulerar vinddata (hastighet och riktning)
- Skriver mätningar till databas var 1 sekund
- Stödjer både GeoPackage och PostgreSQL/PostGIS

### 2. Data Storage (Orange)
**Database** - Lagring av mätdata:
- **GeoPackage**: OGC-standard, SQLite-baserad, filbaserad spatial data
- **PostgreSQL/PostGIS**: Server-baserad spatial databas
- Innehåller: rover_measurements tabell med position, vind, timestamp

### 3. Data Access (Grön)
**IRoverDataReader** - Abstraktion med två implementationer:
- **GeoPackageRoverDataReader**: Läser från .gpkg filer
- **PostgresRoverDataReader**: Läser från PostgreSQL via Npgsql + NetTopologySuite
- API: `GetAllMeasurementsAsync()`, `GetLatestMeasurementAsync()`, `GetNewMeasurementsAsync()`

### 4. Background Processing (Grön)
**ScentPolygonService** - Hosted service som:
- Pollar IRoverDataReader var 1 sekund
- Genererar doftpolygoner från nya mätningar
- Använder **ScentPolygonLibrary** för geometriberäkningar
- Cachar unified polygon för prestanda
- Exponerar `GetUnifiedScentPolygonCached()` för API

### 5. Web Server (Blå)
**Program.cs** - ASP.NET Core web application:
- Registrerar services via Dependency Injection
- Mappar REST API endpoints
- Serverar Blazor-komponenter

**REST API** - Endpoints:
- `/api/rover-trail` - LineString med rover-spår
- `/api/combined-coverage` - Unified doftpolygon från cache
- `/api/rover-stats` - Statistik (antal mätningar, senaste position)
- `/api/forest` - Skoggräns-polygon från GeoPackage

### 6. Frontend (Vit)
**Browser med Leaflet.js**:
- Pollar API var 8 sekund
- Visar karta med OpenStreetMap tiles
- Ritar rover trail (blå linje)
- Ritar coverage polygon (röd)
- Visar aktuell position (markör)

## Dataflöde i Realtid

1. **RoverSimulator** skriver mätning → **Database** (var 1s)
2. **ScentPolygonService** upptäcker ny data → genererar polygon → uppdaterar cache (var 1s)
3. **Browser** hämtar `/api/rover-trail` och `/api/combined-coverage` → uppdaterar karta (var 8s)

### Timing
- **Backend poll**: 1 sekund (ScentPolygonService → IRoverDataReader)
- **Frontend poll**: 8 sekunder (Browser → REST API)
- **Simulator**: 1 sekund per mätning
