using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Npgsql;

namespace RoverData.Repository;

/// <summary>
/// PostgreSQL/PostGIS repository using injected NpgsqlDataSource (connection pool).
/// Stateless repository - session context is injected, not stored internally.
/// Safe to use from both scoped services (web requests) and singleton services (background workers).
/// All queries are automatically filtered by the current session from ISessionContext.
/// </summary>
public class PostgresRoverDataRepository : IRoverDataRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ISessionContext _sessionContext;

    public Guid SessionId => _sessionContext.SessionId;

    public PostgresRoverDataRepository(NpgsqlDataSource dataSource, ISessionContext sessionContext)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // No initialization needed - session context is provided via constructor
        // This method is kept for interface compatibility
        return Task.CompletedTask;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var deleteSql = "DELETE FROM roverdata.rover_points WHERE session_id = @session_id;";
        await using var cmd = new NpgsqlCommand(deleteSql, connection);
        cmd.Parameters.AddWithValue("@session_id", _sessionContext.SessionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertAsync(RoverMeasurement measurement, CancellationToken cancellationToken = default)
    {
        var sessionId = measurement.SessionId == Guid.Empty ? _sessionContext.SessionId : measurement.SessionId;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        
        var insertSql = @"
INSERT INTO roverdata.rover_points
(rover_id, rover_name, session_id, recorded_at, latitude, longitude, wind_direction_deg, wind_speed_mps, geom, sequence)
VALUES (@rover_id, @rover_name, @session_id, @recorded_at, @latitude, @longitude, @wind_direction_deg, @wind_speed_mps, @geom, @sequence);";

        await using var cmd = new NpgsqlCommand(insertSql, connection);
        cmd.Parameters.AddWithValue("@rover_id", measurement.RoverId);
        cmd.Parameters.AddWithValue("@rover_name", measurement.RoverName);
        cmd.Parameters.AddWithValue("@session_id", sessionId);
        cmd.Parameters.AddWithValue("@recorded_at", measurement.RecordedAt);
        cmd.Parameters.AddWithValue("@latitude", measurement.Latitude);
        cmd.Parameters.AddWithValue("@longitude", measurement.Longitude);
        cmd.Parameters.AddWithValue("@wind_direction_deg", measurement.WindDirectionDeg);
        cmd.Parameters.AddWithValue("@wind_speed_mps", measurement.WindSpeedMps);
        cmd.Parameters.AddWithValue("@geom", measurement.Geometry);
        cmd.Parameters.AddWithValue("@sequence", measurement.Sequence);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var sql = "SELECT COUNT(*) FROM roverdata.rover_points WHERE session_id = @session_id;";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@session_id", _sessionContext.SessionId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    public async Task<List<RoverMeasurement>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var sql = @"
SELECT rover_id, rover_name, session_id, sequence, recorded_at, latitude, longitude, 
       wind_direction_deg, wind_speed_mps, geom
FROM roverdata.rover_points 
WHERE session_id = @session_id
ORDER BY sequence ASC;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@session_id", _sessionContext.SessionId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var measurements = new List<RoverMeasurement>();
        while (await reader.ReadAsync(cancellationToken))
        {
            measurements.Add(new RoverMeasurement(
                reader.GetFieldValue<Guid>(0),
                reader.GetFieldValue<string>(1),
                reader.GetFieldValue<Guid>(2),
                reader.GetFieldValue<int>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<double>(5),
                reader.GetFieldValue<double>(6),
                reader.GetFieldValue<short>(7),
                reader.GetFieldValue<float>(8),
                reader.GetFieldValue<Point>(9)
            ));
        }
        return measurements;
    }

    public async Task<List<RoverMeasurement>> GetNewSinceSequenceAsync(int lastSequence, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var sql = @"
SELECT rover_id, rover_name, session_id, sequence, recorded_at, latitude, longitude, 
       wind_direction_deg, wind_speed_mps, geom
FROM roverdata.rover_points
WHERE session_id = @session_id AND sequence > @lastSequence
ORDER BY sequence ASC;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@session_id", _sessionContext.SessionId);
        cmd.Parameters.AddWithValue("@lastSequence", lastSequence);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var measurements = new List<RoverMeasurement>();
        while (await reader.ReadAsync(cancellationToken))
        {
            measurements.Add(new RoverMeasurement(
                reader.GetFieldValue<Guid>(0),
                reader.GetFieldValue<string>(1),
                reader.GetFieldValue<Guid>(2),
                reader.GetFieldValue<int>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<double>(5),
                reader.GetFieldValue<double>(6),
                reader.GetFieldValue<short>(7),
                reader.GetFieldValue<float>(8),
                reader.GetFieldValue<Point>(9)
            ));
        }
        return measurements;
    }

    public async Task<List<RoverMeasurement>> GetNewSinceTimestampAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var sql = @"
SELECT rover_id, rover_name, session_id, sequence, recorded_at, latitude, longitude, 
       wind_direction_deg, wind_speed_mps, geom
FROM roverdata.rover_points
WHERE session_id = @session_id AND recorded_at > @sinceUtc
ORDER BY recorded_at ASC;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@session_id", _sessionContext.SessionId);
        cmd.Parameters.AddWithValue("@sinceUtc", sinceUtc);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var measurements = new List<RoverMeasurement>();
        while (await reader.ReadAsync(cancellationToken))
        {
            measurements.Add(new RoverMeasurement(
                reader.GetFieldValue<Guid>(0),
                reader.GetFieldValue<string>(1),
                reader.GetFieldValue<Guid>(2),
                reader.GetFieldValue<int>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<double>(5),
                reader.GetFieldValue<double>(6),
                reader.GetFieldValue<short>(7),
                reader.GetFieldValue<float>(8),
                reader.GetFieldValue<Point>(9)
            ));
        }
        return measurements;
    }

    public async Task<RoverMeasurement?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var sql = @"
SELECT rover_id, rover_name, session_id, sequence, recorded_at, latitude, longitude, 
       wind_direction_deg, wind_speed_mps, geom
FROM roverdata.rover_points
WHERE session_id = @session_id
ORDER BY sequence DESC
LIMIT 1;";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@session_id", _sessionContext.SessionId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return new RoverMeasurement(
                reader.GetFieldValue<Guid>(0),
                reader.GetFieldValue<string>(1),
                reader.GetFieldValue<Guid>(2),
                reader.GetFieldValue<int>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<double>(5),
                reader.GetFieldValue<double>(6),
                reader.GetFieldValue<short>(7),
                reader.GetFieldValue<float>(8),
                reader.GetFieldValue<Point>(9)
            );
        }
        return null;
    }

    public void Dispose()
    {
        // NpgsqlDataSource is managed by DI container, so we don't dispose it here
    }
}
