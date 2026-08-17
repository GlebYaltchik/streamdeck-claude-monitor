using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// Keeps the plugin loadable and exercises the whole path — input, state, coalesced output —
/// on both controller types while real actions are built.
/// </summary>
internal sealed class PlaceholderAction(IDeckConnection connection) : IDeckAction
{
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    public string Uuid => "com.gyaltchik.claudedeck.placeholder";

    public Task HandleAsync(DeckEvent deckEvent)
    {
        if (deckEvent.Context is null)
        {
            return Task.CompletedTask;
        }

        switch (deckEvent.Name)
        {
            case "willAppear":
                Render(deckEvent);
                break;
            case "dialRotate":
                Rotate(deckEvent);
                break;
            case "keyDown":
                Reset(deckEvent);
                break;
            case "willDisappear":
                _counters.Remove(deckEvent.Context);
                connection.Forget(deckEvent.Context);
                break;
        }

        return Task.CompletedTask;
    }

    private void Rotate(DeckEvent deckEvent)
    {
        var ticks = deckEvent.Payload.TryGetProperty("ticks", out var value) ? value.GetInt32() : 0;
        var counter = Math.Clamp(Counter(deckEvent.Context!) + ticks, 0, 100);
        _counters[deckEvent.Context!] = counter;
        Render(deckEvent);
    }

    private void Reset(DeckEvent deckEvent)
    {
        _counters[deckEvent.Context!] = 0;
        Render(deckEvent);
    }

    private void Render(DeckEvent deckEvent)
    {
        var context = deckEvent.Context!;
        var counter = Counter(context);

        if (deckEvent.IsEncoder)
        {
            connection.Update(context, new FeedbackUpdate("ClaudeDeck", $"{counter}%", counter));
            return;
        }

        var image = new KeyImage()
            .Background("#1b1f24")
            .Ring(counter / 100d, "#4f9cf9", "#2b313a")
            .Text($"{counter}%", 68, 26, "#ffffff", bold: true)
            .Text("ClaudeDeck", 118, 15, "#9aa4b2")
            .ToDataUrl();

        connection.Update(context, new ImageUpdate(image));
    }

    private int Counter(string context) => _counters.TryGetValue(context, out var value) ? value : 0;
}
