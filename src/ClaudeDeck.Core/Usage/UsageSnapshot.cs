namespace ClaudeDeck.Core.Usage;

public enum UsageStatus
{
    /// <summary>Live numbers were retrieved.</summary>
    Ok,

    /// <summary>Credentials are missing, malformed, or rejected. The user must log in again.</summary>
    AuthRequired,

    /// <summary>The endpoint asked us to back off.</summary>
    RateLimited,

    /// <summary>Anything else: network, timeout, an unreadable response.</summary>
    Unavailable,
}

/// <summary>
/// One usage window. `Group` is the coarse identity we key on — "session" for the five-hour
/// window and "weekly" for the seven-day one — because it is more stable than the finer
/// `kind`.
/// </summary>
public sealed record UsageWindow(
    string Group,
    string Kind,
    int Percent,
    string Severity,
    DateTimeOffset? ResetsAt,
    bool IsActive);

/// <summary>
/// The result of one usage lookup. Always returned, never thrown: a key must be able to draw
/// something whatever happened.
/// </summary>
public sealed record UsageSnapshot(
    UsageStatus Status,
    IReadOnlyList<UsageWindow> Windows,
    DateTimeOffset RetrievedAt,
    string? Message = null,
    bool Stale = false,
    /// <summary>How long the server asked us to wait, when it said so.</summary>
    TimeSpan? RetryAfter = null)
{
    public const string SessionGroup = "session";
    public const string WeeklyGroup = "weekly";

    public UsageWindow? Session => Find(SessionGroup);

    public UsageWindow? Weekly => Find(WeeklyGroup);

    public UsageWindow? Find(string group) =>
        Windows.FirstOrDefault(window => string.Equals(window.Group, group, StringComparison.OrdinalIgnoreCase));

    public static UsageSnapshot Failure(UsageStatus status, string message, DateTimeOffset retrievedAt) =>
        new(status, [], retrievedAt, message);
}
