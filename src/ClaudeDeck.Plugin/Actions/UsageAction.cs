using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// Shows one usage window. Which window comes from the control's settings, so the same
/// action serves the five-hour and weekly views on both keys and encoders.
/// </summary>
internal sealed class UsageAction(IDeckConnection connection, UsageService usage) : IDeckAction
{
    private readonly Dictionary<string, ControlState> _controls = new(StringComparer.Ordinal);
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
                Remember(deckEvent);
                await RenderAsync(deckEvent.Context);
                break;

            // Pressing switches the window. There is deliberately no manual refresh: the
            // endpoint rate limits hard enough that a refresh button does more harm than
            // good, and the periodic poll is what actually keeps the number current.
            case "keyDown":
            case "dialDown":
            case "touchTap":
                await SwitchAsync(deckEvent.Context);
                break;

            case "dialRotate":
                await RotateAsync(deckEvent);
                break;

            case "willDisappear":
                Forget(deckEvent.Context);
                connection.Forget(deckEvent.Context);
                break;
        }
    }

    /// <summary>
    /// Rotation switches which window the encoder shows.
    ///
    /// One gesture means one switch, however many detents it spanned. The switch happens on
    /// the first tick, so a single careful click responds immediately, and further ticks are
    /// swallowed until the ticks stop arriving — a fast flick across several detents is one
    /// gesture, not several.
    /// </summary>
    private async Task RotateAsync(DeckEvent deckEvent)
    {
        // Longer than the gap between ticks within a spin, shorter than the pause between
        // two deliberate clicks. Settled at 250 ms by trying it on the hardware: 350 held on
        // long enough to swallow clicks that were meant separately.
        var gestureGap = TimeSpan.FromMilliseconds(250);

        var context = deckEvent.Context!;
        var now = DateTimeOffset.UtcNow;
        var ticks = deckEvent.Payload.TryGetProperty("ticks", out var value) ? value.GetInt32() : 0;
        var direction = Math.Sign(ticks);

        UsageSettings switched;
        lock (_gate)
        {
            if (!_controls.TryGetValue(context, out var state))
            {
                return;
            }

            // Turning back the other way is always a new intention, never the tail of the
            // gesture that came before it.
            var reversed = direction != 0 && state.LastDirection != 0 && direction != state.LastDirection;
            var continuing = !reversed && now - state.LastRotate < gestureGap;

            // The clock advances on every tick, so a spin keeps extending its own gesture.
            _controls[context] = state with { LastRotate = now, LastDirection = direction };

            if (continuing)
            {
                return;
            }

            switched = state.Settings.Switched();
            _controls[context] = state with { Settings = switched, LastRotate = now, LastDirection = direction };
        }

        await ApplyAsync(context, switched);
    }

    private async Task SwitchAsync(string context)
    {
        UsageSettings switched;
        lock (_gate)
        {
            if (!_controls.TryGetValue(context, out var state))
            {
                return;
            }

            switched = state.Settings.Switched();
            _controls[context] = state with { Settings = switched };
        }

        await ApplyAsync(context, switched);
    }

    private async Task ApplyAsync(string context, UsageSettings settings)
    {
        await connection.SaveSettingsAsync(context, settings.ToPayload());
        await RenderAsync(context);
    }

    /// <summary>Redraws every visible control. Cheap: the provider is cached and shared.</summary>
    public async Task RefreshAsync()
    {
        foreach (var context in Contexts())
        {
            await RenderAsync(context);
        }
    }

    private async Task RenderAsync(string context)
    {
        var state = State(context);
        var snapshot = await usage.GetAsync(state.Settings.CredentialsPath);
        var now = DateTimeOffset.UtcNow;
        var window = state.Settings.Window;
        var label = state.Settings.Label;

        if (state.IsEncoder)
        {
            var strip = UsageKeyFace.RenderStrip(snapshot, window, label, now);
            connection.Update(context, new FeedbackUpdate(strip.Title, strip.Value, strip.Indicator, strip.Colour));
            return;
        }

        connection.Update(context, new ImageUpdate(UsageKeyFace.Render(snapshot, window, label, now)));
    }

    private void Remember(DeckEvent deckEvent)
    {
        var settings = UsageSettings.From(deckEvent.Payload);

        lock (_gate)
        {
            // didReceiveSettings does not always carry the controller, so an already known
            // control keeps the type it appeared as.
            var isEncoder = deckEvent.Controller is not null
                ? deckEvent.IsEncoder
                : _controls.TryGetValue(deckEvent.Context!, out var known) && known.IsEncoder;

            _controls[deckEvent.Context!] = new ControlState(settings, isEncoder);
        }
    }

    private void Forget(string context)
    {
        lock (_gate)
        {
            _controls.Remove(context);
        }
    }

    private ControlState State(string context)
    {
        lock (_gate)
        {
            return _controls.TryGetValue(context, out var state)
                ? state
                : new ControlState(UsageSettings.Default, IsEncoder: false);
        }
    }

    private List<string> Contexts()
    {
        lock (_gate)
        {
            return [.. _controls.Keys];
        }
    }

    private sealed record ControlState(UsageSettings Settings, bool IsEncoder)
    {
        /// <summary>When the last rotation tick arrived, for grouping ticks into one gesture.</summary>
        public DateTimeOffset LastRotate { get; init; } = DateTimeOffset.MinValue;

        /// <summary>Which way the last tick went, so a reversal can break the gesture.</summary>
        public int LastDirection { get; init; }
    }
}
