# Quick Fix: ScentPolygonTester Session Issue

## The Problem

When you start the ScentPolygonTester with PostgreSQL, it shows:
```
Available sessions:
  1. test1
     ?? 0 measurements, last: No measurements yet
```

Even though your RoverSimulator is running and generating measurements.

## Root Cause

The `PostgresRoverDataReader` reads ALL measurements from ALL sessions in the `rover_points` table. It doesn't filter by `session_id`. So when the ScentPolygonService queries for measurements, it gets data from ALL sessions mixed together.

The session query shows "0 measurements" because it's checking the link between `rover_sessions` and `rover_points`, but the reader itself doesn't respect that filter.

## Quick Fix

### Option 1: Wait for Measurements (Simplest)

1. Let your RoverSimulator run for 30 seconds
2. It should write measurements to the database
3. Then start the ScentPolygonTester
4. The session should now show measurements

### Option 2: Check Database Directly

```sql
-- Check if measurements exist
SELECT COUNT(*), session_id 
FROM roverdata.rover_points 
GROUP BY session_id;

-- Check session registration
SELECT * FROM roverdata.rover_sessions;
```

If you see measurements in `rover_points` but they're not showing up, the issue is the reader needs filtering.

## Proper Fix (Requires Code Changes)

The `PostgresRoverDataReader` needs to be made session-aware. This requires:

1. Adding a `session_id` parameter to the reader
2. Filtering all queries by that session
3. Updating ScentPolygonTester to pass the session_id

Would you like me to implement this proper fix?

## Workaround for Now

Use **GeoPackage** mode instead:

1. Edit `ScentPolygonTester\appsettings.json`:
```json
{
  "DatabaseConfiguration": {
    "DatabaseType": "geopackage",
    "GeoPackageFolderPath": "C:\\temp\\Rover1\\"
  }
}
```

2. Make sure your RoverSimulator is using GeoPackage too

3. Run the tester - it will automatically find the session-specific `session_test1.gpkg` file

This works because GeoPackage uses **separate files per session**, so there's no mixing of data!
