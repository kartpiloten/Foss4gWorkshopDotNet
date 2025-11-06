namespace RoverData.Repository;

/// <summary>
/// Provides session context for repository operations.
/// Separates session selection from data access logic.
/// </summary>
public interface ISessionContext
{
    /// <summary>
    /// Gets the current session ID for filtering database queries.
    /// </summary>
    Guid SessionId { get; }

    /// <summary>
    /// Gets the current session name for display/logging purposes.
    /// </summary>
    string SessionName { get; }
}
