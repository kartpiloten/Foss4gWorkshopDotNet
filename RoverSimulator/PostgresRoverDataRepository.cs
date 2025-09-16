using NetTopologySuite.Geometries;
using Npgsql;
using NpgsqlTypes;

namespace RoverSimulator;

public class PostgresRoverDataRepository : RoverDataRepositoryBase
{
    private NpgsqlDataSource? _dataSource;
    private NpgsqlConnection? _connection;
    private NpgsqlCommand? _insertCommand;

    public PostgresRoverDataRepository(string connectionString) : base(connectionString)
    {
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Build an Npgsql data source with NetTopologySuite enabled for PostGIS geometry mapping
        _dataSource = new NpgsqlDataSourceBuilder(_connectionString)
            .UseNetTopologySuite()
            .Build();
        
        _connection = await _dataSource.OpenConnectionAsync(cancellationToken);

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
        await using var createCmd = new NpgsqlCommand(createSql, _connection);
        await createCmd.ExecuteNonQueryAsync(cancellationToken);

        // PostGIS types (e.g., geometry) were just created; reload type info for this connection
        await _connection.ReloadTypesAsync(cancellationToken);

        // Prepare reusable insert command
        const string insertSql = @"
INSERT INTO roverdata.rover_measurements
(session_id, sequence, recorded_at, latitude, longitude, wind_direction_deg, wind_speed_mps, geom)
VALUES (@session_id, @sequence, @recorded_at, @latitude, @longitude, @wind_direction_deg, @wind_speed_mps, @geom);";

        _insertCommand = new NpgsqlCommand(insertSql, _connection);
        _insertCommand.Parameters.Add("@session_id", NpgsqlDbType.Uuid);
        _insertCommand.Parameters.Add("@sequence", NpgsqlDbType.Integer);
        _insertCommand.Parameters.Add("@recorded_at", NpgsqlDbType.TimestampTz);
        _insertCommand.Parameters.Add("@latitude", NpgsqlDbType.Double);
        _insertCommand.Parameters.Add("@longitude", NpgsqlDbType.Double);
        _insertCommand.Parameters.Add("@wind_direction_deg", NpgsqlDbType.Smallint);
        _insertCommand.Parameters.Add("@wind_speed_mps", NpgsqlDbType.Real);
        _insertCommand.Parameters.Add("@geom", NpgsqlDbType.Geometry);
    }

    public override async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var csb = new NpgsqlConnectionStringBuilder(_connectionString);
        var targetDb = csb.Database;
        var adminCsb = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Database = "postgres"
        };

        await using var adminConn = new NpgsqlConnection(adminCsb.ConnectionString);
        await adminConn.OpenAsync(cancellationToken);

        // Terminate existing connections to the target DB
        await using (var term = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid();",
            adminConn))
        {
            term.Parameters.AddWithValue("@db", targetDb);
            await term.ExecuteNonQueryAsync(cancellationToken);
        }

        // Drop the database if it exists
        await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{targetDb}\";", adminConn))
        {
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        // Recreate the database
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{targetDb}\" ENCODING 'UTF8' TEMPLATE template0;", adminConn))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public override async Task InsertMeasurementAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default)
    {
        if (_insertCommand == null)
            throw new InvalidOperationException("Repository not initialized. Call InitializeAsync first.");

        _insertCommand.Parameters["@session_id"].Value = measurement.SessionId;
        _insertCommand.Parameters["@sequence"].Value = measurement.Sequence;
        _insertCommand.Parameters["@recorded_at"].Value = measurement.RecordedAt;
        _insertCommand.Parameters["@latitude"].Value = measurement.Latitude;
        _insertCommand.Parameters["@longitude"].Value = measurement.Longitude;
        _insertCommand.Parameters["@wind_direction_deg"].Value = measurement.WindDirectionDeg;
        _insertCommand.Parameters["@wind_speed_mps"].Value = measurement.WindSpeedMps;
        _insertCommand.Parameters["@geom"].Value = measurement.Geometry;

        await _insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _insertCommand?.Dispose();
                _connection?.Dispose();
                _dataSource?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}