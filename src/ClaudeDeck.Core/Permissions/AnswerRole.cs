namespace ClaudeDeck.Core.Permissions;

/// <summary>Which half of the answering pair a key is.</summary>
public enum AnswerRole
{
    Allow,

    Deny,
}

/// <summary>
/// Which way round the pair of answer keys is.
///
/// The roles come from where the keys sit, the same way a session slot's index does: drop two
/// on the deck and the first one is Allow, with nothing to configure. Position rather than a
/// per-key setting is also what makes the pair impossible to break — two keys both claiming to
/// be Allow cannot be expressed.
///
/// Swapping is therefore one value for the pair rather than one per key. A checkbox in either
/// key's settings flips it, and both keys change because there was only ever one thing to
/// change.
/// </summary>
public sealed class AnswerRoles
{
    private readonly Lock _gate = new();

    /// <summary>Raised when the pair changed sides, so both keys redraw.</summary>
    public event Action? Changed;

    public bool Swapped { get; private set; }

    /// <summary>The role of the key at this position among the answer keys, in reading order.</summary>
    public AnswerRole Of(int position) =>
        (position == 0) != Swapped ? AnswerRole.Allow : AnswerRole.Deny;

    public void Set(bool swapped)
    {
        lock (_gate)
        {
            if (Swapped == swapped)
            {
                return;
            }

            Swapped = swapped;
        }

        Changed?.Invoke();
    }

    public static string Name(AnswerRole role) => role.ToString().ToLowerInvariant();
}
