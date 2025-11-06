# Foss4gWorkshopDotNet — Architecture Report

Date: 2025-11-04
Branch: moreblazor

This report documents the architecture of the Foss4gWorkshopDotNet repository, the backend database model and schema (PostgreSQL/PostGIS and GeoPackage fallbacks), how backend components interact, and how the frontends read the data. It includes a compact ASCII diagram of data flow, the main tables and columns, and notes on how to run and extend the system.

## High-level overview

The solution is a collection of small .NET projects for simulating rover measurements (RoverSimulator), reading rover data (ReadRoverDBStubLibrary), generating scent polygons (ScentPolygonLibrary), converting wind measurements to polygons (ConvertWinddataToPolygon), and two Blazor frontends (FrontendLeaflet, FrontendOpenLayers) for visualization.

Key responsibilities:
- RoverSimulator: creates session-aware measurement data; stores to either Postgres/PostGIS or GeoPackage file depending on configuration.
- ReadRoverDBStubLibrary: read-only abstraction (IRoverDataReader) with implementations for Postgres and GeoPackage. Provides polling-friendly APIs and a RoverDataMonitor for UI consumers.
- ScentPolygonLibrary: HostedService that reads measurements and converts them to per-measurement scent polygons, per-rover unified polygons, and a combined coverage polygon.
- ConvertWinddataToPolygon: Console tool that reads measurements and exports polygons to GeoPackage and GeoJSON.
- FrontendLeaflet / FrontendOpenLayers: Blazor apps that consume `IRoverDataReader` and `ScentPolygonService` to render maps and serve small JSON API endpoints.

Data sources supported:
- Postgres/PostGIS (recommended for sessions and concurrency)
- OGC GeoPackage (.gpkg) as file-based fallback

## Database model (Postgres/PostGIS)

The Postgres repository (`RoverSimulator/PostgresRoverDataRepository.cs`) sets up a unified schema in schema `roverdata` with two main tables:

1) roverdata.rover_sessions
- id SERIAL PRIMARY KEY
- session_name TEXT UNIQUE NOT NULL
- session_id UUID NOT NULL DEFAULT gen_random_uuid()
- created_at TIMESTAMPTZ DEFAULT NOW()
- last_updated TIMESTAMPTZ DEFAULT NOW()

Purpose: registry of sessions. Each row maps a human-readable session_name to a stable session_id (UUID). The repository uses an INSERT ... ON CONFLICT to create or refresh the session row and returns session_id.

2) roverdata.rover_points
- id BIGSERIAL PRIMARY KEY
- geom geometry(Point,4326)           -- PostGIS point geometry
- rover_id UUID NOT NULL
- rover_name TEXT NOT NULL
- session_id UUID NOT NULL            -- foreign key-like reference to rover_sessions.session_id (not declared as FK in file, but used consistently)
- sequence BIGINT NOT NULL            -- per-measurement sequence number (unique per rover/session)
- recorded_at TIMESTAMPTZ NOT NULL
- latitude DOUBLE PRECISION NOT NULL
- longitude DOUBLE PRECISION NOT NULL
- wind_direction_deg SMALLINT NOT NULL
- wind_speed_mps REAL NOT NULL
- UNIQUE(session_id, rover_id, sequence)

Recommended indices created by the repository initialization:
- ix_rover_points_session_time ON (session_id, recorded_at)
- ix_rover_points_rover_time ON (rover_id, recorded_at)
- ix_rover_points_session_rover ON (session_id, rover_id, sequence)
- ix_rover_points_geom USING GIST (geom)

Notes
- The Postgres repository registers `postgis` extension during initialization.
- The `session_id` value is critical: the simulator registers a session row and stores the DB-provided UUID session_id; this is used to scope data and to support multiple sessions in the same `rover_points` table.

## GeoPackage model (file-based)

GeoPackage fallback uses a single layer named `rover_measurements` (or session-specific session_*.gpkg depending on the repository implementation). The GeoPackage record schema contains fields equivalent to the Postgres columns: rover_id, rover_name, sequence, recorded_at, latitude, longitude, wind_direction_deg, wind_speed_mps and a geometry column (Point) — see `GeoPackageRoverDataRepository.cs` and `GeoPackageRoverDataReader.cs`.

For the ConvertWinddataToPolygon tool, the output GeoPackage contains layers:
- wind_scent_polygons (POLYGON) — individual scent polygons with attributes like session_id, sequence, recorded_at, wind_direction_deg, wind_speed_mps, scent_area_m2, max_distance_m
- unified_scent_coverage (POLYGON) — combined coverage polygon

## Data access and reader abstraction

IRoverDataReader (ReadRoverDBStubLibrary/IRoverDataReader.cs)
- InitializeAsync()
- GetMeasurementCountAsync()
- GetAllMeasurementsAsync()
- GetNewMeasurementsAsync(int lastSequence)
- GetNewMeasurementsSinceAsync(DateTimeOffset sinceUtc)
- GetLatestMeasurementAsync()

RoverMeasurement record fields (used across projects):
- Guid RoverId
- string RoverName
- Guid SessionId
- int Sequence
- DateTimeOffset RecordedAt
- double Latitude
- double Longitude
- short WindDirectionDeg
- float WindSpeedMps
- NetTopologySuite.Geometries.Point Geometry

Postgres reader specifics (ReadRoverDBStubLibrary/PostgresRoverDataReader.cs):
- Uses NpgsqlDataSourceBuilder().UseNetTopologySuite() to map PostGIS geometry to NTS Point.
- Session-aware: optional sessionId can be passed to the reader to filter queries using WHERE session_id = @sessionId.
- Queries read the following columns in this order: rover_id, rover_name, session_id, sequence, recorded_at, latitude, longitude, wind_direction_deg, wind_speed_mps, geom
- Methods return lists of RoverMeasurement and support timestamp-based or sequence-based incremental polling.

RoverDataMonitor (ReadRoverDBStubLibrary/RoverDataMonitor.cs)
- Polls via IRoverDataReader.GetNewMeasurementsAsync(_lastKnownSequence)
- Caches measurements in a ConcurrentDictionary keyed by Sequence.
- Exposes events DataUpdated and StatusUpdate for UI components.

## Backend services and processing

RoverSimulator
- Creates a repository (PostgresRoverDataRepository or GeoPackageRoverDataRepository) via `DatabaseService`.
- Calls InitializeAsync() to create schema and register the session (capturing session_id).
- Runs a fixed-interval loop that updates rover position and wind attributes, then calls repository.InsertMeasurementAsync(measurement).

PostgresRoverDataRepository
- InitializeAsync creates the schema (postgis extension, rover_sessions and rover_points), registers session row and obtains session_id (GUID), prepares a reusable insert command with parameters, and reloads PostGIS types.
- InsertMeasurementAsync sets parameter values and executes the prepared statement.
- ResetDatabaseAsync can delete data for a session using session_name -> session_id mapping and DELETE FROM rover_points WHERE session_id = (...)

ScentPolygonService (ScentPolygonLibrary)
- HostedService that initializes an IRoverDataReader and periodically polls for new measurements (using a timestamp watermark via GetNewMeasurementsSinceAsync).
- For each measurement it generates a scent polygon (ScentPolygonCalculator), stores ScentPolygonResult, and maintains per-rover RoverPolygonProcessor to create per-rover unified polygons.
- Maintains a cached unified polygon (GetUnifiedScentPolygonCached) combining per-rover unified polygons for efficient recompute.
- Exposes events: PolygonsUpdated, StatusUpdate, ForestCoverageUpdated (forest GeoPackage optional).

ConvertWinddataToPolygon
- Reads all measurements from IRoverDataReader and generates wind_scent_polygons and unified_scent_coverage layers in an output GeoPackage and exports GeoJSON.
- Uses approximate degree->meter conversion to calculate scent_area_m2.
- Bulk inserts into GeoPackage layers and creates spatial indices where applicable.

Frontend APIs (FrontendLeaflet/Program.cs)
- Lightweight HTTP endpoints are registered in the Blazor host to provide JSON: /api/rover-data, /api/rover-trail, /api/rover-stats, /api/rover-sample, /api/combined-coverage, /api/forest, /api/forest-bounds
- The frontend depends on a singleton IRoverDataReader instance created at startup; the reader is initialized synchronously during startup in the current code.
- ScentPolygonService is registered as a singleton and hosted service in FrontendLeaflet to provide combined coverage endpoints.

## Data flow (ASCII diagram)

Client (browser)               Blazor frontend (Backend)                 DB / File
   |                                 |                                     |
   | REST / SignalR / Blazor pages    |                                     |
   |---- GET /api/rover-data -------->|                                     |
   |                                  | IRoverDataReader (singleton)        |
   |                                  |                                     |
   |                                  |- If Postgres -> Npgsql -> SQL -----|
   |                                  |   SELECT ... FROM roverdata.rover_points
   |                                  |    WHERE session_id = ?  (optional)
   |                                  |                                     |
   |                                  |- If GeoPackage -> MapPiloteGeoPkg  |
   |                                  |   -> read features from layer        |
   | JSON (FeatureCollection)         |                                     |

Background data generation:

RoverSimulator -> IRoverDataRepository (PostgresRoverDataRepository or GeoPackageRoverDataRepository)
   InsertMeasurementAsync -> INSERT INTO roverdata.rover_points (...) VALUES (...)

ScentPolygonService reads via IRoverDataReader (polling) and computes polygons, emits events used by UI and writes GeoPackage via ConvertWinddataToPolygon if requested.

## Key SQL snippets

- Session registration (returns session_id):
  INSERT INTO roverdata.rover_sessions (session_name)
  VALUES (@session_name)
  ON CONFLICT (session_name) DO UPDATE SET last_updated = NOW()
  RETURNING session_id;

- Insert measurement (prepared statement):
  INSERT INTO roverdata.rover_points
    (rover_id, rover_name, session_id, recorded_at, latitude, longitude, wind_direction_deg, wind_speed_mps, geom, sequence)
  VALUES (@rover_id, @rover_name, @session_id, @recorded_at, @latitude, @longitude, @wind_direction_deg, @wind_speed_mps, @geom, @sequence);

- Delete session data (reset):
  DELETE FROM roverdata.rover_points
  WHERE session_id = (SELECT session_id FROM roverdata.rover_sessions WHERE session_name = @session_name);

- Measurement reads (examples):
  SELECT rover_id, rover_name, session_id, sequence, recorded_at, latitude, longitude, wind_direction_deg, wind_speed_mps, geom
  FROM roverdata.rover_points
  WHERE sequence > @lastSequence AND session_id = @sessionId
  ORDER BY sequence ASC;

  SELECT ... WHERE recorded_at > @sinceUtc AND session_id = @sessionId ORDER BY recorded_at ASC;

## How to run (notes)

1) RoverSimulator (generate data)
- Navigate to `RoverSimulator` project directory and edit `appsettings.json` to select Database.Type = "postgres" or "geopackage". For Postgres, set `Postgres.ConnectionString`.
- Run the simulator: dotnet run --project RoverSimulator
- The simulator will: initialize repository (create schema), register a session, and start inserting measurements. It prints the SessionId and session name.

2) Frontend (visualization)
- By default FrontendLeaflet expects an IRoverDataReader configured in its appsettings.json. The service is a singleton that will use Postgres if available or GeoPackage as a fallback.
- Run the Blazor host: dotnet run --project FrontendLeaflet
- Open browser to the configured address and hit the API endpoints described above.

3) ConvertWinddataToPolygon (export)
- dotnet run --project ConvertWinddataToPolygon
- The tool will attempt to connect to Postgres first (if configured) and fall back to GeoPackage file; it outputs a GeoPackage and GeoJSON with polygon layers.

4) ScentPolygonTester (diagnosis)
- This project is useful for verifying that Postgres reader is session-aware. See `ScentPolygonTester/SESSION-ISSUE-FIX.md` and `SESSION-AWARE-COMPLETE.md` for notes.

## Observations, edge-cases and recommended improvements

1) Session-awareness
- Postgres reader and repository are session-aware; ensure callers pass or use the DB-assigned session_id consistently. There are historical notes in `SESSION-ISSUE-FIX.md` about earlier bugs where session filtering was missing.

2) Concurrency and scalability
- The Postgres schema stores all sessions in a single rover_points table with proper indices. This is scalable for large point counts; the GIST index on `geom` allows spatial queries.
- When serving large datasets, the frontend already provides sampling and trail endpoints to reduce payload.

3) Type mapping and SRIDs
- All geometry uses SRID 4326 (WGS84), mapped via Npgsql.UseNetTopologySuite() for Postgres and via MapPiloteGeopackageHelper for GeoPackage.

4) Consistency checks
- When inserting into Postgres, the code attempts to use a session_id from the measurement or repository; ensure no zero-GUIDs are used in production.

5) Tests and validation
- The project contains verification utilities (WindPolygonVerifier) and ScentPolygonTester. Adding small unit tests for Postgres reader query behavior (session filter, timestamp filtering) would help prevent regressions.

## Files referenced (key)
- ReadRoverDBStubLibrary/
  - IRoverDataReader.cs
  - PostgresRoverDataReader.cs
  - GeoPackageRoverDataReader.cs
  - RoverDataMonitor.cs
- RoverSimulator/
  - PostgresRoverDataRepository.cs
  - GeoPackageRoverDataRepository.cs
  - RoverSimulatorProgram.cs
- ScentPolygonLibrary/
  - ScentPolygonService.cs
  - ScentPolygonCalculator.cs
- ConvertWinddataToPolygon/
  - Program.cs
  - GeoJsonExporter.cs
- FrontendLeaflet/
  - Program.cs (API endpoints)

## Closing summary

The repository is organized into small, focused projects that together provide a workflow to simulate rover-derived wind measurements, persist them to Postgres/PostGIS or GeoPackage, compute scent-detection polygons, export polygons, and serve them to light-weight Blazor frontends. The Postgres schema centers on a session-aware `rover_points` table accompanied by a `rover_sessions` registry. The `IRoverDataReader` abstraction and `RoverDataMonitor` decouple polling and UI consumption from the underlying storage. The ScentPolygonService builds on those abstractions to continuously compute and cache per-measurement and unified polygons.

If you want, I can:
- Add a diagram file (SVG) or Mermaid graph to the repo for nicer visuals.
- Add unit tests that validate Postgres session filtering.
- Create a small README section with quick commands to run the simulator + frontend using Dockerized Postgres for an easy reproducible environment.

---
Report generated automatically by analysis of repository code (files: ReadRoverDBStubLibrary, RoverSimulator, ScentPolygonLibrary, ConvertWinddataToPolygon, FrontendLeaflet).