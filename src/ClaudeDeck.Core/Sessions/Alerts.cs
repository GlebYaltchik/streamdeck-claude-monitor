namespace ClaudeDeck.Core.Sessions;

/// <summary>
/// Which sessions are asking to be looked at, and whether the deck is allowed to say so.
///
/// Shared by the slot keys that flash and the one key that silences them. Muting changes
/// nothing about a session: it stops the deck shouting, and the moment it is unmuted every
/// session still waiting is still waiting.
///
/// A slot stops flashing once it has been acknowledged with a tap. The acknowledgement is
/// forgotten as soon as the session stops waiting, so the end of its next turn flashes again
/// rather than being silently swallowed by a tap from an hour ago.
/// </summary>
public sealed class Alerts
{
    private readonly HashSet<string> _acknowledged = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Raised when the mute changed, so every key showing it can redraw.</summary>
    public event Action? Changed;

    public bool Muted { get; private set; }

    public void ToggleMute()
    {
        lock (_gate)
        {
            Muted = !Muted;
        }

        Changed?.Invoke();
    }

    /// <summary>Whether this session should be flashing right now.</summary>
    public bool Alerting(string sessionId, bool awaitingUser)
    {
        if (!awaitingUser || Muted)
        {
            return false;
        }

        lock (_gate)
        {
            return !_acknowledged.Contains(sessionId);
        }
    }

    /// <summary>Stops one slot flashing. The session itself is untouched.</summary>
    public void Acknowledge(string sessionId)
    {
        lock (_gate)
        {
            _acknowledged.Add(sessionId);
        }
    }

    /// <summary>
    /// Drops acknowledgements for sessions that are no longer waiting, and for sessions that
    /// have gone entirely. Called with whatever is currently waiting, on every refresh.
    /// </summary>
    public void Settle(IEnumerable<string> awaiting)
    {
        var current = awaiting.ToHashSet(StringComparer.Ordinal);

        lock (_gate)
        {
            _acknowledged.RemoveWhere(id => !current.Contains(id));
        }
    }
}
