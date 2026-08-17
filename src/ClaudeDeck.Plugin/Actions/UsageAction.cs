using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// Shows one usage window on a key. Which window it is comes from the key's settings, so the
/// same action serves the five-hour and the weekly views.
/// </summary>
internal sealed class UsageAction(IDeckConnection connection, UsageService usage) : IDeckAction
{
    private readonly Dictionary<string, UsageSettings> _keys = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public string Uuid => "com.gyaltchik.claudedeck.usage";

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
                Remember(deckEvent.Context, UsageSettings.From(deckEvent.Payload));
                await RenderAsync(deckEvent.Context);
                break;

            case "keyDown":
                usage.Invalidate(Settings(deckEvent.Context).CredentialsPath);
                await RenderAsync(deckEvent.Context);
                break;

            case "willDisappear":
                Forget(deckEvent.Context);
                connection.Forget(deckEvent.Context);
                break;
        }
    }

    /// <summary>Redraws every visible key. Cheap: the provider is cached and shared.</summary>
    public async Task RefreshAsync()
    {
        foreach (var context in Contexts())
        {
            await RenderAsync(context);
        }
    }

    private async Task RenderAsync(string context)
    {
        var settings = Settings(context);
        var snapshot = await usage.GetAsync(settings.CredentialsPath);
        var image = UsageKeyFace.Render(snapshot, settings.Window, settings.Label, DateTimeOffset.UtcNow);

        connection.Update(context, new ImageUpdate(image));
    }

    private void Remember(string context, UsageSettings settings)
    {
        lock (_gate)
        {
            _keys[context] = settings;
        }
    }

    private void Forget(string context)
    {
        lock (_gate)
        {
            _keys.Remove(context);
        }
    }

    private UsageSettings Settings(string context)
    {
        lock (_gate)
        {
            return _keys.TryGetValue(context, out var settings) ? settings : UsageSettings.Default;
        }
    }

    private List<string> Contexts()
    {
        lock (_gate)
        {
            return [.. _keys.Keys];
        }
    }
}
