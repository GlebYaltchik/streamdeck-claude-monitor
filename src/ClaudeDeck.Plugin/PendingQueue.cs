using ClaudeDeck.Hub;
using ClaudeDeck.Protocol;

namespace ClaudeDeck.Plugin;

/// <summary>
/// Which question the deck is currently talking about.
///
/// One place decides it, so the strip that shows a question and the key that answers one can
/// never mean different sessions. That is not tidiness: a key that answers something other
/// than what is on the strip beside it is the exact failure design §6.4 forbids.
///
/// The oldest wait comes first — the session that has been stopped longest is the one holding
/// somebody up. Choosing between several is Step 6; until then there is one and this says
/// which.
/// </summary>
internal sealed class PendingQueue(AgentRegistry agents)
{
    public IReadOnlyList<AgentSession> Waiting() =>
    [
        .. agents.Snapshot()
            .SelectMany(agent => agent.Sessions)
            .Where(session => session.PendingTool is { Length: > 0 })
            .OrderBy(session => session.LastEventAt),
    ];

    public AgentSession? Current() => Waiting().FirstOrDefault();
}
