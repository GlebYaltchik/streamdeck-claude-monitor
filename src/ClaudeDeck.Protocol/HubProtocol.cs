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

    public const string Ping = "ping";

    public const string Pong = "pong";

    public static int Port() =>
        Environment.GetEnvironmentVariable(PortEnvironmentVariable) is { } configured &&
        int.TryParse(configured, out var port)
            ? port
            : DefaultPort;
}
