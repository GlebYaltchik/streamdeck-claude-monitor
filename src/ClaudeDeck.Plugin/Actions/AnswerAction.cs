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
/// A press answers the addressed session and nothing else. That is what a standalone answer
/// key could never do honestly: with two sessions waiting, "deny" on its own is a guess, and
/// the space on a key is not enough to say which one it meant. The session key names the
/// session, the pair names the answer.
/// </summary>
internal sealed class AnswerAction(
    IDeckConnection connection,
    DeckModes modes,
    AnswerRoles roles,
    Addressing addressing,
    PendingQueue queue,
    Func<string, ApprovalDecision, Task<bool>> decide) : IDeckAction
{
    private readonly Dictionary<string, DeckKey> _keys = new(StringComparer.Ordinal);

    /// <summary>
    /// The last face sent to each key, so a draining bar costs one message per step it
    /// actually moves rather than one per tick of the clock driving it.
    /// </summary>
    private readonly Dictionary<string, string> _drawn = new(StringComparer.Ordinal);

    private readonly Lock _gate = new();

    public string Uuid => "com.gyaltchik.claudedeck.answer";

    /// <summary>Whether the deck carries a pair, which is what makes addressing worth doing.</summary>
    public bool Paired
    {
        get
        {
            lock (_gate)
            {
                return _keys.Count == 2;
            }
        }
    }

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

            case "keyDown":
                Press(deckEvent.Context);
                break;

            case "willDisappear":
                lock (_gate)
                {
                    _keys.Remove(deckEvent.Context);
                    _drawn.Remove(deckEvent.Context);
                }

                connection.Forget(deckEvent.Context);
                Refresh();
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Answers the addressed session. A press with nothing addressed does nothing at all —
    /// deliberately silent, because the pair only ever speaks about a session somebody has
    /// just pointed it at.
    /// </summary>
    private void Press(string context)
    {
        if (modes.Current != DeckMode.Active || !Paired)
        {
            return;
        }

        if (Role(context) is not { } role || addressing.Current(DateTimeOffset.UtcNow) is not { } addressed)
        {
            return;
        }

        // Dropped before the answer is sent, not after. The question is about to be closed
        // either way, and an address outliving its own question is how the next one gets
        // answered by a press meant for this.
        addressing.Drop();

        _ = AnswerAsync(role, addressed);
    }

    private async Task AnswerAsync(AnswerRole role, Addressed addressed)
    {
        var decision = role == AnswerRole.Allow
            ? new ApprovalDecision(ApprovalDecision.Allow, null)
            : ApprovalDecision.Denied();

        var reached = await decide(addressed.SessionId, decision);

        PluginLog.Write(reached
            ? decision.Behaviour + " for " + addressed.Tool + " from the answer pair"
            : "nothing left to answer for " + addressed.Tool);
    }

    /// <summary>Redraws the pair. Safe to call from the hub's threads.</summary>
    public void Refresh()
    {
        var keys = Ordered();
        var answering = modes.Current == DeckMode.Active;
        var now = DateTimeOffset.UtcNow;
        var addressed = addressing.Current(now);
        var waiting = queue.Waiting().Count > 0;

        foreach (var (context, position) in keys)
        {
            var face = AnswerKeyFace.Render(
                roles.Of(position),
                keys.Count,
                answering,
                waiting,
                addressed is null ? null : addressing.Remaining(now));

            lock (_gate)
            {
                if (_drawn.TryGetValue(context, out var already) && already == face)
                {
                    continue;
                }

                _drawn[context] = face;
            }

            connection.Update(context, new ImageUpdate(face));
        }
    }

    /// <summary>
    /// Moves the draining bar on. Costs nothing while no session is addressed, which is almost
    /// always, and the redraw itself is dropped whenever the bar has not moved a whole step.
    /// </summary>
    public void Pulse()
    {
        if (addressing.Current(DateTimeOffset.UtcNow) is null)
        {
            return;
        }

        Refresh();
    }

    private AnswerRole? Role(string context)
    {
        var position = Ordered().FindIndex(key => key.Context == context);

        return position < 0 ? null : roles.Of(position);
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
