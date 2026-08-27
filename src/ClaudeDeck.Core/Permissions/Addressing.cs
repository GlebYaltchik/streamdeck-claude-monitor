namespace ClaudeDeck.Core.Permissions;

/// <summary>
/// The session the deck is talking about, and the question it was addressed for.
///
/// The question is part of the address, not a detail of it. A session that is asked one thing,
/// has it answered in its own window, and is then asked another is the same session with a
/// different question — and an address that survived the change would answer the second one
/// with a press meant for the first.
/// </summary>
public sealed record Addressed(
    string SessionId,
    string Tool,
    string? Summary,
    bool Dangerous,
    DateTimeOffset Until);

/// <summary>
/// Which session a press on the answer pair means.
///
/// A standalone answer key cannot say which session it is for, and a session name rarely fits
/// on a key. Addressing turns that round: the session key says which session, the pair says
/// which answer, and neither has to carry the other's job.
///
/// The address is deliberately short-lived. It is a sentence half spoken, and a half-spoken
/// sentence left lying around is how the wrong session gets answered — so it lapses on its
/// own, and is dropped the moment the question it was for is gone.
/// </summary>
public sealed class Addressing
{
    /// <summary>
    /// How long an address lives. Long enough to move a hand from one key to another, short
    /// enough that a forgotten one cannot be inherited by whatever asks next.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    private readonly Lock _gate = new();

    private Addressed? _current;

    /// <summary>Raised when the address appeared, was used, lapsed or was dropped.</summary>
    public event Action? Changed;

    /// <summary>The live address, or null when there is none or it has run out.</summary>
    public Addressed? Current(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _current is { } address && now < address.Until ? address : null;
        }
    }

    /// <summary>
    /// What is left of the window, from 1 down to 0, and 0 when nothing is addressed. This is
    /// what the pair draws instead of an instruction: twenty seconds is short enough that keys
    /// going quiet with no warning would read as a fault.
    /// </summary>
    public double Remaining(DateTimeOffset now) =>
        Current(now) is { } address
            ? Math.Clamp((address.Until - now) / Window, 0, 1)
            : 0;

    /// <summary>
    /// Addresses this session, or drops the address when this session is the one already
    /// addressed — pressing the same key twice means never mind.
    /// </summary>
    public void Address(string sessionId, string tool, string? summary, bool dangerous, DateTimeOffset now)
    {
        lock (_gate)
        {
            var live = _current is { } address && now < address.Until ? address : null;

            _current = live?.SessionId == sessionId
                ? null
                : new Addressed(sessionId, tool, summary, dangerous, now + Window);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Takes the address and leaves none behind, in one step. Two presses arriving together
    /// would otherwise both find it live and both answer: the second would be answering a
    /// question nobody addressed, which is the whole thing the window exists to stop.
    /// </summary>
    public Addressed? Take(DateTimeOffset now)
    {
        Addressed? taken;
        bool cleared;

        lock (_gate)
        {
            taken = _current is { } address && now < address.Until ? address : null;
            cleared = _current is not null;
            _current = null;
        }

        if (cleared)
        {
            Changed?.Invoke();
        }

        return taken;
    }

    /// <summary>Drops an address that has run out. The clock is the only thing that notices.</summary>
    public void Expire(DateTimeOffset now) => Clear(address => now >= address.Until);

    /// <summary>
    /// Settles the address against the questions actually open now. A question answered in the
    /// session's own window, or replaced by another, takes its address with it.
    /// </summary>
    public void Settle(IEnumerable<(string SessionId, string? Tool, string? Summary)> waiting)
    {
        var open = waiting.ToList();

        Clear(address => !open.Any(session =>
            string.Equals(session.SessionId, address.SessionId, StringComparison.Ordinal) &&
            string.Equals(session.Tool, address.Tool, StringComparison.Ordinal) &&
            string.Equals(session.Summary, address.Summary, StringComparison.Ordinal)));
    }

    private void Clear(Func<Addressed, bool> when)
    {
        lock (_gate)
        {
            if (_current is not { } address || !when(address))
            {
                return;
            }

            _current = null;
        }

        Changed?.Invoke();
    }
}
