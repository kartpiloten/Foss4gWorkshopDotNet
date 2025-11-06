namespace RoverData.Repository;

/// <summary>
/// Immutable session context for console applications.
/// Session is set once at application startup and never changes.
/// Thread-safe as singleton.
/// </summary>
public class ConsoleSessionContext : ISessionContext
{
    public Guid SessionId { get; }
    public string SessionName { get; }

    public ConsoleSessionContext(Guid sessionId, string sessionName)
    {
        SessionId = sessionId;
        SessionName = sessionName ?? throw new ArgumentNullException(nameof(sessionName));
    }
}

/// <summary>
/// Mutable session context for web applications.
/// Designed to be scoped per HTTP request.
/// Future: Can be updated via UI (dropdown) to switch sessions.
/// </summary>
public class WebSessionContext : ISessionContext
{
    public Guid SessionId { get; set; }
    public string SessionName { get; set; } = string.Empty;

    public WebSessionContext()
    {
    }

    public WebSessionContext(Guid sessionId, string sessionName)
    {
        SessionId = sessionId;
        SessionName = sessionName;
    }
}
