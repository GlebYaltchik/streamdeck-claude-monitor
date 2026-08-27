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
///
/// Allowing something dangerous has to be held instead of pressed. The key says so before it
/// is touched - it is the one that has turned red - so the gesture that changes is the one on
/// the key that changed, in front of the person about to press it. Deny is never held: a
/// refusal costs a retry and runs nothing, and making it harder would only push people towards
/// the other key.
/// </summary>
internal sealed class AnswerAction(
    IDeckConnection connection,
    DeckModes modes,
    AnswerRoles roles,
    Addressing addressing,
    PendingQueue queue,
    Func<string, ApprovalDecision, Task<bool>> decide) : IDeckAction
{
    /// <summary>
    /// How long a dangerous allow is held for. Longer than any hold a slot key has ever asked
    /// for, because this is the press that runs the command Claude Code stopped to ask about.
    /// </summary>
    private static readonly TimeSpan DangerousPress = TimeSpan.FromMilliseconds(1500);

    private readonly Dictionary<string, DeckKey> _keys = new(StringComparer.Ordinal);

    /// <summary>Keys being held right now; cancelling one abandons its hold.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _holds = new(StringComparer.Ordinal);

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
                Pressed(deckEvent.Context);
                break;

            case "keyUp":
                // A hold still counting down was let go too early. For a dangerous allow that
                // is the whole point, and it does nothing.
                Abandon(deckEvent.Context);
                break;

            case "willDisappear":
                Abandon(deckEvent.Context);

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
    ///
    /// Allowing something dangerous starts a hold instead of answering. The address is only
    /// read here, not taken: a hold that is let go early must leave the question exactly as it
    /// found it.
    /// </summary>
    private void Pressed(string context)
    {
        if (modes.Current != DeckMode.Active || !Paired || Role(context) is not { } role)
        {
            return;
        }

        if (addressing.Current(DateTimeOffset.UtcNow) is not { } addressed)
        {
            return;
        }

        if (role == AnswerRole.Allow && addressed.Dangerous)
        {
            BeginHold(context, role);
            return;
        }

        Answer(role);
    }

    /// <summary>
    /// Takes the address and sends the answer. Taken rather than read, so two presses arriving
    /// together cannot both find it live: the second gets nothing and answers nothing.
    /// </summary>
    private void Answer(AnswerRole role)
    {
        if (addressing.Take(DateTimeOffset.UtcNow) is { } addressed)
        {
            _ = AnswerAsync(role, addressed);
        }
    }

    private void BeginHold(string context, AnswerRole role)
    {
        var hold = new CancellationTokenSource();

        lock (_gate)
        {
            AbandonLocked(context);
            _holds[context] = hold;
        }

        _ = HeldAsync(context, hold, role);
    }

    /// <summary>
    /// Waits out the hold and, if the key is still down, answers. Everything is asked again at
    /// the end rather than trusted from the beginning: the mode can change, the pair can lose
    /// a key, and the address can lapse or be answered elsewhere while a finger is down.
    /// </summary>
    private async Task HeldAsync(string context, CancellationTokenSource hold, AnswerRole role)
    {
        using (hold)
        {
            try
            {
                await Task.Delay(DangerousPress, hold.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_gate)
            {
                // The key can be released between the wait ending and this lock, so only the
                // hold still registered gets to act.
                if (!_holds.TryGetValue(context, out var current) || current != hold)
                {
                    return;
                }

                _holds.Remove(context);
            }

            if (modes.Current == DeckMode.Active && Paired)
            {
                Answer(role);
            }
        }
    }

    private void Abandon(string context)
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
            // It finished on its own between being taken from the dictionary and being
            // cancelled, which is the outcome that was wanted anyway.
        }
    }

    private async Task AnswerAsync(AnswerRole role, Addressed addressed)
    {
        var decision = role == AnswerRole.Allow
            ? new ApprovalDecision(ApprovalDecision.Allow, null)
            : ApprovalDecision.Denied();

        // Sent, not answered. All the hub reports is that an agent took the message; whether
        // it reached a question still being held is the agent's to log, and a plugin line
        // claiming the answer landed is one that will eventually be believed wrongly.
        var sent = await decide(addressed.SessionId, decision);

        PluginLog.Write(sent
            ? decision.Behaviour + " sent for " + addressed.Tool
            : "no agent to answer " + addressed.Tool);
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
                addressed is null ? null : addressing.Remaining(now),
                addressed?.Dangerous ?? false);

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
