using ClaudeDeck.Protocol;

namespace ClaudeDeck.Hub.Tests;

public class AgentRegistryTests
{
    [Fact]
    public void An_agent_that_reconnects_replaces_its_earlier_entry()
    {
        var registry = new AgentRegistry();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        registry.Connected(first, Agent("wsl"));
        registry.Connected(second, Agent("wsl"));

        Assert.Single(registry.Snapshot());
    }

    [Fact]
    public void A_dead_socket_noticed_late_does_not_remove_the_live_agent()
    {
        // A machine that vanishes is only noticed when the read times out, which can be long
        // after the agent came back on a new connection.
        var registry = new AgentRegistry();
        var dead = Guid.NewGuid();
        var live = Guid.NewGuid();

        registry.Connected(dead, Agent("wsl"));
        registry.Connected(live, Agent("wsl"));
        registry.Disconnected(dead);

        Assert.Single(registry.Snapshot());
    }

    [Fact]
    public void Reports_from_a_connection_that_is_gone_are_ignored()
    {
        var registry = new AgentRegistry();
        var connection = Guid.NewGuid();

        registry.Connected(connection, Agent("wsl"));
        registry.Disconnected(connection);
        registry.Report(connection, [Session()], DateTimeOffset.UtcNow);

        Assert.Empty(registry.Snapshot());
    }

    private static ConnectedAgent Agent(string id) => new()
    {
        Id = id,
        Machine = "test-machine",
        Platform = "linux",
        ConnectedAt = DateTimeOffset.UtcNow,
        LastMessageAt = DateTimeOffset.UtcNow,
    };

    private static AgentSession Session() => new(
        "session-1",
        "Idle",
        "project",
        "/home/user/project",
        "default",
        null,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);
}
