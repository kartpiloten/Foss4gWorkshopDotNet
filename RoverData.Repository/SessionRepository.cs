using Npgsql;

namespace RoverData.Repository;

/// <summary>
/// Repository for session management operations in PostgreSQL.
/// Handles session registration and retrieval from the database.
/// </summary>
public class SessionRepository : ISessionRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public SessionRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    /// <summary>
    /// Registers or retrieves an existing session by name.
    /// Returns the session ID.
    /// </summary>
    public async Task<Guid> RegisterOrGetSessionAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        
        var registerSql = @"
INSERT INTO roverdata.rover_sessions (session_name)
VALUES (@session_name)
ON CONFLICT (session_name) DO UPDATE SET last_updated = NOW()
RETURNING session_id;";

        await using var cmd = new NpgsqlCommand(registerSql, connection);
        cmd.Parameters.AddWithValue("@session_name", sessionName);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return (Guid)result!;
    }

    /// <summary>
    /// Gets all available sessions with their measurement counts.
    /// </summary>
    public async Task<List<SessionInfo>> GetSessionsWithCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT rs.session_name, COUNT(rp.id) as measurement_count
            FROM roverdata.rover_sessions rs
            LEFT JOIN roverdata.rover_points rp ON rs.session_id = rp.session_id
            GROUP BY rs.session_name, rs.last_updated
            ORDER BY rs.last_updated DESC";

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var sessions = new List<SessionInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var sessionName = reader.GetString(0);
            var count = reader.GetInt64(1);
            sessions.Add(new SessionInfo(sessionName, count));
        }

        return sessions;
    }
}
