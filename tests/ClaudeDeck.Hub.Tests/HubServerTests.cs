using System.Net.WebSockets;
using ClaudeDeck.Protocol;

namespace ClaudeDeck.Hub.Tests;

public class HubServerTests
{
    /// <summary>
    /// The switch has to reach the agents to be a switch at all: with the deck off they stop
    /// holding permission questions open.
    /// </summary>
    [Fact]
    public async Task A_change_of_mode_reaches_a_connected_agent()
    {
        await using var hub = new HubUnderTest();
        using var agent = await hub.ConnectAgentAsync();

        await hub.Server.SetModeAsync("off");

        var sent = await HubUnderTest.ReceiveAsync(agent);
        Assert.Equal(HubProtocol.Mode, sent?.Type);
        Assert.Equal("off", sent?.PayloadAs<ModeUpdate>()?.Mode);
    }

    [Fact]
    public async Task An_agent_with_the_right_token_is_accepted()
    {
        await using var hub = new HubUnderTest();

        using var agent = await hub.ConnectAgentAsync("windows-agent");

        await HubUnderTest.UntilAsync(() => hub.Server.Agents.Snapshot().Count == 1);
        var connected = hub.Server.Agents.Snapshot().Single();
        Assert.Equal("windows-agent", connected.Id);
        Assert.Equal("test-machine", connected.Machine);
    }

    [Fact]
    public async Task An_agent_with_a_bad_token_is_refused()
    {
        await using var hub = new HubUnderTest();

        using var agent = await hub.ConnectAsync();
        await HubUnderTest.SendAsync(
            agent,
            Envelope.Write(HubProtocol.Hello, new Hello("not-the-token", "intruder", "elsewhere", "linux")));

        Assert.Null(await HubUnderTest.ReceiveAsync(agent));
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, agent.CloseStatus);
        Assert.Empty(hub.Server.Agents.Snapshot());
    }

    [Fact]
    public async Task An_unsupported_protocol_version_is_refused()
    {
        await using var hub = new HubUnderTest();

        var fromTheFuture = $$$"""
            {"v":99,"type":"hello","payload":{"token":"{{{HubUnderTest.Token}}}","agentId":"future","machine":"m","platform":"linux"}}
            """;

        using var agent = await hub.ConnectAsync();
        await HubUnderTest.SendAsync(agent, fromTheFuture);

        Assert.Null(await HubUnderTest.ReceiveAsync(agent));
        Assert.Equal(WebSocketCloseStatus.ProtocolError, agent.CloseStatus);
        Assert.Empty(hub.Server.Agents.Snapshot());
    }

    [Fact]
    public async Task Anything_before_the_handshake_is_refused()
    {
        await using var hub = new HubUnderTest();

        using var agent = await hub.ConnectAsync();
        await HubUnderTest.SendAsync(agent, Envelope.Write(HubProtocol.Sessions, new SessionsUpdate([])));

        Assert.Null(await HubUnderTest.ReceiveAsync(agent));
        Assert.Equal(WebSocketCloseStatus.ProtocolError, agent.CloseStatus);
    }

    [Fact]
    public async Task Reported_sessions_are_kept_against_the_agent()
    {
        await using var hub = new HubUnderTest();
        using var agent = await hub.ConnectAgentAsync();

        var session = new AgentSession(
            "session-1",
            "Working",
            "streamdeck-claude-monitor",
            "/home/user/src/streamdeck-claude-monitor",
            "default",
            "Bash",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await HubUnderTest.SendAsync(agent, Envelope.Write(HubProtocol.Sessions, new SessionsUpdate([session])));

        await HubUnderTest.UntilAsync(() => hub.Server.Agents.Snapshot().SingleOrDefault()?.Sessions.Count == 1);
        Assert.Equal(session, hub.Server.Agents.Snapshot().Single().Sessions.Single());
    }

    /// <summary>
    /// The point of the message: the person at the deck knows the terminal is gone, and the
    /// agent can only find out by waiting out a timeout.
    /// </summary>
    [Fact]
    public async Task A_session_can_be_cleared_on_the_agent_that_reported_it()
    {
        await using var hub = new HubUnderTest();
        using var agent = await hub.ConnectAgentAsync();
        await Report(hub, agent, "session-1");

        Assert.True(await hub.Server.ForgetSessionAsync("session-1"));

        var sent = await HubUnderTest.ReceiveAsync(agent);
        Assert.Equal(HubProtocol.Forget, sent?.Type);
        Assert.Equal("session-1", sent?.PayloadAs<ForgetSession>()?.SessionId);
    }

    [Fact]
    public async Task Clearing_a_session_nobody_reported_asks_nobody()
    {
        await using var hub = new HubUnderTest();
        using var agent = await hub.ConnectAgentAsync();
        await Report(hub, agent, "session-1");

        Assert.False(await hub.Server.ForgetSessionAsync("a-session-from-somewhere-else"));
    }

    /// <summary>
    /// Two machines, and the request has to reach the one the session is actually on. The
    /// plugin holds session ids and knows nothing about which agent owns what.
    /// </summary>
    [Fact]
    public async Task A_session_is_cleared_on_its_own_agent_and_not_the_other_one()
    {
        await using var hub = new HubUnderTest();
        using var windows = await hub.ConnectAgentAsync("windows-agent");
        using var wsl = await hub.ConnectAgentAsync("wsl-agent");
        await Report(hub, windows, "on-windows");
        await Report(hub, wsl, "in-wsl");

        Assert.True(await hub.Server.ForgetSessionAsync("in-wsl"));

        var sent = await HubUnderTest.ReceiveAsync(wsl);
        Assert.Equal("in-wsl", sent?.PayloadAs<ForgetSession>()?.SessionId);

        // The other agent was told nothing at all, which its silence has to prove.
        await HubUnderTest.SendAsync(windows, Envelope.Write(HubProtocol.Ping));
        Assert.Equal(HubProtocol.Pong, (await HubUnderTest.ReceiveAsync(windows))?.Type);
    }

    [Fact]
    public async Task A_ping_is_answered_with_a_pong()
    {
        await using var hub = new HubUnderTest();
        using var agent = await hub.ConnectAgentAsync();

        await HubUnderTest.SendAsync(agent, Envelope.Write(HubProtocol.Ping));

        var answer = await HubUnderTest.ReceiveAsync(agent);
        Assert.Equal(HubProtocol.Pong, answer?.Type);
    }

    /// <summary>
    /// Reports one session and waits for the hub to have taken it in. Without the wait the
    /// forget that follows would race the report and fail for the wrong reason.
    /// </summary>
    private static async Task Report(HubUnderTest hub, WebSocket agent, string sessionId)
    {
        var session = new AgentSession(
            sessionId,
            "Idle",
            "streamdeck-claude-monitor",
            "/home/user/src/streamdeck-claude-monitor",
            "default",
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await HubUnderTest.SendAsync(agent, Envelope.Write(HubProtocol.Sessions, new SessionsUpdate([session])));

        await HubUnderTest.UntilAsync(() => hub.Server.Agents.ConnectionFor(sessionId) is not null);
    }

    [Fact]
    public async Task An_agent_that_comes_back_appears_once()
    {
        await using var hub = new HubUnderTest();

        using (var first = await hub.ConnectAgentAsync("the-same-agent"))
        {
            await HubUnderTest.UntilAsync(() => hub.Server.Agents.Snapshot().Count == 1);
            await first.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "restarting", HubUnderTest.Deadline);
        }

        await HubUnderTest.UntilAsync(() => hub.Server.Agents.Snapshot().Count == 0);

        using var second = await hub.ConnectAgentAsync("the-same-agent");

        await HubUnderTest.UntilAsync(() => hub.Server.Agents.Snapshot().Count == 1);
        Assert.Equal("the-same-agent", hub.Server.Agents.Snapshot().Single().Id);
    }
}
