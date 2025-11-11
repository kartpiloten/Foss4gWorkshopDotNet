namespace RoverData.Repository;

/// <summary>
/// Repository interface for managing session data in the database.
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// Registers a new session or retrieves an existing session by name.
    /// If a session with the given name already exists, returns its ID.
    /// Otherwise, creates a new session and returns the new ID.
    /// </summary>
    /// <param name="sessionName">The name of the session to register or retrieve.</param>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    /// <returns>The session ID (existing or newly created).</returns>
    Task<Guid> RegisterOrGetSessionAsync(string sessionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all sessions with their measurement counts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    /// <returns>List of session information including names and measurement counts.</returns>
    Task<List<SessionInfo>> GetSessionsWithCountsAsync(CancellationToken cancellationToken = default);
}
