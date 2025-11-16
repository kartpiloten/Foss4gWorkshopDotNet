using Npgsql;

namespace RoverData.Repository;

/// <summary>
/// Handles one-time database schema initialization for PostgreSQL.
/// Should be run once at application startup.
/// </summary>
public class PostgresDatabaseInitializer
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresDatabaseInitializer(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task InitializeSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var createSql = @"
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE SCHEMA IF NOT EXISTS roverdata;

CREATE TABLE IF NOT EXISTS roverdata.rover_sessions (
    id SERIAL PRIMARY KEY,
    session_name TEXT UNIQUE NOT NULL,
    session_id UUID NOT NULL DEFAULT gen_random_uuid(),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    last_updated TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS roverdata.rover_points (
    id BIGSERIAL PRIMARY KEY,
    geom geometry(Point,4326),
    rover_id UUID NOT NULL,
    rover_name TEXT NOT NULL,
    session_id UUID NOT NULL,
    sequence BIGINT NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL,
    wind_direction_deg SMALLINT NOT NULL,
    wind_speed_mps REAL NOT NULL,
    UNIQUE(session_id, rover_id, sequence)
);

CREATE INDEX IF NOT EXISTS ix_rover_points_session_time ON roverdata.rover_points (session_id, recorded_at);
CREATE INDEX IF NOT EXISTS ix_rover_points_geom ON roverdata.rover_points USING GIST (geom);
";

        await using var cmd = new NpgsqlCommand(createSql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
