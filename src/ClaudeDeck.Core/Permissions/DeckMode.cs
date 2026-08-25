namespace ClaudeDeck.Core.Permissions;

/// <summary>How far the deck is allowed into a session's permission decisions.</summary>
public enum DeckMode
{
    /// <summary>The deck stays out of it entirely: nothing is flagged and nothing is held.</summary>
    Off,

    /// <summary>A waiting session is shown, and answered nowhere but in the session.</summary>
    Observe,

    /// <summary>A waiting session can also be answered from the deck.</summary>
    Active,
}

/// <summary>
/// The one switch design §6.4 asks for, shared by the key that shows it and the hub that
/// tells every agent about it.
///
/// It defaults to Observe rather than Active: watching costs a session nothing, and deciding
/// from a key is the thing that has to be chosen on purpose. It defaults to Observe rather
/// than Off too, because a switch nobody has touched should still do the harmless half of
/// the job.
/// </summary>
public sealed class DeckModes
{
    private readonly Lock _gate = new();

    /// <summary>Raised when the mode changed, so keys redraw and agents are told.</summary>
    public event Action? Changed;

    public DeckMode Current { get; private set; } = DeckMode.Observe;

    /// <summary>Steps to the next mode: off, observe, active, and round again.</summary>
    public void Cycle() => Set(Current switch
    {
        DeckMode.Off => DeckMode.Observe,
        DeckMode.Observe => DeckMode.Active,
        _ => DeckMode.Off,
    });

    public void Set(DeckMode mode)
    {
        lock (_gate)
        {
            if (Current == mode)
            {
                return;
            }

            Current = mode;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// What the mode is called on the wire and in a saved setting. Anything unrecognised
    /// reads as Observe, which is the same answer as never having been told.
    /// </summary>
    public static DeckMode Parse(string? mode) =>
        Enum.TryParse<DeckMode>(mode, ignoreCase: true, out var parsed) ? parsed : DeckMode.Observe;

    public static string Name(DeckMode mode) => mode.ToString().ToLowerInvariant();
}
