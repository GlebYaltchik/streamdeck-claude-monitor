using System.Net.WebSockets;
using System.Text;
using ClaudeDeck.Protocol;

namespace ClaudeDeck.Hub.Tests;

/// <summary>
/// A hub on a free loopback port, plus the pieces of an agent a test needs. Nothing here
/// touches the network beyond loopback or the file system.
/// </summary>
internal sealed class HubUnderTest : IAsyncDisposable
{
    public const string Token = "a-token-only-this-test-knows";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _running;

    public HubUnderTest()
    {
        // Port 0 asks for a free one; RunAsync binds before its first await, so the port is
        // known as soon as it returns.
        Server = new HubServer(new HubOptions { Port = 0, Token = Token });
        _running = Server.RunAsync(_stopping.Token);
    }

    public HubServer Server { get; }

    public static CancellationToken Deadline => new CancellationTokenSource(Patience).Token;

    public async Task<ClientWebSocket> ConnectAsync()
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{Server.Port}/"), Deadline);
        return socket;
    }

    /// <summary>Connects and completes the handshake, which most tests want as a starting point.</summary>
    public async Task<ClientWebSocket> ConnectAgentAsync(string agentId = "agent-under-test")
    {
        var socket = await ConnectAsync();
        await SendAsync(socket, Envelope.Write(HubProtocol.Hello, new Hello(Token, agentId, "test-machine", "linux")));

        var welcome = await ReceiveAsync(socket);
        Assert.Equal(HubProtocol.Welcome, welcome?.Type);

        // The handshake is two messages: an agent has to know the deck's mode before it
        // starts holding anything open, so it is sent without being asked for.
        var mode = await ReceiveAsync(socket);
        Assert.Equal(HubProtocol.Mode, mode?.Type);
        return socket;
    }

    public static Task SendAsync(WebSocket socket, string message) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, Deadline);

    public static async Task<Envelope?> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[16 * 1024];
        var result = await socket.ReceiveAsync(buffer, Deadline);

        return result.MessageType == WebSocketMessageType.Close
            ? null
            : Envelope.Read(Encoding.UTF8.GetString(buffer, 0, result.Count));
    }

    /// <summary>Waits for something the hub does on another task, rather than sleeping blindly.</summary>
    public static async Task UntilAsync(Func<bool> settled)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (settled())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("the hub did not reach the expected state in time");
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        await Server.DisposeAsync();
        await _running;
        _stopping.Dispose();
    }
}
