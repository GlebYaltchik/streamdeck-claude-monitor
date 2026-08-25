using System.Text.Json;
using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// One key that says how far the deck is allowed into permission decisions, and the switch
/// design §6.4 requires. It exists before anything can decide, so the way to turn the feature
/// off is older than the feature.
///
/// Pressed rather than held: three modes need cycling, and the dangerous one is entered
/// deliberately by pressing twice past the harmless one rather than by holding.
///
/// The mode is the plugin's, not the key's. It is saved on whichever key changed it so that
/// it survives a restart, and any other mode key on the deck shows the same word.
/// </summary>
internal sealed class ModeAction(IDeckConnection connection, DeckModes modes) : IDeckAction
{
    private readonly HashSet<string> _contexts = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public string Uuid => "com.gyaltchik.claudedeck.mode";

    public async Task HandleAsync(DeckEvent deckEvent)
    {
        if (deckEvent.Context is null)
        {
            return;
        }

        switch (deckEvent.Name)
        {
            case "willAppear":
            case "didReceiveSettings":
                lock (_gate)
                {
                    _contexts.Add(deckEvent.Context);
                }

                // A saved mode is only read at the point a key appears. With no key on the
                // deck at all the plugin stays on its default, which is the harmless one.
                modes.Set(DeckModes.Parse(Saved(deckEvent.Payload)));
                Refresh();
                break;

            case "keyDown":
                modes.Cycle();
                await connection.SaveSettingsAsync(
                    deckEvent.Context,
                    new { mode = DeckModes.Name(modes.Current) });
                break;

            case "willDisappear":
                lock (_gate)
                {
                    _contexts.Remove(deckEvent.Context);
                }

                connection.Forget(deckEvent.Context);
                break;
        }
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

    private static string? Saved(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object &&
        payload.TryGetProperty("settings", out var settings) &&
        settings.ValueKind == JsonValueKind.Object &&
        settings.TryGetProperty("mode", out var mode) &&
        mode.ValueKind == JsonValueKind.String
            ? mode.GetString()
            : null;

    private List<string> Contexts()
    {
        lock (_gate)
        {
            return [.. _contexts];
        }
    }
}
