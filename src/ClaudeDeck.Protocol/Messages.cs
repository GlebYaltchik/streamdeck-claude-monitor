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
/// Hub to agent: drop this session from the registry without waiting for it to go silent.
///
/// Harmless when it is wrong. A session that is in fact still running re-registers on its
/// next hook, so the worst outcome is a key that is briefly empty.
/// </summary>
public sealed record ForgetSession(string SessionId);

/// <summary>
/// Hub to agent: the deck's mode, by name — <c>off</c>, <c>observe</c> or <c>active</c>.
/// A name this build does not know reads as <c>observe</c>, which is what an agent that was
/// never told does too.
/// </summary>
public sealed record ModeUpdate(string Mode);

/// <summary>
/// A session as it crosses the wire. Deliberately not the agent's own record: the hub is
/// given what a key has to draw, and gains no opinion about the agent's internals.
///
/// The context fields default, so an agent built before they existed still speaks version 1.
/// Adding a field nobody has to send is not a protocol change; removing or repurposing one
/// would be.
/// </summary>
public sealed record AgentSession(
    string Id,
    string State,
    string? Project,
    string? Cwd,
    string? PermissionMode,
    string? CurrentTool,
    DateTimeOffset StartedAt,
    DateTimeOffset LastEventAt,
    string? Title = null,
    string? Model = null,
    string? Branch = null,
    int? ContextTokens = null,
    int? ContextPercent = null,
    bool ContextEstimated = false,
    bool AwaitingUser = false);
