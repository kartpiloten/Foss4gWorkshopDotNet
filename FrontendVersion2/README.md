
# BlazorLeafletHello — Full Demo (Leaflet + PostGIS + NetTopologySuite)

## What you get
- **Leaflet** front end with **layer control** (Points, Lines, Polygons).
- Hover **tooltips** showing feature names.
- Click map to create an **NTS buffer** (meters) and compute **intersections** with PostGIS polygons.
- Minimal APIs:
  - `/api/hello-geojson`
  - `/api/postgis/points?limit=1000`
  - `/api/postgis/lines?limit=500`
  - `/api/postgis/polygons?limit=500`
  - `/api/nts/buffer?lng=..&lat=..&meters=..`
  - `/api/nts/intersections?lng=..&lat=..&meters=..&limit=..`

## Configure PostGIS
Edit `appsettings.json` and set your PostgreSQL connection string.

### Create demo tables
```sql
CREATE EXTENSION IF NOT EXISTS postgis;

-- Points
CREATE TABLE IF NOT EXISTS public.poi_demo (
  id   serial PRIMARY KEY,
  name text,
  geom geometry(Point, 4326)
);

INSERT INTO public.poi_demo(name, geom) VALUES
('Östersund Center', ST_SetSRID(ST_MakePoint(14.6357, 63.1792), 4326)),
('Åre',               ST_SetSRID(ST_MakePoint(13.0007, 63.3997), 4326)),
('Sundsvall',         ST_SetSRID(ST_MakePoint(17.3069, 62.3908), 4326));

-- Lines
CREATE TABLE IF NOT EXISTS public.lines_demo (
  id   serial PRIMARY KEY,
  name text,
  geom geometry(LineString, 4326)
);

INSERT INTO public.lines_demo(name, geom) VALUES
('River sample', ST_GeomFromText('LINESTRING(14.5 63.0, 14.7 63.2, 14.9 63.3)', 4326));

-- Polygons
CREATE TABLE IF NOT EXISTS public.polys_demo (
  id   serial PRIMARY KEY,
  name text,
  geom geometry(Polygon, 4326)
);

INSERT INTO public.polys_demo(name, geom) VALUES
('Demo polygon A', ST_GeomFromText('POLYGON((14.5 63.05, 14.9 63.05, 14.9 63.30, 14.5 63.30, 14.5 63.05))', 4326)),
('Demo polygon B', ST_Buffer(ST_SetSRID(ST_MakePoint(14.2, 63.1),4326)::geography, 10000)::geometry);
```

## Run
1. Open `BlazorLeafletHello.sln` in Visual Studio 2022 (.NET 8).
2. Update `appsettings.json` with your DB credentials.
3. Press **F5**.
   - `/` — Hello Leaflet
   - `/postgis-demo` — Layers + click-to-buffer + intersections

## Notes
- Buffering in **meters** uses **Web Mercator (EPSG:3857)** internally via ProjNet for a practical planar approximation.
- Intersections are computed in C# with **NetTopologySuite** after fetching candidate polygons from PostGIS using a bounding-box filter for efficiency.
- Feel free to swap EPSG:3857 for a local projected CRS for higher accuracy in your region.
