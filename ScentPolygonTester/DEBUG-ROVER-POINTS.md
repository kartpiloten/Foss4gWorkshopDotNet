# Debug: Check rover_points table

## Run these SQL queries to diagnose:

```sql
-- 1. Check if there are ANY measurements in rover_points
SELECT COUNT(*) as total_measurements
FROM roverdata.rover_points;

-- 2. Check measurements by session_id
SELECT 
    session_id,
    COUNT(*) as measurement_count,
    MIN(recorded_at) as first_measurement,
    MAX(recorded_at) as last_measurement,
    COUNT(DISTINCT rover_id) as rover_count
FROM roverdata.rover_points
GROUP BY session_id
ORDER BY MAX(recorded_at) DESC;

-- 3. Check if measurements exist for test1 session
SELECT COUNT(*) as test1_measurements
FROM roverdata.rover_points
WHERE session_id = '6656b24b-bafd-455c-a1b8-4412fceadb8f';

-- 4. Check what rover_names are writing data
SELECT 
    rover_name,
    session_id,
    COUNT(*) as count,
    MAX(recorded_at) as latest
FROM roverdata.rover_points
GROUP BY rover_name, session_id
ORDER BY MAX(recorded_at) DESC;

-- 5. Check the most recent 10 measurements
SELECT 
    rover_name,
    session_id,
    sequence,
    recorded_at,
    latitude,
    longitude
FROM roverdata.rover_points
ORDER BY recorded_at DESC
LIMIT 10;
```

## What to look for:

1. **If total_measurements = 0:**
   - Your rovers are NOT writing to PostgreSQL at all
   - Check rover console output for errors
   - Check appsettings.json DatabaseType

2. **If measurements exist but session_id ? test1:**
   - Your rovers created different sessions
   - The session wasn't registered in rover_sessions table (bug!)

3. **If measurements exist with session_id = test1:**
   - The LEFT JOIN in the session listing query is broken
   - Or there's a timing issue

## Most Likely Issue:

Your rovers are probably:
- Writing to GeoPackage files instead of PostgreSQL, OR
- Writing to PostgreSQL but with a session_id that's not in rover_sessions table

Check your RoverSimulator console output - what does it say?
