using Npgsql;

namespace ReadRoverDBStubLibrary;

public record SessionInfo(string Name, Guid? SessionId, long MeasurementCount, DateTime? LastMeasurement);

public static class SessionDiscovery
{
    public static async Task<List<SessionInfo>> ListSessionsAsync(DatabaseConfiguration config, CancellationToken cancellationToken = default)
    {
        if (config.DatabaseType.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return await ListPostgresSessionsAsync(config.PostgresConnectionString!, cancellationToken);
        }
        else if (config.DatabaseType.Equals("geopackage", StringComparison.OrdinalIgnoreCase))
        {
            return ListGeoPackageSessions(config.GeoPackageFolderPath ?? Directory.GetCurrentDirectory());
        }
        else
        {
            return new List<SessionInfo>();
        }
    }

    private static async Task<List<SessionInfo>> ListPostgresSessionsAsync(string connectionString, CancellationToken cancellationToken)
    {
        var result = new List<SessionInfo>();
        try
        {
            using var dataSource = new NpgsqlDataSourceBuilder(connectionString).UseNetTopologySuite().Build();
            await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
            const string sql = @"SELECT rs.session_name, rs.session_id, COUNT(rp.id) as measurement_count, MAX(rp.recorded_at) as last_measurement
 FROM roverdata.rover_sessions rs
 LEFT JOIN roverdata.rover_points rp ON rs.session_id = rp.session_id
 GROUP BY rs.session_name, rs.session_id
 ORDER BY MAX(rp.recorded_at) DESC NULLS LAST;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                var id = reader.GetGuid(1);
                var count = reader.GetInt64(2);
                var last = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);
                result.Add(new SessionInfo(name, id, count, last));
            }
        }
        catch
        {
            // Silent per library policy
        }
        return result;
    }

    private static List<SessionInfo> ListGeoPackageSessions(string folderPath)
    {
        var list = new List<SessionInfo>();
        try
        {
            if (!Directory.Exists(folderPath)) return list;
            var files = Directory.GetFiles(folderPath, "session_*.gpkg")
            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
            .ToList();
            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f).Replace("session_", "");
                var info = new FileInfo(f);
                list.Add(new SessionInfo(name, null, info.Length, info.LastWriteTime));
            }
        }
        catch { }
        return list;
    }
}
