namespace ClaudeDeck.Protocol;

/// <summary>Agent to hub. Nothing else is accepted until the token checks out.</summary>
public sealed record Hello(string Token, string AgentId, string Machine, string Platform);

/// <summary>Hub to agent. Tells the agent how often the hub expects to hear from it.</summary>
public sealed record Welcome(int HeartbeatSeconds);

/// <summary>
/// Agent to hub: the complete set of sessions, not a delta. A reconnecting agent then needs
/// no replay, and a lost message costs nothing but freshness.
/// </summary>
public sealed record SessionsUpdate(IReadOnlyList<AgentSession> Sessions);

/// <summary>
/// A session as it crosses the wire. Deliberately not the agent's own record: the hub is
/// given what a key has to draw, and gains no opinion about the agent's internals.
/// </summary>
public sealed record AgentSession(
    string Id,
    string State,
    string? Project,
    string? Cwd,
    string? PermissionMode,
    string? CurrentTool,
    DateTimeOffset StartedAt,
    DateTimeOffset LastEventAt);
