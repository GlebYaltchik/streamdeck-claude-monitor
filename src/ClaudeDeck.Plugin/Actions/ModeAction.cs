using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// One key that says how far the deck is allowed into permission decisions, and the switch
/// design §6.4 requires. It exists before anything can decide, so the way to turn the feature
/// off is older than the feature.
///
/// Pressed rather than held: it says which of two things a session key does, and neither is
/// reached by the press itself — the dangerous one still needs a hold on the session key.
///
/// The mode is the plugin's, not the key's: it is saved in the plugin's own settings, so it
/// survives a restart, reads the same on every mode key, and still has an answer on a deck
/// that carries no mode key at all. Such a deck sets it from the Property Inspector instead.
/// </summary>
internal sealed class ModeAction(IDeckConnection connection, DeckModes modes) : IDeckAction
{
    private readonly HashSet<string> _contexts = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public string Uuid => "com.gyaltchik.claudedeck.mode";

    public Task HandleAsync(DeckEvent deckEvent)
    {
        if (deckEvent.Context is null)
        {
            return Task.CompletedTask;
        }

        switch (deckEvent.Name)
        {
            case "willAppear":
            case "didReceiveSettings":
                lock (_gate)
                {
                    _contexts.Add(deckEvent.Context);
                }

                Refresh();
                break;

            case "keyDown":
                // Only the change is made here. Remembering it is the plugin's job, because
                // the mode also changes from the Property Inspector, and one writer is easier
                // to reason about than two agreeing ones.
                modes.Toggle();
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

    /// <summary>Redraws every visible mode key. Safe to call from the hub's threads.</summary>
    public void Refresh()
    {
        var face = ModeKeyFace.Render(modes.Current);

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
