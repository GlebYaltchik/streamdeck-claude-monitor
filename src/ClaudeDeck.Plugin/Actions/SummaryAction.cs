using ClaudeDeck.Core.Rendering;
using ClaudeDeck.Hub;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// Shows what the hub currently knows: how many agents are connected and how many sessions
/// they report. This is the whole path from a hook to the hardware on one key.
///
/// It has no settings and nothing to press. The face changes when the hub says something
/// changed, so <see cref="Refresh"/> is wired to the registry rather than polled.
/// </summary>
internal sealed class SummaryAction(IDeckConnection connection, AgentRegistry agents) : IDeckAction
{
    private readonly HashSet<string> _contexts = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public string Uuid => "com.gyaltchik.claudedeck.summary";

    public Task HandleAsync(DeckEvent deckEvent)
    {
        if (deckEvent.Context is null)
        {
            return Task.CompletedTask;
        }

        switch (deckEvent.Name)
        {
            case "willAppear":
                lock (_gate)
                {
                    _contexts.Add(deckEvent.Context);
                }

                Refresh();
                break;

            case "willDisappear":
                lock (_gate)
                {
                    _contexts.Remove(deckEvent.Context);
                }

                connection.Forget(deckEvent.Context);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Redraws every visible summary key. Safe to call from the hub's own threads: the
    /// update is queued and rate limited rather than sent here.
    /// </summary>
    public void Refresh()
    {
        var connected = agents.Snapshot();
        var face = SummaryKeyFace.Render(connected.Count, connected.Sum(agent => agent.Sessions.Count));

        foreach (var context in Contexts())
        {
            connection.Update(context, new ImageUpdate(face));
        }
    }

    private List<string> Contexts()
    {
        lock (_gate)
        {
            return [.. _contexts];
        }
    }
}
