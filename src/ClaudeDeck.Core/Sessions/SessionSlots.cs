namespace ClaudeDeck.Core.Sessions;

/// <summary>
/// Which slot each session occupies.
///
/// Dynamic but sticky, design §8. A session takes the lowest free slot the first time it is
/// seen and holds it until it ends; a freed slot goes only to a session that has not been
/// placed yet. Nothing reorders by activity: keys that move under your fingers are irritating
/// while they only report, and dangerous once the same key approves a command.
/// </summary>
public sealed class SessionSlots
{
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);

    /// <summary>
    /// Places sessions that have not been seen before and releases the slots of those that
    /// have gone. Several new sessions at once are placed in the order given, so the caller's
    /// ordering — oldest first — decides who gets the lower slot.
    /// </summary>
    public IReadOnlyDictionary<string, int> Assign(IEnumerable<string> live)
    {
        var present = live.ToList();
        var alive = present.ToHashSet(StringComparer.Ordinal);

        foreach (var gone in _slots.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _slots.Remove(gone);
        }

        foreach (var arrival in present.Where(id => !_slots.ContainsKey(id)))
        {
            _slots[arrival] = LowestFree();
        }

        return new Dictionary<string, int>(_slots, StringComparer.Ordinal);
    }

    private int LowestFree()
    {
        var taken = _slots.Values.ToHashSet();

        var slot = 0;
        while (taken.Contains(slot))
        {
            slot++;
        }

        return slot;
    }
}
