using ClaudeDeck.Core.Permissions;
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
/// An addressed session is the one the deck is talking about, because somebody just said so.
/// With nothing addressed the oldest wait comes first — the session that has been stopped
/// longest is the one holding somebody up.
/// </summary>
internal sealed class PendingQueue(AgentRegistry agents, Addressing addressing)
{
    public IReadOnlyList<AgentSession> Waiting() =>
    [
        .. agents.Snapshot()
            .SelectMany(agent => agent.Sessions)
            .Where(session => session.PendingTool is { Length: > 0 })
            .OrderBy(session => session.LastEventAt),
    ];

    public AgentSession? Current()
    {
        var waiting = Waiting();

        if (addressing.Current(DateTimeOffset.UtcNow) is not { } addressed)
        {
            return waiting.FirstOrDefault();
        }

        return waiting.FirstOrDefault(session =>
            string.Equals(session.Id, addressed.SessionId, StringComparison.Ordinal))
            ?? waiting.FirstOrDefault();
    }
}
