# Quick Diagnosis: Check PostgreSQL Sessions

## Run this SQL to see what's in the database:

```sql
-- 1. Check all sessions
SELECT 
    rs.session_name,
  rs.session_id,
    COUNT(rp.id) as measurement_count,
    MAX(rp.recorded_at) as last_measurement
FROM roverdata.rover_sessions rs
LEFT JOIN roverdata.rover_points rp ON rs.session_id = rp.session_id
GROUP BY rs.session_name, rs.session_id
ORDER BY MAX(rp.recorded_at) DESC NULLS LAST;

-- 2. Check if there are measurements NOT linked to sessions
SELECT COUNT(*) as orphan_measurements
FROM roverdata.rover_points rp
WHERE NOT EXISTS (
    SELECT 1 FROM roverdata.rover_sessions rs 
    WHERE rs.session_id = rp.session_id
);

-- 3. Check what session_ids are actually in rover_points
SELECT DISTINCT session_id, COUNT(*) as count
FROM roverdata.rover_points
GROUP BY session_id
ORDER BY count DESC;
```

## Likely Issue

Your rovers are probably creating **new auto-generated sessions** (like `auto_20251031_143022_a3b5c7`) but you're trying to monitor the old "test1" session which has no new data.

## Quick Fix

1. **Check what sessions your rovers created:**
   - Look at the RoverSimulator console output
   - It should say something like: `Session: auto_20251031_143022_a3b5c7`

2. **Run the tester again and select the NEW session:**
   ```
   cd ScentPolygonTester
   dotnet run
   # Select the auto_XXXXX session, not test1
   ```

## Alternative: Start rovers with specific session

```powershell
# Stop all rovers
Get-Process RoverSimulator -ErrorAction SilentlyContinue | Stop-Process -Force

# Start rovers with specific session name
cd RoverSimulator
.\start-rover.ps1 Rover1 test1
.\start-rover.ps1 Rover2 test1
```

Now both rovers will write to "test1" session!
