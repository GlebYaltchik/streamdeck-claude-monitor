using System.Net.WebSockets;
using System.Text;
using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Sessions;
using ClaudeDeck.Protocol;

namespace ClaudeDeck.Agent;

/// <summary>
/// Holds a connection to the hub, or keeps trying to.
///
/// The agent works standalone: recording hooks must not depend on the plugin being up, so
/// every failure here is logged, backed off and retried, and never propagates.
/// </summary>
internal sealed class HubClient(
    SessionRegistry sessions,
    DeckModes modes,
    string? token,
    Action<string> log)
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumRetry = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sessions change on every tool call, so what is sent is the latest snapshot rather than
    /// one message per event.
    /// </summary>
    private static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(250);

    private int _pending;

    /// <summary>Marks the session state as worth sending. Cheap enough to call per event.</summary>
    public void Publish() => Interlocked.Exchange(ref _pending, 1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (token is null)
        {
            log($"no hub token, staying offline (set {HubToken.EnvironmentVariable})");
            return;
        }

        var secret = token;

        var retry = FirstRetry;

        while (!cancellationToken.IsCancellationRequested)
        {
            var connected = false;

            try
            {
                connected = await ConnectAndPumpAsync(secret, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log($"hub connection failed: {ex.Message}");
            }

            // A connection that got as far as the handshake earned a fresh budget; one that
            // never did keeps backing off, so a hub that is simply absent costs nothing.
            retry = connected ? FirstRetry : Slower(retry);

            try
            {
                await Task.Delay(retry, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> ConnectAndPumpAsync(string secret, CancellationToken cancellationToken)
    {
        var uri = new Uri($"ws://{HubHost.Resolve()}:{HubProtocol.Port()}/");

        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = Heartbeat;

        await socket.ConnectAsync(uri, cancellationToken);

        var hello = new Hello(secret, AgentIdentity.Id(), AgentIdentity.Machine(), AgentIdentity.Platform());
        await SendAsync(socket, Envelope.Write(HubProtocol.Hello, hello), cancellationToken);

        if (await ReceiveAsync(socket, cancellationToken) is not { Type: HubProtocol.Welcome })
        {
            log($"hub at {uri} refused the handshake: {socket.CloseStatusDescription ?? "no reason given"}");
            return false;
        }

        log($"connected to the hub at {uri}");

        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Sending happens on one task only: a websocket allows a single send at a time, and
        // the receive loop never has to answer anything.
        var sending = SendLoopAsync(socket, connection.Token);
        await ReceiveLoopAsync(socket, connection.Token);

        await connection.CancelAsync();
        await sending;

        log("hub connection closed");
        return true;
    }

    private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var lastHeartbeat = DateTimeOffset.UtcNow;
        Publish();

        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            try
            {
                if (Interlocked.Exchange(ref _pending, 0) == 1)
                {
                    await SendAsync(socket, Envelope.Write(HubProtocol.Sessions, Snapshot()), cancellationToken);
                }

                if (DateTimeOffset.UtcNow - lastHeartbeat >= Heartbeat)
                {
                    await SendAsync(socket, Envelope.Write(HubProtocol.Ping), cancellationToken);
                    lastHeartbeat = DateTimeOffset.UtcNow;
                }

                await Task.Delay(PublishInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException)
            {
                return;
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            if (await ReceiveAsync(socket, cancellationToken) is not { } envelope)
            {
                return;
            }

            Handle(envelope);
        }
    }

    /// <summary>
    /// Acts on what the hub sends. Anything unrecognised is ignored rather than complained
    /// about: a newer plugin talking about something this build has no idea of is not a fault.
    /// </summary>
    private void Handle(Envelope envelope)
    {
        switch (envelope.Type)
        {
            case HubProtocol.Forget when envelope.PayloadAs<ForgetSession>() is { SessionId.Length: > 0 } forget:
                sessions.Forget(forget.SessionId);
                log($"session {forget.SessionId} cleared from the deck");
                Publish();
                break;

            case HubProtocol.Mode when envelope.PayloadAs<ModeUpdate>() is { Mode.Length: > 0 } update:
                modes.Set(DeckModes.Parse(update.Mode));
                log($"deck is {DeckModes.Name(modes.Current)}");
                break;
        }
    }

    private SessionsUpdate Snapshot() =>
        new([.. sessions.Snapshot().Select(session => new AgentSession(
            session.Id,
            session.State.ToString(),
            session.Project,
            session.Cwd,
            session.PermissionMode,
            session.CurrentTool,
            session.StartedAt,
            session.LastEventAt,
            session.Title,
            session.Model,
            session.Branch,
            session.Context?.Tokens,
            session.Context?.Percent,
            session.Context?.Estimated ?? false,
            session.AwaitingUser,
            session.Pending?.Tool,
            session.Pending?.Summary))]);

    private static async Task<Envelope?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var message = new StringBuilder();

        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                return Envelope.Read(message.ToString());
            }
        }
    }

    private static Task SendAsync(WebSocket socket, string message, CancellationToken cancellationToken) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(message),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

    private static TimeSpan Slower(TimeSpan retry) =>
        TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, MaximumRetry.Ticks));
}
