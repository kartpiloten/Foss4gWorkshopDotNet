using System.Diagnostics;
using NetTopologySuite.Geometries;
using Npgsql;
using NpgsqlTypes;
// use this connection string if your DB is on another machine 
//var connString = "Host=192.168.1.10;Port=5432;Username=anders;Password=tua123;Database=AucklandRoverData";
// use this connection string if your DB is on the same machine as the simulator (server)
var connString = "Host=localhost;Port=5432;Username=anders;Password=tua123;Database=AucklandRoverData";

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("Rover simulator starting. Press Ctrl+C to stop. Press Ctrl+P to toggle progress output.");

// Progress toggle (Ctrl+P) - use a shared state container (declared at end of file)
ProgressState.Enabled = true; // default
_ = Task.Run(async () =>
{
    while (!cts.IsCancellationRequested)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.P && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                ProgressState.Enabled = !ProgressState.Enabled;
                Console.WriteLine($"[progress] {(ProgressState.Enabled ? "ON" : "OFF")}");
            }
        }
        await Task.Delay(50, CancellationToken.None); // small polling delay
    }
});

// Riverhead Forest (approx) bounding box (WGS84: EPSG:4326)
const double minLat = -36.78;
const double maxLat = -36.70;
const double minLon = 174.55;
const double maxLon = 174.70;

// Start near center of the forest
double lat = -36.75;
double lon = 174.60;

// Walking parameters
var rng = new Random();
double bearingDeg = rng.NextDouble() * 360.0; // initial bearing
double walkSpeedMps = 1.4;                     // avg human walking speed
TimeSpan interval = TimeSpan.FromSeconds(2);   // send a point every 2 seconds
double stepMeters = walkSpeedMps * interval.TotalSeconds;

// Wind parameters (simple smooth random walk)
int windDirDeg = rng.Next(0, 360);
double windSpeedMps = 1.0 + rng.NextDouble() * 5.0; // 1–6 m/s typical inside forest
const double maxWindMps = 15.0;

var sessionId = Guid.NewGuid();
int seq = 0;

// Reset the target database on every startup (drop and recreate)
var csb = new NpgsqlConnectionStringBuilder(connString);
var targetDb = csb.Database;
var adminCsb = new NpgsqlConnectionStringBuilder(connString)
{
    Database = "postgres"
};
await ResetDatabaseAsync(adminCsb.ConnectionString, targetDb, cts.Token);

// Build an Npgsql data source with NetTopologySuite enabled for PostGIS geometry mapping
await using var dataSource = new NpgsqlDataSourceBuilder(connString)
    .UseNetTopologySuite()
    .Build();
await using var conn = await dataSource.OpenConnectionAsync(cts.Token);

// Ensure PostGIS, schema and table/columns exist
const string createSql = @"
CREATE EXTENSION IF NOT EXISTS postgis;

CREATE SCHEMA IF NOT EXISTS roverdata;

DROP TABLE IF EXISTS roverdata.rover_measurements;

CREATE TABLE IF NOT EXISTS roverdata.rover_measurements (
    id BIGSERIAL PRIMARY KEY,
    geom geometry(Point, 4326),
    session_id UUID NOT NULL,
    sequence INT NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL,
    latitude DOUBLE PRECISION NOT NULL,
    longitude DOUBLE PRECISION NOT NULL,
    wind_direction_deg SMALLINT NOT NULL,
    wind_speed_mps REAL NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_rover_measurements_session_time
  ON roverdata.rover_measurements (session_id, recorded_at);

CREATE INDEX IF NOT EXISTS ix_rover_measurements_geom
  ON roverdata.rover_measurements USING GIST (geom);
";
await using (var createCmd = new NpgsqlCommand(createSql, conn))
{
    await createCmd.ExecuteNonQueryAsync(cts.Token);
}

// PostGIS types (e.g., geometry) were just created; reload type info for this connection
await conn.ReloadTypesAsync(cts.Token);

// Prepare reusable insert command (now also writing geom)
const string insertSql = @"
INSERT INTO roverdata.rover_measurements
(session_id, sequence, recorded_at, latitude, longitude, wind_direction_deg, wind_speed_mps, geom)
VALUES (@session_id, @sequence, @recorded_at, @latitude, @longitude, @wind_direction_deg, @wind_speed_mps, @geom);";

await using var insert = new NpgsqlCommand(insertSql, conn);
var pSession = insert.Parameters.Add("@session_id", NpgsqlDbType.Uuid);
var pSeq = insert.Parameters.Add("@sequence", NpgsqlDbType.Integer);
var pTs = insert.Parameters.Add("@recorded_at", NpgsqlDbType.TimestampTz);
var pLat = insert.Parameters.Add("@latitude", NpgsqlDbType.Double);
var pLon = insert.Parameters.Add("@longitude", NpgsqlDbType.Double);
var pWdir = insert.Parameters.Add("@wind_direction_deg", NpgsqlDbType.Smallint);
var pWspd = insert.Parameters.Add("@wind_speed_mps", NpgsqlDbType.Real);
var pGeom = insert.Parameters.Add("@geom", NpgsqlDbType.Geometry);

// Pace the loop accurately
var sw = Stopwatch.StartNew();
var nextTick = sw.Elapsed;

try
{
    while (!cts.IsCancellationRequested)
    {
        var now = DateTimeOffset.UtcNow;

        // Update walking direction with slight randomness (keeps mostly forward motion)
        bearingDeg = NormalizeDegrees(bearingDeg + NextGaussian(rng, mean: 0, stdDev: 3));

        // Compute next position using local tangent plane approximation
        Step(ref lat, ref lon, stepMeters, bearingDeg);

        // Keep within bounds; if outside, reflect bearing and step back inside
        if (!InBounds(lat, lon))
        {
            bearingDeg = NormalizeDegrees(bearingDeg + 180);
            Step(ref lat, ref lon, stepMeters, bearingDeg);
            ClampToBounds(ref lat, ref lon);
        }

        // Update wind as a smooth random walk
        windDirDeg = (int)NormalizeDegrees(windDirDeg + NextGaussian(rng, 0, 10));
        windSpeedMps = Math.Clamp(windSpeedMps + NextGaussian(rng, 0, 0.5), 0.0, maxWindMps);

        // Build geometry (NTS Point expects X=lon, Y=lat) with SRID 4326
        var point = new Point(lon, lat) { SRID = 4326 };

        // Insert row
        pSession.Value = sessionId;
        pSeq.Value = seq++;
        pTs.Value = now;
        pLat.Value = lat;
        pLon.Value = lon;
        pWdir.Value = (short)windDirDeg;
        pWspd.Value = (float)windSpeedMps;
        pGeom.Value = point;

        await insert.ExecuteNonQueryAsync(cts.Token);

        // Progress every 10 inserts
        if (ProgressState.Enabled && seq % 10 == 0)
        {
            Console.WriteLine($"[progress] {seq} rows inserted (session {sessionId}) last=({lat:F5},{lon:F5}) wind {windSpeedMps:F1}m/s dir {windDirDeg}°");
        }

        // Wait until next interval tick
        nextTick += interval;
        var delay = nextTick - sw.Elapsed;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cts.Token);
        else
            nextTick = sw.Elapsed; // if we're late, reset schedule
    }
}
catch (OperationCanceledException)
{
    // Graceful shutdown
}

Console.WriteLine("Rover simulator stopped.");

bool InBounds(double la, double lo) =>
    la >= minLat && la <= maxLat && lo >= minLon && lo <= maxLon;

void ClampToBounds(ref double la, ref double lo)
{
    la = Math.Clamp(la, minLat, maxLat);
    lo = Math.Clamp(lo, minLon, maxLon);
}

void Step(ref double la, ref double lo, double distanceMeters, double bearingDegrees)
{
    // Convert bearing to radians
    double b = bearingDegrees * Math.PI / 180.0;

    // Decompose into local N/E components
    double dNorth = distanceMeters * Math.Cos(b);
    double dEast = distanceMeters * Math.Sin(b);

    // Convert meters to degrees
    const double metersPerDegLat = 111_320.0; // near NZ; adequate for small steps
    double metersPerDegLon = 111_320.0 * Math.Cos(la * Math.PI / 180.0);

    la += dNorth / metersPerDegLat;
    lo += dEast / metersPerDegLon;
}

double NormalizeDegrees(double d)
{
    d %= 360.0;
    if (d < 0) d += 360.0;
    return d;
}

double NextGaussian(Random r, double mean, double stdDev)
{
    // Box-Muller
    double u1 = 1.0 - r.NextDouble();
    double u2 = 1.0 - r.NextDouble();
    double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    return mean + z0 * stdDev;
}

// Drops and recreates the target database using an admin connection to 'postgres'.
static async Task ResetDatabaseAsync(string adminConnectionString, string dbName, CancellationToken token)
{
    await using var adminConn = new NpgsqlConnection(adminConnectionString);
    await adminConn.OpenAsync(token);

    // Terminate existing connections to the target DB
    await using (var term = new NpgsqlCommand(
        "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid();",
        adminConn))
    {
        term.Parameters.AddWithValue("@db", dbName);
        await term.ExecuteNonQueryAsync(token);
    }

    // Drop the database if it exists
    await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\";", adminConn))
    {
        await drop.ExecuteNonQueryAsync(token);
    }

    // Recreate the database
    await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\" ENCODING 'UTF8' TEMPLATE template0;", adminConn))
    {
        await create.ExecuteNonQueryAsync(token);
    }
}

// Shared progress state container
static class ProgressState { public static volatile bool Enabled; }
