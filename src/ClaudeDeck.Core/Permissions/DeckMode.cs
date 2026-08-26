namespace ClaudeDeck.Core.Permissions;

/// <summary>
/// Whether a key may answer a permission question, or only show it.
///
/// There were three of these. The third, Off, also stopped the agent holding a question open
/// and stopped the deck flagging one at all — and no scenario was found that wanted it.
/// Holding costs a session nothing (the question is on screen throughout, and answering it
/// there closes ours), and the switch design §6.4 asks for is <see cref="Observe"/>, which
/// cannot act by construction. Two states also mean the key and the settings checkbox say
/// exactly the same thing, instead of the checkbox being able to express two of three.
/// </summary>
public enum DeckMode
{
    /// <summary>A waiting session is shown, and answered nowhere but in the session.</summary>
    Observe,

    /// <summary>A waiting session can also be answered from the deck.</summary>
    Active,
}

/// <summary>
/// The one switch design §6.4 asks for, shared by the key that shows it and the hub that
/// tells every agent about it.
///
/// It defaults to Observe: watching costs a session nothing, and answering from a key is the
/// thing that has to be chosen on purpose.
/// </summary>
public sealed class DeckModes
{
    private readonly Lock _gate = new();

    /// <summary>Raised when the mode changed, so keys redraw and agents are told.</summary>
    public event Action? Changed;

    public DeckMode Current { get; private set; } = DeckMode.Observe;

    /// <summary>Switches between watching and answering.</summary>
    public void Toggle() =>
        Set(Current == DeckMode.Active ? DeckMode.Observe : DeckMode.Active);

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
    /// reads as Observe, which is the same answer as never having been told — and that
    /// includes <c>off</c>, written by builds that had three modes.
    /// </summary>
    public static DeckMode Parse(string? mode) =>
        Enum.TryParse<DeckMode>(mode, ignoreCase: true, out var parsed) ? parsed : DeckMode.Observe;

    public static string Name(DeckMode mode) => mode.ToString().ToLowerInvariant();
}
