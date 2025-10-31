/*
 The functionallity in this file is:
 - Implement a PostgreSQL/PostGIS-backed repository using Npgsql and NetTopologySuite (geometry mapping).
 - Create/reset schema and insert measurements efficiently using a prepared command.
 - Keep initialization and error messages concise for workshop use.
*/

using Npgsql; // ADO.NET provider for PostgreSQL
using NpgsqlTypes; // Npgsql type mappings (includes Geometry with NTS)

namespace RoverSimulator;

public class PostgresRoverDataRepository : RoverDataRepositoryBase
{
    private NpgsqlDataSource? _dataSource;
    private NpgsqlConnection? _connection;
    private NpgsqlCommand? _insertCommand;

    public PostgresRoverDataRepository(string connectionString, string sessionTableName) 
        : base(connectionString, sessionTableName)
    {
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
{
Console.WriteLine("Initializing PostgreSQL connection...");
   
         _dataSource = new NpgsqlDataSourceBuilder(_connectionString)
     .UseNetTopologySuite() // Enable PostGIS <-> NTS (NetTopologySuite) geometry mapping
       .Build();

  Console.WriteLine("Opening database connection...");
    _connection = await _dataSource.OpenConnectionAsync(cancellationToken);

       Console.WriteLine($"Setting up database schema for session '{_sessionTableName}'...");
   
   // Create unified schema with rover_sessions and rover_points tables
  var createSql = @"
CREATE EXTENSION IF NOT EXISTS postgis;

CREATE SCHEMA IF NOT EXISTS roverdata;

-- Sessions tracking table (registry of all sessions)
CREATE TABLE IF NOT EXISTS roverdata.rover_sessions (
    id SERIAL PRIMARY KEY,
    session_name TEXT UNIQUE NOT NULL,
    session_id UUID NOT NULL DEFAULT gen_random_uuid(),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    last_updated TIMESTAMPTZ DEFAULT NOW()
);

-- Unified rover points table (all measurements from all sessions)
CREATE TABLE IF NOT EXISTS roverdata.rover_points (
    id BIGSERIAL PRIMARY KEY,
    geom geometry(Point,4326),
    rover_id UUID NOT NULL,
    rover_name TEXT NOT NULL,
    session_id UUID NOT NULL,
    sequence BIGINT NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL,
    latitude DOUBLE PRECISION NOT NULL,
    longitude DOUBLE PRECISION NOT NULL,
    wind_direction_deg SMALLINT NOT NULL,
    wind_speed_mps REAL NOT NULL,
    UNIQUE(session_id, rover_id, sequence)
);

CREATE INDEX IF NOT EXISTS ix_rover_points_session_time
  ON roverdata.rover_points (session_id, recorded_at);

CREATE INDEX IF NOT EXISTS ix_rover_points_rover_time
  ON roverdata.rover_points (rover_id, recorded_at);

CREATE INDEX IF NOT EXISTS ix_rover_points_session_rover
  ON roverdata.rover_points (session_id, rover_id, sequence);

CREATE INDEX IF NOT EXISTS ix_rover_points_geom
  ON roverdata.rover_points USING GIST (geom);
";
   await using var createCmd = new NpgsqlCommand(createSql, _connection);
            await createCmd.ExecuteNonQueryAsync(cancellationToken);

       // Register this session in the sessions table and get the session_id
 var registerSessionSql = @"
INSERT INTO roverdata.rover_sessions (session_name)
VALUES (@session_name)
ON CONFLICT (session_name) DO UPDATE SET last_updated = NOW()
RETURNING session_id;";
    
   await using var registerCmd = new NpgsqlCommand(registerSessionSql, _connection);
          registerCmd.Parameters.AddWithValue("@session_name", _sessionTableName);
var retrievedSessionId = await registerCmd.ExecuteScalarAsync(cancellationToken);
       
    Console.WriteLine($"Session '{_sessionTableName}' registered/updated. Session ID: {retrievedSessionId}");

      Console.WriteLine("Reloading PostGIS types...");

 // PostGIS types (e.g., geometry) were just created; reload type info for this connection
       await _connection.ReloadTypesAsync(cancellationToken);

   Console.WriteLine("Preparing insert command...");
  
   // Prepare reusable insert command for rover_points table
 var insertSql = @"
INSERT INTO roverdata.rover_points
(rover_id, rover_name, session_id, recorded_at, latitude, longitude, wind_direction_deg, wind_speed_mps, geom, sequence)
VALUES (@rover_id, @rover_name, @session_id, @recorded_at, @latitude, @longitude, @wind_direction_deg, @wind_speed_mps, @geom, @sequence);";

 _insertCommand = new NpgsqlCommand(insertSql, _connection);
      _insertCommand.Parameters.Add("@rover_id", NpgsqlDbType.Uuid);
_insertCommand.Parameters.Add("@rover_name", NpgsqlDbType.Text);
          _insertCommand.Parameters.Add("@session_id", NpgsqlDbType.Uuid);
       _insertCommand.Parameters.Add("@recorded_at", NpgsqlDbType.TimestampTz);
 _insertCommand.Parameters.Add("@latitude", NpgsqlDbType.Double);
    _insertCommand.Parameters.Add("@longitude", NpgsqlDbType.Double);
     _insertCommand.Parameters.Add("@wind_direction_deg", NpgsqlDbType.Smallint);
 _insertCommand.Parameters.Add("@wind_speed_mps", NpgsqlDbType.Real);
      _insertCommand.Parameters.Add("@geom", NpgsqlDbType.Geometry);
 _insertCommand.Parameters.Add("@sequence", NpgsqlDbType.Bigint);

Console.WriteLine($"PostgreSQL repository initialization completed successfully for session '{_sessionTableName}'!");
  }
        catch (OperationCanceledException)
    {
 Console.WriteLine("PostgreSQL initialization cancelled.");
        throw;
      }
  catch (TimeoutException ex)
        {
    throw new TimeoutException($"PostgreSQL connection timed out during initialization: {ex.Message}", ex);
        }
   catch (NpgsqlException ex) when (ex.Message.Contains("timeout"))
 {
   throw new TimeoutException($"PostgreSQL connection timed out: {ex.Message}", ex);
        }
    catch (NpgsqlException ex) when (ex.Message.Contains("connection"))
   {
   throw new InvalidOperationException($"Failed to connect to PostgreSQL: {ex.Message}", ex);
      }
        catch (Exception ex)
    {
   Console.WriteLine($"Error during PostgreSQL initialization: {ex.Message}");
     throw;
    }
    }

    public override async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
       Console.WriteLine($"Resetting PostgreSQL data for session '{_sessionTableName}'...");
    
_dataSource = _dataSource ?? new NpgsqlDataSourceBuilder(_connectionString)
         .UseNetTopologySuite()
          .Build();
            
   await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        Console.WriteLine($"Deleting data for session '{_sessionTableName}' from rover_points table...");
       
  // Delete data for this session from the unified rover_points table
  var deleteSql = @"
DELETE FROM roverdata.rover_points 
WHERE session_id = (SELECT session_id FROM roverdata.rover_sessions WHERE session_name = @session_name);";
    
   await using var deleteCmd = new NpgsqlCommand(deleteSql, conn);
   deleteCmd.Parameters.AddWithValue("@session_name", _sessionTableName);
     var deletedCount = await deleteCmd.ExecuteNonQueryAsync(cancellationToken);

      Console.WriteLine($"Deleted {deletedCount} measurements for session '{_sessionTableName}'");
     Console.WriteLine($"Session reset completed successfully!");
   }
      catch (OperationCanceledException)
        {
    Console.WriteLine("Session reset cancelled.");
       throw;
        }
  catch (TimeoutException ex)
  {
   throw new TimeoutException($"Session reset timed out: {ex.Message}", ex);
        }
 catch (NpgsqlException ex) when (ex.Message.Contains("timeout"))
  {
 throw new TimeoutException($"Session reset timed out: {ex.Message}", ex);
        }
        catch (Exception ex)
     {
 Console.WriteLine($"Error during session reset: {ex.Message}");
      throw;
        }
    }

    public override async Task InsertMeasurementAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default)
    {
     if (_insertCommand == null)
  throw new InvalidOperationException("Repository not initialized. Call InitializeAsync first.");

        try
        {
         _insertCommand.Parameters["@rover_id"].Value = measurement.RoverId;
    _insertCommand.Parameters["@rover_name"].Value = measurement.RoverName;
   _insertCommand.Parameters["@session_id"].Value = measurement.SessionId;
  _insertCommand.Parameters["@recorded_at"].Value = measurement.RecordedAt;
   _insertCommand.Parameters["@latitude"].Value = measurement.Latitude;
_insertCommand.Parameters["@longitude"].Value = measurement.Longitude;
            _insertCommand.Parameters["@wind_direction_deg"].Value = measurement.WindDirectionDeg;
        _insertCommand.Parameters["@wind_speed_mps"].Value = measurement.WindSpeedMps;
       _insertCommand.Parameters["@geom"].Value = measurement.Geometry; // NTS -> PostGIS via Npgsql
            _insertCommand.Parameters["@sequence"].Value = measurement.Sequence;

 await _insertCommand.ExecuteNonQueryAsync(cancellationToken);
  }
  catch (OperationCanceledException)
        {
            throw;
 }
      catch (TimeoutException ex)
{
      throw new TimeoutException($"Insert operation timed out: {ex.Message}", ex);
   }
        catch (NpgsqlException ex) when (ex.Message.Contains("timeout"))
        {
    throw new TimeoutException($"Insert operation timed out: {ex.Message}", ex);
        }
   catch (Exception ex)
        {
throw new InvalidOperationException($"Failed to insert measurement: {ex.Message}", ex);
        }
    }

 protected override void Dispose(bool disposing)
    {
if (!_disposed)
      {
            if (disposing)
   {
    try
        {
           _insertCommand?.Dispose();
       _connection?.Dispose();
        _dataSource?.Dispose();
         
          Console.WriteLine("PostgreSQL connection resources disposed.");
        }
     catch (Exception ex)
     {
        Console.WriteLine($"Error disposing PostgreSQL resources: {ex.Message}");
         }
   }
       base.Dispose(disposing);
        }
    }
}