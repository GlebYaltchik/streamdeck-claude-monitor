using System.Net.WebSockets;
using ClaudeDeck.Protocol;

namespace ClaudeDeck.Hub.Tests;

public class HubServerTests
{
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

    [Fact]
    public async Task A_ping_is_answered_with_a_pong()
    {
        await using var hub = new HubUnderTest();
        using var agent = await hub.ConnectAgentAsync();

        await HubUnderTest.SendAsync(agent, Envelope.Write(HubProtocol.Ping));

        var answer = await HubUnderTest.ReceiveAsync(agent);
        Assert.Equal(HubProtocol.Pong, answer?.Type);
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
