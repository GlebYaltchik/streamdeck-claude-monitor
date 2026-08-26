namespace ClaudeDeck.Protocol;

/// <summary>
/// The contract between an agent and the hub.
///
/// The version travels in every envelope rather than only in the handshake, so a message
/// from a peer built against a different version is recognised as such instead of being
/// misread.
/// </summary>
public static class HubProtocol
{
    public const int Version = 1;

    public const int DefaultPort = 17801;

    public const string PortEnvironmentVariable = "CLAUDEDECK_HUB_PORT";

    /// <summary>Agent to hub, first message on a connection: identity and token.</summary>
    public const string Hello = "hello";

    /// <summary>Hub to agent: the handshake was accepted.</summary>
    public const string Welcome = "welcome";

    /// <summary>Agent to hub: every session it currently knows about.</summary>
    public const string Sessions = "sessions";

    /// <summary>
    /// Hub to agent: retire this session now. The user knows its terminal is gone and should
    /// not have to wait out a timeout that exists only because the agent cannot tell.
    /// </summary>
    public const string Forget = "forget";

    /// <summary>
    /// Hub to agent: answer the permission question this session is waiting on. Addressed by
    /// session rather than by a question id, because a session can only ever be asked one
    /// thing at a time — it is stopped until the answer comes.
    /// </summary>
    public const string Decide = "decide";

    public const string Ping = "ping";

    public const string Pong = "pong";

    public static int Port() =>
        Environment.GetEnvironmentVariable(PortEnvironmentVariable) is { } configured &&
        int.TryParse(configured, out var port)
            ? port
            : DefaultPort;
}
