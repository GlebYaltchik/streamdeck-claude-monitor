using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// The pair of keys that answers a permission question: one Allow, one Deny.
///
/// Two of these on the deck, and no other number. Which key is which comes from where they
/// sit, read left to right and top to bottom, so a pair works with nothing configured — the
/// same rule that gives session slots their order. A checkbox in either key's settings swaps
/// the pair, and because that is one value rather than one per key, the two can never both
/// be Allow.
///
/// A key that is not part of a pair says so and says what to do about it. The alternative is
/// a key that looks ready and does nothing when pressed, which is the worst thing a key on
/// this deck could be.
///
/// Nothing is answered from here yet: a press does nothing until a session has been addressed.
/// The pair is built first so that it can be arranged, and can explain itself, before it can
/// act — the order the mode key was built in, and for the same reason.
/// </summary>
internal sealed class AnswerAction(IDeckConnection connection, DeckModes modes, AnswerRoles roles) : IDeckAction
{
    private readonly Dictionary<string, DeckKey> _keys = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public string Uuid => "com.gyaltchik.claudedeck.answer";

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

                // Every key, not just this one: the arrival of a second key is what turns the
                // first into half of a pair.
                Refresh();
                break;

            case "willDisappear":
                lock (_gate)
                {
                    _keys.Remove(deckEvent.Context);
                }

                connection.Forget(deckEvent.Context);
                Refresh();
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>Redraws the pair. Safe to call from the hub's threads.</summary>
    public void Refresh()
    {
        var keys = Ordered();
        var answering = modes.Current == DeckMode.Active;

        foreach (var (context, position) in keys)
        {
            connection.Update(
                context,
                new ImageUpdate(AnswerKeyFace.Render(roles.Of(position), keys.Count, answering)));
        }
    }

    /// <summary>
    /// The visible keys in reading order, each with its position in the pair. A key whose
    /// coordinates never arrived sorts last rather than being dropped: it still counts
    /// towards the pair, and it still deserves a face.
    /// </summary>
    private List<(string Context, int Position)> Ordered()
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
                    .Select((key, position) => (key.Key, position)),
            ];
        }
    }

    private sealed record DeckKey(string? Device, DeckCoordinates? Coordinates);
}
