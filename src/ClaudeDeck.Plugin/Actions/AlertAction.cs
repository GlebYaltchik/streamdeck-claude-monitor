using ClaudeDeck.Core.Rendering;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// One key that silences every flashing slot.
///
/// Pressed rather than held: unlike clearing a session, muting costs nothing to undo, and a
/// mute you have to hold for during a call is a mute that arrives too late.
///
/// It changes no session. The states on the deck stay exactly what they were, and unmuting
/// shows every slot still waiting rather than the ones that started waiting since.
/// </summary>
internal sealed class AlertAction(IDeckConnection connection, Alerts alerts, Func<int> waiting) : IDeckAction
{
    private readonly HashSet<string> _contexts = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public string Uuid => "com.gyaltchik.claudedeck.alerts";

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

            case "keyDown":
                alerts.ToggleMute();
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
    /// Redraws every visible mute key. Safe to call from the hub's own threads: the update is
    /// queued and rate limited rather than sent here.
    /// </summary>
    public void Refresh()
    {
        var face = AlertKeyFace.Render(alerts.Muted, waiting());

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
