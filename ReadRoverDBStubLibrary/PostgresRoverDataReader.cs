using NetTopologySuite.Geometries;
using Npgsql;
using System.Data;

namespace ReadRoverDBStubLibrary;

/// <summary>
/// PostgreSQL rover data reader (silent library version)
/// </summary>
public class PostgresRoverDataReader : RoverDataReaderBase
{
    private NpgsqlDataSource? _dataSource;
    private NpgsqlConnection? _connection;

    public PostgresRoverDataReader(string connectionString) : base(connectionString)
    {
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await CreateAndOpenConnectionAsync(cancellationToken);
    }

    private async Task EnsureConnectionOpenAsync(CancellationToken cancellationToken = default)
    {
        if (_connection == null || _connection.State != ConnectionState.Open)
        {
            await CreateAndOpenConnectionAsync(cancellationToken);
        }
    }

    private async Task CreateAndOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _dataSource?.Dispose();
            _connection?.Dispose();
            _dataSource = new NpgsqlDataSourceBuilder(_connectionString)
                .UseNetTopologySuite()
                .Build();
            _connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            // Test if the table exists and is accessible
            const string testSql = @"SELECT COUNT(*) FROM roverdata.rover_measurements LIMIT 1;";
            await using var testCmd = new NpgsqlCommand(testSql, _connection);
            await testCmd.ExecuteScalarAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("timeout"))
        {
            throw new TimeoutException($"PostgreSQL connection timed out: {ex.Message}", ex);
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("connection"))
        {
            throw new InvalidOperationException($"Failed to connect to PostgreSQL: {ex.Message}", ex);
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("relation") && ex.Message.Contains("does not exist"))
        {
            throw new InvalidOperationException($"Rover data table not found. Please run the RoverSimulator first to create the database schema. Error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error during PostgreSQL reader initialization: {ex.Message}", ex);
        }
    }

    public override async Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectionOpenAsync(cancellationToken);
        try
        {
            const string countSql = "SELECT COUNT(*) FROM roverdata.rover_measurements;";
            await using var cmd = new NpgsqlCommand(countSql, _connection);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CleanupConnection();
            throw new InvalidOperationException($"Failed to get measurement count: {ex.Message}", ex);
        }
    }

    public override async Task<List<RoverMeasurement>> GetAllMeasurementsAsync(string? whereClause = null, CancellationToken cancellationToken = default)
    {
        await EnsureConnectionOpenAsync(cancellationToken);
        try
        {
            var sql = @"
                SELECT session_id, sequence, recorded_at, latitude, longitude, 
                       wind_direction_deg, wind_speed_mps, geom
                FROM roverdata.rover_measurements";
            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                sql += $" WHERE {whereClause}";
            }
            sql += " ORDER BY sequence ASC;";
            await using var cmd = new NpgsqlCommand(sql, _connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var measurements = new List<RoverMeasurement>();
            while (await reader.ReadAsync(cancellationToken))
            {
                measurements.Add(ConvertToRoverMeasurement(reader));
            }
            return measurements;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CleanupConnection();
            throw new InvalidOperationException($"Failed to get measurements: {ex.Message}", ex);
        }
    }

    public override async Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default)
    {
        await EnsureConnectionOpenAsync(cancellationToken);
        try
        {
            const string sql = @"
                SELECT session_id, sequence, recorded_at, latitude, longitude, 
                       wind_direction_deg, wind_speed_mps, geom
                FROM roverdata.rover_measurements
                WHERE sequence > @lastSequence
                ORDER BY sequence ASC;";
            await using var cmd = new NpgsqlCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@lastSequence", lastSequence);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var measurements = new List<RoverMeasurement>();
            while (await reader.ReadAsync(cancellationToken))
            {
                measurements.Add(ConvertToRoverMeasurement(reader));
            }
            return measurements;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CleanupConnection();
            throw new InvalidOperationException($"Failed to get new measurements: {ex.Message}", ex);
        }
    }

    public override async Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectionOpenAsync(cancellationToken);
        try
        {
            const string sql = @"
                SELECT session_id, sequence, recorded_at, latitude, longitude, 
                       wind_direction_deg, wind_speed_mps, geom
                FROM roverdata.rover_measurements
                ORDER BY sequence DESC
                LIMIT 1;";
            await using var cmd = new NpgsqlCommand(sql, _connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return ConvertToRoverMeasurement(reader);
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CleanupConnection();
            throw new InvalidOperationException($"Failed to get latest measurement: {ex.Message}", ex);
        }
    }

    private static RoverMeasurement ConvertToRoverMeasurement(NpgsqlDataReader reader)
    {
        // Use ordinal-based column access - columns are in this order from our SELECT:
        // session_id, sequence, recorded_at, latitude, longitude, wind_direction_deg, wind_speed_mps, geom
        var sessionId = reader.GetFieldValue<Guid>(0);      // session_id
        var sequence = reader.GetFieldValue<int>(1);        // sequence  
        var recordedAt = reader.GetFieldValue<DateTimeOffset>(2);  // recorded_at
        var latitude = reader.GetFieldValue<double>(3);     // latitude
        var longitude = reader.GetFieldValue<double>(4);    // longitude
        var windDirection = reader.GetFieldValue<short>(5); // wind_direction_deg
        var windSpeed = reader.GetFieldValue<float>(6);     // wind_speed_mps
        var geometry = reader.GetFieldValue<Point>(7);      // geom

        return new RoverMeasurement(
            sessionId,
            sequence,
            recordedAt,
            latitude,
            longitude,
            windDirection,
            windSpeed,
            geometry
        );
    }

    private void CleanupConnection()
    {
        try
        {
            _connection?.Dispose();
        }
        catch { }
        _connection = null;
        try
        {
            _dataSource?.Dispose();
        }
        catch { }
        _dataSource = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                try
                {
                    _connection?.Dispose();
                    _dataSource?.Dispose();
                }
                catch (Exception)
                {
                    // Silent disposal - no console output
                }
            }
            base.Dispose(disposing);
        }
    }
}