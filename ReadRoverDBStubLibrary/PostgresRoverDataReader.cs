/*
 The functionallity in this file is:
 - Implement a PostgreSQL/PostGIS data reader using Npgsql and NetTopologySuite (geom -> NTS Point).
 - Provide async read operations (count, list, new since seq, latest) with minimal error translation.
 - Keep the sample simple and silent: no extra logging; dispose ADO.NET resources properly.
*/

using NetTopologySuite.Geometries; // NuGet: NetTopologySuite for GIS geometry types (maps PostGIS geometries)
using Npgsql; // NuGet: Npgsql is the ADO.NET provider for PostgreSQL (supports PostGIS via NTS)
using System.Data; // ADO.NET primitives (IDbConnection, ConnectionState)

namespace ReadRoverDBStubLibrary;

/// <summary>
/// PostgreSQL rover data reader (silent library version).
/// Notes:
/// - Uses Npgsql with NetTopologySuite to read PostGIS geometry (geom -> Point).
/// - Async/await with CancellationToken to avoid blocking and allow cooperative cancellation.
/// </summary>
public class PostgresRoverDataReader : RoverDataReaderBase
{
    private NpgsqlDataSource? _dataSource; // ADO.NET data source (manages connection pooling)
    private NpgsqlConnection? _connection; // ADO.NET connection instance (opened on demand)

    public PostgresRoverDataReader(string connectionString) : base(connectionString)
    {
    }

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Only initialize once - connection pooling will be handled by Npgsql
        if (_dataSource != null && _connection != null)
            return;

        try
        {
            // UseNetTopologySuite registers PostGIS type handlers so 'geom' maps to NTS Point (FOSS4G: PostGIS)
            _dataSource = new NpgsqlDataSourceBuilder(_connectionString)
                .UseNetTopologySuite()
                .Build();

            // Open physical connection (ADO.NET) - this will be reused
            _connection = await _dataSource.OpenConnectionAsync(cancellationToken);

            // Quick sanity check: verify table access (updated to use rover_points table)
            const string testSql = @"SELECT COUNT(*) FROM roverdata.rover_points LIMIT 1;";
            await using var testCmd = new NpgsqlCommand(testSql, _connection);
            await testCmd.ExecuteScalarAsync(cancellationToken);
        }
        // Keep exception translation minimal; just enough for clear troubleshooting in a workshop
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

    private async Task EnsureConnectionOpenAsync(CancellationToken cancellationToken = default)
    {
        // Check if connection is broken and needs to be reconnected
        if (_connection == null || _connection.State == ConnectionState.Broken || _connection.State == ConnectionState.Closed)
        {
            try
            {
                // Try to reconnect using the existing data source
                if (_dataSource != null)
                {
                    _connection?.Dispose();
                    _connection = await _dataSource.OpenConnectionAsync(cancellationToken);
                }
                else
                {
                    // Data source was disposed - reinitialize completely
                    await InitializeAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to reconnect to database: {ex.Message}", ex);
            }
        }
    }

    public override async Task<long> GetMeasurementCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectionOpenAsync(cancellationToken);
        try
        {
            const string countSql = "SELECT COUNT(*) FROM roverdata.rover_points;";
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
            throw new InvalidOperationException($"Failed to get measurement count: {ex.Message}", ex);
        }
    }

    public override async Task<List<RoverMeasurement>> GetAllMeasurementsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectionOpenAsync(cancellationToken);
        try
        {
            var sql = @"
                SELECT rover_id, rover_name, session_id, sequence, recorded_at, latitude, longitude, 
                       wind_direction_deg, wind_speed_mps, geom
                FROM roverdata.rover_points ORDER BY sequence ASC;";

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
            throw new InvalidOperationException($"Failed to get measurements: {ex.Message}", ex);
        }
    }

    public override async Task<List<RoverMeasurement>> GetNewMeasurementsAsync(int lastSequence, CancellationToken cancellationToken = default)
    {
        await EnsureConnectionOpenAsync(cancellationToken);
        try
        {
            const string sql = @"
                SELECT rover_id, rover_name, session_id, sequence, recorded_at, latitude, longitude, 
                       wind_direction_deg, wind_speed_mps, geom
                FROM roverdata.rover_points
                WHERE sequence > @lastSequence
                ORDER BY sequence ASC;";
            await using var cmd = new NpgsqlCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@lastSequence", lastSequence); // Parameterized query (ADO.NET)
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
            throw new InvalidOperationException($"Failed to get new measurements: {ex.Message}", ex);
        }
    }

    public override async Task<RoverMeasurement?> GetLatestMeasurementAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectionOpenAsync(cancellationToken);
        try
        {
            const string sql = @"
                SELECT rover_id, rover_name, session_id, sequence, recorded_at, latitude, longitude, 
                       wind_direction_deg, wind_speed_mps, geom
                FROM roverdata.rover_points
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
            throw new InvalidOperationException($"Failed to get latest measurement: {ex.Message}", ex);
        }
    }

    private static RoverMeasurement ConvertToRoverMeasurement(NpgsqlDataReader reader)
    {
        // Ordinal-based access is efficient; order matches SELECT list.
        // NetTopologySuite Point mapping requires Npgsql UseNetTopologySuite (PostGIS <-> NTS).
        var roverId = reader.GetFieldValue<Guid>(0);        // rover_id
        var roverName = reader.GetFieldValue<string>(1);    // rover_name
        var sessionId = reader.GetFieldValue<Guid>(2);      // session_id
        var sequence = reader.GetFieldValue<int>(3);        // sequence
        var recordedAt = reader.GetFieldValue<DateTimeOffset>(4);  // recorded_at
        var latitude = reader.GetFieldValue<double>(5);     // latitude
        var longitude = reader.GetFieldValue<double>(6);    // longitude
        var windDirection = reader.GetFieldValue<short>(7); // wind_direction_deg
        var windSpeed = reader.GetFieldValue<float>(8);     // wind_speed_mps
        var geometry = reader.GetFieldValue<Point>(9);      // geom (PostGIS geometry -> NTS Point)

        return new RoverMeasurement(
            roverId,
            roverName,
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

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources (ADO.NET). Keep it silent (no logging).
                try
                {
                    _connection?.Dispose();
                    _dataSource?.Dispose();
                }
                catch (Exception)
                {
                    // Intentionally silent per workshop guidelines
                }
            }
            base.Dispose(disposing);
        }
    }
}