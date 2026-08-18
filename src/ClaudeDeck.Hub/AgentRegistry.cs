using ClaudeDeck.Protocol;

namespace ClaudeDeck.Hub;

/// <summary>An agent as the hub sees it, with whatever it last reported.</summary>
public sealed record ConnectedAgent
{
    public required string Id { get; init; }

    public required string Machine { get; init; }

    public required string Platform { get; init; }

    public required DateTimeOffset ConnectedAt { get; init; }

    public required DateTimeOffset LastMessageAt { get; init; }

    public IReadOnlyList<AgentSession> Sessions { get; init; } = [];
}

/// <summary>
/// Who is connected and what they reported.
///
/// Entries are keyed by agent id, so an agent that reconnects replaces itself rather than
/// appearing twice. Every call also names the connection it came from: when a dead socket is
/// noticed only after the agent has already reconnected, its disconnect must not take the
/// live entry with it.
/// </summary>
public sealed class AgentRegistry
{
    private readonly Dictionary<string, Entry> _agents = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Raised after anything visible on a key has changed.</summary>
    public event Action? Changed;

    public IReadOnlyList<ConnectedAgent> Snapshot()
    {
        lock (_gate)
        {
            return [.. _agents.Values.Select(entry => entry.Agent).OrderBy(agent => agent.ConnectedAt)];
        }
    }

    public void Connected(Guid connection, ConnectedAgent agent)
    {
        lock (_gate)
        {
            _agents[agent.Id] = new Entry(connection, agent);
        }

        Changed?.Invoke();
    }

    public void Report(Guid connection, IReadOnlyList<AgentSession> sessions, DateTimeOffset at)
    {
        if (!Update(connection, agent => agent with { Sessions = sessions, LastMessageAt = at }))
        {
            return;
        }

        Changed?.Invoke();
    }

    public void Touch(Guid connection, DateTimeOffset at) =>
        Update(connection, agent => agent with { LastMessageAt = at });

    public void Disconnected(Guid connection)
    {
        lock (_gate)
        {
            if (Find(connection) is not { } entry)
            {
                return;
            }

            _agents.Remove(entry.Agent.Id);
        }

        Changed?.Invoke();
    }

    private bool Update(Guid connection, Func<ConnectedAgent, ConnectedAgent> change)
    {
        lock (_gate)
        {
            if (Find(connection) is not { } entry)
            {
                return false;
            }

            _agents[entry.Agent.Id] = entry with { Agent = change(entry.Agent) };
            return true;
        }
    }

    private Entry? Find(Guid connection) =>
        _agents.Values.FirstOrDefault(entry => entry.Connection == connection);

    private sealed record Entry(Guid Connection, ConnectedAgent Agent);
}
