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
/// </summary>
internal sealed class SessionAction(IDeckConnection connection, AgentRegistry agents) : IDeckAction
{
    private readonly Dictionary<string, DeckKey> _keys = new(StringComparer.Ordinal);
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

            case "willDisappear":
                lock (_gate)
                {
                    _keys.Remove(deckEvent.Context);
                }

                connection.Forget(deckEvent.Context);
                break;
        }

        return Task.CompletedTask;
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
            var face = bySlot.TryGetValue(slot, out var session)
                ? SessionKeyFace.Render(Describe(session))
                : SessionKeyFace.Empty();

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
