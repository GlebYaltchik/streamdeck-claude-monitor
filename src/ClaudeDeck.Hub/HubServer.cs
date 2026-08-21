using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using ClaudeDeck.Protocol;

namespace ClaudeDeck.Hub;

public sealed record HubOptions
{
    /// <summary>Zero asks the operating system for a free port, which is what tests want.</summary>
    public int Port { get; init; } = HubProtocol.Port();

    public required string Token { get; init; }

    public Action<string>? Log { get; init; }
}

/// <summary>
/// Accepts agent connections and keeps track of what they report.
///
/// Agents connect to the hub rather than the other way round, which is what removes every
/// NAT and firewall question. Nothing here may take the plugin down: a port already in use,
/// an agent that dies mid-frame and a garbage handshake are all logged and survived.
/// </summary>
public sealed class HubServer : IAsyncDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Long enough for a few missed heartbeats. A machine that vanishes leaves a half-open
    /// socket whose read never completes, so silence is the only way to notice.
    /// </summary>
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan RebindInterval = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    private const int MaximumMessageBytes = 1024 * 1024;

    private readonly HubOptions _options;
    private readonly Dictionary<IPAddress, TcpListener> _listeners = [];
    private readonly ConcurrentDictionary<Guid, Link> _links = [];
    private readonly CancellationTokenSource _stopping = new();

    public HubServer(HubOptions options)
    {
        _options = options;
        Port = options.Port;
    }

    public AgentRegistry Agents { get; } = new();

    /// <summary>The port actually in use, which differs from the option when it asked for any.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// Asks whichever agent owns a session to drop it. Returns whether the request reached an
    /// agent at all — false when nobody claims the session, or its connection has just gone.
    /// </summary>
    public async Task<bool> ForgetSessionAsync(string sessionId)
    {
        if (Agents.ConnectionFor(sessionId) is not { } connection ||
            !_links.TryGetValue(connection, out var link))
        {
            return false;
        }

        return await link.SendAsync(Envelope.Write(HubProtocol.Forget, new ForgetSession(sessionId)));
    }

    /// <summary>Binds what is available now and keeps watching for what appears later.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);

        while (!stopping.IsCancellationRequested)
        {
            Rebind(stopping.Token);
            DropSilentAgents();

            try
            {
                await Task.Delay(RebindInterval, stopping.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        foreach (var listener in _listeners.Values)
        {
            listener.Stop();
        }

        _listeners.Clear();
    }

    /// <summary>
    /// Adds a listener for every address not already bound. The WSL adapter only exists while
    /// a distribution is running, so it is normal for one to appear long after startup.
    /// </summary>
    private void Rebind(CancellationToken cancellationToken)
    {
        foreach (var address in HostAddresses.Current().Where(address => !_listeners.ContainsKey(address)))
        {
            try
            {
                var listener = new TcpListener(address, Port);
                listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _listeners[address] = listener;
                _ = AcceptLoopAsync(listener, cancellationToken);
                Log($"hub listening on {address}:{Port}");
            }
            catch (Exception ex) when (ex is SocketException or ArgumentOutOfRangeException)
            {
                // A port in use, or a configured one outside the valid range. Neither may
                // stop the plugin; the next pass tries again.
                Log($"hub could not bind {address}:{Port}: {ex.Message}");
            }
        }
    }

    private void DropSilentAgents()
    {
        var deadline = DateTimeOffset.UtcNow - SilenceTimeout;

        foreach (var link in _links.Values.Where(link => link.LastMessageAt < deadline))
        {
            Log($"hub dropping silent agent {link.Machine}");
            link.Cancel();
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = ServeAsync(client, cancellationToken);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var connection = Guid.NewGuid();
        var link = new Link(cancellationToken);

        using (client)
        using (link)
        {
            try
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();

                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(link.Token);
                handshake.CancelAfter(HandshakeTimeout);

                if (!await WebSocketHandshake.TryAcceptAsync(stream, handshake.Token))
                {
                    return;
                }

                using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
                {
                    IsServer = true,
                    KeepAliveInterval = HeartbeatInterval,
                });

                if (await GreetAsync(socket, connection, link, handshake.Token))
                {
                    link.Socket = socket;
                    _links[connection] = link;
                    await PumpAsync(socket, connection, link);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log($"hub connection failed: {ex.Message}");
            }
            finally
            {
                // Only a connection that got past the handshake was ever registered, so this
                // does not log every port scan that reaches the socket.
                if (_links.TryRemove(connection, out var registered))
                {
                    Log($"hub lost agent {registered.Machine}");
                }

                Agents.Disconnected(connection);
            }
        }
    }

    /// <summary>
    /// Reads the first message, which must be a hello carrying a token we recognise. Anything
    /// else closes the connection with a reason the agent can log.
    /// </summary>
    private async Task<bool> GreetAsync(WebSocket socket, Guid connection, Link link, CancellationToken cancellationToken)
    {
        var envelope = await ReceiveAsync(socket, cancellationToken);

        if (envelope is null || envelope.Type != HubProtocol.Hello)
        {
            await CloseAsync(socket, WebSocketCloseStatus.ProtocolError, "expected hello");
            return false;
        }

        if (envelope.Version != HubProtocol.Version)
        {
            Log($"hub rejected protocol version {envelope.Version}");
            await CloseAsync(socket, WebSocketCloseStatus.ProtocolError, "unsupported protocol version");
            return false;
        }

        if (envelope.PayloadAs<Hello>() is not { } hello || !HubToken.Matches(_options.Token, hello.Token))
        {
            Log("hub rejected an agent with a bad token");
            await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "unauthorised");
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        link.Machine = hello.Machine;
        link.Touch(now);

        Agents.Connected(connection, new ConnectedAgent
        {
            Id = hello.AgentId,
            Machine = hello.Machine,
            Platform = hello.Platform,
            ConnectedAt = now,
            LastMessageAt = now,
        });

        var welcome = new Welcome((int)HeartbeatInterval.TotalSeconds);
        await SendAsync(socket, Envelope.Write(HubProtocol.Welcome, welcome), cancellationToken);
        Log($"hub accepted agent {hello.Machine} ({hello.Platform})");
        return true;
    }

    private async Task PumpAsync(WebSocket socket, Guid connection, Link link)
    {
        while (socket.State == WebSocketState.Open && !link.Token.IsCancellationRequested)
        {
            var envelope = await ReceiveAsync(socket, link.Token);
            if (envelope is null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            link.Touch(now);

            switch (envelope.Type)
            {
                case HubProtocol.Sessions:
                    Agents.Report(connection, envelope.PayloadAs<SessionsUpdate>()?.Sessions ?? [], now);
                    break;

                case HubProtocol.Ping:
                    await link.SendAsync(Envelope.Write(HubProtocol.Pong));
                    Agents.Touch(connection, now);
                    break;

                default:
                    // An unknown type is a newer agent talking about something this build does
                    // not have. Its heartbeat still counts.
                    Agents.Touch(connection, now);
                    break;
            }
        }
    }

    private static async Task<Envelope?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
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

            if (message.Length > MaximumMessageBytes)
            {
                return null;
            }

            if (result.EndOfMessage)
            {
                return Envelope.Read(message.ToString());
            }
        }
    }

    private static async Task SendAsync(WebSocket socket, string message, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        await socket.SendAsync(
            Encoding.UTF8.GetBytes(message),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    /// <summary>
    /// Sends the close frame without waiting for the agent to acknowledge it. The reason is
    /// what the agent logs, and the connection is being torn down regardless of the answer.
    /// </summary>
    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        try
        {
            await socket.CloseOutputAsync(status, reason, CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // The agent may already be gone.
        }
    }

    private void Log(string message) => _options.Log?.Invoke(message);

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _stopping.Dispose();
    }

    /// <summary>One live connection: how to reach it, how to stop it, and when it last spoke.</summary>
    private sealed class Link(CancellationToken cancellationToken) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        /// <summary>
        /// A websocket permits one send at a time, and this one now has two senders: the pump
        /// answering pings, and whatever key the user just pressed.
        /// </summary>
        private readonly SemaphoreSlim _sending = new(1, 1);

        private long _lastMessageTicks = DateTimeOffset.UtcNow.UtcTicks;

        public CancellationToken Token => _cancellation.Token;

        /// <summary>Set once the handshake succeeded; null before that.</summary>
        public WebSocket? Socket { get; set; }

        /// <summary>What the logs call this agent. Only set once the handshake succeeded.</summary>
        public string Machine { get; set; } = "unknown";

        public DateTimeOffset LastMessageAt => new(Interlocked.Read(ref _lastMessageTicks), TimeSpan.Zero);

        public void Touch(DateTimeOffset at) => Interlocked.Exchange(ref _lastMessageTicks, at.UtcTicks);

        public void Cancel()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The connection ended between being picked as silent and being cancelled,
                // which is the outcome that was wanted anyway.
            }
        }

        /// <summary>
        /// Returns whether the message actually went out. A closed or dying socket is not an
        /// error here: the caller is a key press, and the agent is about to be dropped anyway.
        /// </summary>
        public async Task<bool> SendAsync(string message)
        {
            if (Socket is not { State: WebSocketState.Open })
            {
                return false;
            }

            try
            {
                await _sending.WaitAsync(Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return false;
            }

            try
            {
                await Socket.SendAsync(
                    Encoding.UTF8.GetBytes(message),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    Token);
                return true;
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                return false;
            }
            finally
            {
                _sending.Release();
            }
        }

        public void Dispose()
        {
            _cancellation.Dispose();
            _sending.Dispose();
        }
    }
}
