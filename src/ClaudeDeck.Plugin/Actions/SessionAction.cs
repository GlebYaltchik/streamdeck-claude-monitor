using ClaudeDeck.Core.Rendering;
using ClaudeDeck.Core.Sessions;
using ClaudeDeck.Hub;
using ClaudeDeck.Protocol;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// One key per session slot.
///
/// A key's slot is its position among the session keys, read left to right and top to bottom
/// from the coordinates Stream Deck sends. Nothing to configure: drop six of these on the
/// deck and they fill in reading order. Which session lands in which slot is decided by
/// <see cref="SessionSlots"/>, and stays decided.
///
/// Holding a key clears the session on it. The agent can only retire a session by waiting out
/// a long silence, because nothing tells it the terminal is gone; the person looking at the
/// deck usually knows already, and should not have to wait for a timeout that exists only to
/// cover that ignorance. Held rather than tapped because a key is easy to brush against, and
/// a tap will mean something else.
///
/// It fires the moment the hold is long enough, not when the key is let go. The key then
/// clears under the finger, which is the only thing that says the hold was long enough — wait
/// for the release and a hold that was already sufficient looks the same as one that was not.
/// </summary>
internal sealed class SessionAction(
    IDeckConnection connection,
    AgentRegistry agents,
    Func<string, Task<bool>> forgetSession) : IDeckAction
{
    /// <summary>
    /// How long is long. Short enough not to feel like a wait, long enough that a knock
    /// against the deck does not reach it.
    /// </summary>
    private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(800);

    private readonly Dictionary<string, DeckKey> _keys = new(StringComparer.Ordinal);

    /// <summary>Keys being held right now; cancelling one abandons its hold.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _holds = new(StringComparer.Ordinal);

    /// <summary>Which session each key is currently showing, so a press knows what it is on.</summary>
    private readonly Dictionary<string, string> _showing = new(StringComparer.Ordinal);

    private readonly SessionSlots _slots = new();
    private readonly Lock _gate = new();

    public string Uuid => "com.gyaltchik.claudedeck.session";

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
                    _keys[deckEvent.Context] = new DeckKey(deckEvent.Device, deckEvent.Coordinates);
                }

                Refresh();
                break;

            case "keyDown":
                BeginHold(deckEvent.Context);
                break;

            case "keyUp":
                AbandonHold(deckEvent.Context);
                break;

            case "willDisappear":
                AbandonHold(deckEvent.Context);

                lock (_gate)
                {
                    _keys.Remove(deckEvent.Context);
                }

                connection.Forget(deckEvent.Context);
                break;
        }

        return Task.CompletedTask;
    }

    private void BeginHold(string context)
    {
        var hold = new CancellationTokenSource();

        lock (_gate)
        {
            // A second keyDown without its keyUp would otherwise leave the first hold
            // counting down against a key nobody is pressing any more.
            AbandonLocked(context);
            _holds[context] = hold;
        }

        _ = HeldAsync(context, hold);
    }

    private void AbandonHold(string context)
    {
        lock (_gate)
        {
            AbandonLocked(context);
        }
    }

    private void AbandonLocked(string context)
    {
        if (!_holds.Remove(context, out var hold))
        {
            return;
        }

        try
        {
            hold.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The hold completed on its own between being taken from the dictionary and
            // being cancelled, which is the outcome that was wanted anyway.
        }
    }

    /// <summary>
    /// Waits out the hold and, if the key is still down, clears the session on it. Holding an
    /// empty slot is a press with nothing to mean and does nothing.
    /// </summary>
    private async Task HeldAsync(string context, CancellationTokenSource hold)
    {
        using (hold)
        {
            try
            {
                await Task.Delay(LongPress, hold.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            string? sessionId;

            lock (_gate)
            {
                // The key can be released in the moment between the wait ending and this
                // lock, so only the hold still registered gets to act.
                if (!_holds.TryGetValue(context, out var current) || current != hold)
                {
                    return;
                }

                _holds.Remove(context);
                sessionId = _showing.GetValueOrDefault(context);
            }

            if (sessionId is null)
            {
                return;
            }

            // Nothing is redrawn here. The agent drops the session and reports its new list,
            // and the key clears through the same path as every other change — so a request
            // that does not arrive leaves the key telling the truth rather than lying about
            // a session that is still there.
            await forgetSession(sessionId);
        }
    }

    /// <summary>
    /// Redraws every slot. Safe to call from the hub's threads: updates are queued and rate
    /// limited rather than sent from here.
    /// </summary>
    public void Refresh()
    {
        // Oldest first, so when several sessions arrive together the one that started first
        // takes the lower slot.
        var sessions = agents.Snapshot()
            .SelectMany(agent => agent.Sessions)
            .OrderBy(session => session.StartedAt)
            .ToList();

        var placed = _slots.Assign(sessions.Select(session => session.Id));

        var bySlot = new Dictionary<int, AgentSession>();
        foreach (var session in sessions.Where(session => placed.ContainsKey(session.Id)))
        {
            bySlot[placed[session.Id]] = session;
        }

        foreach (var (context, slot) in Slots())
        {
            var live = bySlot.TryGetValue(slot, out var session);

            lock (_gate)
            {
                if (live)
                {
                    _showing[context] = session!.Id;
                }
                else
                {
                    _showing.Remove(context);
                }
            }

            var face = live ? SessionKeyFace.Render(Describe(session!)) : SessionKeyFace.Empty();

            connection.Update(context, new ImageUpdate(face));
        }
    }

    /// <summary>
    /// Each visible key paired with the slot it shows. A key whose coordinates never arrived
    /// sorts last rather than being dropped: it still deserves a face.
    /// </summary>
    private List<(string Context, int Slot)> Slots()
    {
        lock (_gate)
        {
            return
            [
                .. _keys
                    .OrderBy(key => key.Value.Coordinates is null)
                    .ThenBy(key => key.Value.Device, StringComparer.Ordinal)
                    .ThenBy(key => key.Value.Coordinates?.Row ?? 0)
                    .ThenBy(key => key.Value.Coordinates?.Column ?? 0)
                    .Select((key, slot) => (key.Key, slot)),
            ];
        }
    }

    /// <summary>A visible key: which device it is on, and where. Both decide its slot.</summary>
    private sealed record DeckKey(string? Device, DeckCoordinates? Coordinates);

    private static SessionSlotFace Describe(AgentSession session) => new(
        Enum.TryParse<SessionState>(session.State, out var state) ? state : SessionState.Idle,
        session.Title,
        session.Project,
        session.ContextPercent,
        session.ContextEstimated);
}
