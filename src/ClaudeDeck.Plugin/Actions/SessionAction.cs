using System.Diagnostics;
using ClaudeDeck.Core.Permissions;
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
///
/// Holding a key clears the session on it. The agent can only retire a session by waiting out
/// a long silence, because nothing tells it the terminal is gone; the person looking at the
/// deck usually knows already, and should not have to wait for a timeout that exists only to
/// cover that ignorance. Held rather than tapped because a key is easy to brush against, and
/// a tap will mean something else.
///
/// It fires the moment the hold is long enough, not when the key is let go. The key then
/// clears under the finger, which is the only thing that says the hold was long enough — wait
/// for the release and a hold that was already sufficient looks the same as one that was not.
///
/// A session waiting for permission takes both gestures over: a tap denies and a hold allows.
/// Answering belongs on the key that shows the question, because a separate answer key has to
/// name which session it means, and a session name rarely fits on a key. While a question is
/// open the slot cannot be cleared: the session is stopped anyway, and clearing it mid
/// question would lose the one thing the key is there to say.
///
/// Allowing is held for half again as long as clearing. The two are otherwise the same
/// movement, and muscle memory from one must not run a command through the other.
///
/// A pair of answer keys on the deck changes both gestures. A tap then addresses the session
/// rather than denying it, and the hold does nothing at all: with the pair, answering is
/// always two presses, and keeping the hold would leave a command reachable in one gesture
/// while giving two ways to do the same thing.
/// </summary>
internal sealed class SessionAction(
    IDeckConnection connection,
    AgentRegistry agents,
    Alerts alerts,
    DeckModes modes,
    Addressing addressing,
    Func<bool> paired,
    Func<string, Task<bool>> forgetSession,
    Func<string, ApprovalDecision, Task<bool>> decide) : IDeckAction
{
    /// <summary>What a hold on a key is for, decided when the key goes down.</summary>
    private enum Held
    {
        Clear,

        Allow,

        /// <summary>Nothing: the pair answers, and a hold must not be a second way to allow.</summary>
        Nothing,
    }

    /// <summary>
    /// How long is long. Short enough not to feel like a wait, long enough that a knock
    /// against the deck does not reach it.
    /// </summary>
    private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// Half again as long as clearing a slot. Allowing runs a command that Claude Code
    /// stopped to ask about, and it must not be reachable by the hold somebody already has
    /// in their fingers.
    /// </summary>
    private static readonly TimeSpan AllowPress = TimeSpan.FromMilliseconds(1200);

    private readonly Dictionary<string, DeckKey> _keys = new(StringComparer.Ordinal);

    /// <summary>Keys being held right now; cancelling one abandons its hold.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _holds = new(StringComparer.Ordinal);

    /// <summary>Which session each key is currently showing, so a press knows what it is on.</summary>
    private readonly Dictionary<string, string> _showing = new(StringComparer.Ordinal);

    /// <summary>
    /// The last face sent to each key. The hub reports on every tool call, and sending a
    /// picture identical to the one already there is traffic for nothing — and, now that a
    /// waiting slot animates itself, would restart its swell from the beginning each time.
    /// </summary>
    private readonly Dictionary<string, string> _drawn = new(StringComparer.Ordinal);

    /// <summary>
    /// Where the swell is up to. One clock for every key, so slots asking together breathe
    /// together rather than each on its own phase.
    /// </summary>
    private readonly Stopwatch _breathing = Stopwatch.StartNew();

    private readonly SessionSlots _slots = new();
    private readonly Lock _gate = new();

    private bool _anyAlerting;

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

            case "keyDown":
                BeginHold(deckEvent.Context);
                break;

            case "keyUp":
                // A hold still counting down means the key was let go too early, which is a
                // tap. One that has already fired left nothing to abandon.
                if (AbandonHold(deckEvent.Context))
                {
                    Tapped(deckEvent.Context);
                }

                break;

            case "willDisappear":
                AbandonHold(deckEvent.Context);

                lock (_gate)
                {
                    _keys.Remove(deckEvent.Context);
                    _drawn.Remove(deckEvent.Context);
                }

                connection.Forget(deckEvent.Context);
                break;
        }

        return Task.CompletedTask;
    }

    private void BeginHold(string context)
    {
        var hold = new CancellationTokenSource();

        lock (_gate)
        {
            // A second keyDown without its keyUp would otherwise leave the first hold
            // counting down against a key nobody is pressing any more.
            AbandonLocked(context);
            _holds[context] = hold;
        }

        // What the hold means is settled now rather than when it fires: a question can be
        // answered in its own window while a finger is still down, and the gesture should do
        // what it meant when it started.
        _ = HeldAsync(context, hold, Meaning(context));
    }

    /// <summary>
    /// What a hold that starts now would do. A hold is still started when it means nothing,
    /// because abandoning it is what tells a tap apart from a press held too long.
    /// </summary>
    private Held Meaning(string context)
    {
        if (Asking(context) is null)
        {
            return Held.Clear;
        }

        return paired() ? Held.Nothing : Held.Allow;
    }

    /// <summary>
    /// The session waiting on this key, when the deck is allowed to answer it. Both halves
    /// matter: a session with a question open, and a mode that lets a key answer one.
    /// </summary>
    private AgentSession? Asking(string context)
    {
        if (modes.Current != DeckMode.Active)
        {
            return null;
        }

        string? sessionId;

        lock (_gate)
        {
            sessionId = _showing.GetValueOrDefault(context);
        }

        if (sessionId is null)
        {
            return null;
        }

        return agents.Snapshot()
            .SelectMany(agent => agent.Sessions)
            .FirstOrDefault(session =>
                session.Id == sessionId && session.PendingTool is { Length: > 0 });
    }

    /// <summary>Returns whether there was a hold still running to abandon.</summary>
    private bool AbandonHold(string context)
    {
        lock (_gate)
        {
            return AbandonLocked(context);
        }
    }

    /// <summary>
    /// A tap: stops this slot flashing. The session is not touched — the deck has stopped
    /// asking, which is all a tap can honestly mean.
    /// </summary>
    private void Tapped(string context)
    {
        // A question takes the gesture over. With a pair on the deck the tap only says which
        // session is meant and the pair answers it; without one the tap is itself the answer
        // that costs a retry and cannot run anything.
        if (Asking(context) is { } asking)
        {
            if (paired())
            {
                addressing.Address(
                    asking.Id,
                    asking.PendingTool!,
                    asking.PendingSummary,
                    DateTimeOffset.UtcNow);
            }
            else
            {
                _ = AnswerAsync(asking, ApprovalDecision.Denied());
            }

            return;
        }

        string? sessionId;

        lock (_gate)
        {
            sessionId = _showing.GetValueOrDefault(context);
        }

        if (sessionId is null)
        {
            return;
        }

        alerts.Acknowledge(sessionId);
        Refresh();
    }

    private async Task AnswerAsync(AgentSession asking, ApprovalDecision decision)
    {
        var reached = await decide(asking.Id, decision);
        PluginLog.Write(reached
            ? decision.Behaviour + " for " + asking.PendingTool + " in " + (asking.Title ?? asking.Id)
            : "nothing left to answer in " + (asking.Title ?? asking.Id));
    }

    private bool AbandonLocked(string context)
    {
        if (!_holds.Remove(context, out var hold))
        {
            return false;
        }

        try
        {
            hold.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The hold completed on its own between being taken from the dictionary and
            // being cancelled, which is the outcome that was wanted anyway.
        }

        return true;
    }


    /// <summary>
    /// Waits out the hold and, if the key is still down, clears the session on it. Holding an
    /// empty slot is a press with nothing to mean and does nothing.
    /// </summary>
    private async Task HeldAsync(string context, CancellationTokenSource hold, Held meaning)
    {
        using (hold)
        {
            try
            {
                await Task.Delay(meaning == Held.Allow ? AllowPress : LongPress, hold.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            string? sessionId;

            lock (_gate)
            {
                // The key can be released in the moment between the wait ending and this
                // lock, so only the hold still registered gets to act.
                if (!_holds.TryGetValue(context, out var current) || current != hold)
                {
                    return;
                }

                _holds.Remove(context);
                sessionId = _showing.GetValueOrDefault(context);
            }

            if (sessionId is null)
            {
                return;
            }

            if (meaning == Held.Nothing)
            {
                return;
            }

            if (meaning == Held.Allow)
            {
                // Asked again rather than trusted: the question may have been answered in its
                // own window while the key was down, and then there is nothing to allow.
                if (Asking(context) is { } asking)
                {
                    await AnswerAsync(asking, new ApprovalDecision(ApprovalDecision.Allow, null));
                }

                return;
            }

            // Nothing is redrawn here. The agent drops the session and reports its new list,
            // and the key clears through the same path as every other change — so a request
            // that does not arrive leaves the key telling the truth rather than lying about
            // a session that is still there.
            await forgetSession(sessionId);
        }
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

        alerts.Settle(sessions.Where(WantsAttention).Select(session => session.Id));

        var addressed = addressing.Current(DateTimeOffset.UtcNow)?.SessionId;
        var alerting = false;

        foreach (var (context, slot) in Slots())
        {
            var live = bySlot.TryGetValue(slot, out var session);
            string face;

            if (live)
            {
                var lit = alerts.Alerting(session!.Id, WantsAttention(session));
                alerting |= lit;
                face = SessionKeyFace.Render(
                    Describe(session),
                    lit ? SlotPulse.Glow(Elapsed()) : 0,
                    answerable: modes.Current == DeckMode.Active,
                    addressed: session.Id == addressed);
            }
            else
            {
                face = SessionKeyFace.Empty();
            }

            lock (_gate)
            {
                if (live)
                {
                    _showing[context] = session!.Id;
                }
                else
                {
                    _showing.Remove(context);
                }

                if (_drawn.TryGetValue(context, out var already) && already == face)
                {
                    continue;
                }

                _drawn[context] = face;
            }

            connection.Update(context, new ImageUpdate(face));
        }

        _anyAlerting = alerting;
    }

    /// <summary>
    /// Sends the next frame of the swell, straight out rather than through the rate limit.
    /// Costs nothing while no slot is asking for attention, which is almost always.
    /// </summary>
    public async Task PulseAsync()
    {
        if (!_anyAlerting)
        {
            return;
        }

        var glow = SlotPulse.Glow(Elapsed());

        foreach (var (context, face) in Alerting(glow))
        {
            lock (_gate)
            {
                _drawn[context] = face;
            }

            await connection.AnimateAsync(context, new ImageUpdate(face));
        }
    }

    /// <summary>The face each swelling key should be showing at this point in the breath.</summary>
    private List<(string Context, string Face)> Alerting(double glow)
    {
        var bySession = agents.Snapshot()
            .SelectMany(agent => agent.Sessions)
            .ToDictionary(session => session.Id, StringComparer.Ordinal);

        var addressed = addressing.Current(DateTimeOffset.UtcNow)?.SessionId;

        List<(string, string)> frames = [];

        lock (_gate)
        {
            foreach (var (context, sessionId) in _showing)
            {
                if (bySession.TryGetValue(sessionId, out var session) &&
                    alerts.Alerting(sessionId, WantsAttention(session)))
                {
                    frames.Add((context, SessionKeyFace.Render(
                        Describe(session),
                        glow,
                        answerable: modes.Current == DeckMode.Active,
                        addressed: sessionId == addressed)));
                }
            }
        }

        return frames;
    }

    private TimeSpan Elapsed() => _breathing.Elapsed;

    /// <summary>How many sessions are waiting to be looked at, muted or not.</summary>
    public int Waiting() =>
        agents.Snapshot().SelectMany(agent => agent.Sessions).Count(WantsAttention);

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

    /// <summary>
    /// Whether this session is waiting on its owner: its turn has ended, or it is stopped at a
    /// question. Both are the deck asking to be looked at, and the alert key counts both.
    /// </summary>
    private static bool WantsAttention(AgentSession session) =>
        session.AwaitingUser || session.PendingTool is { Length: > 0 };

    /// <summary>A visible key: which device it is on, and where. Both decide its slot.</summary>
    private sealed record DeckKey(string? Device, DeckCoordinates? Coordinates);

    private static SessionSlotFace Describe(AgentSession session) => new(
        Enum.TryParse<SessionState>(session.State, out var state) ? state : SessionState.Idle,
        session.Title,
        session.Project,
        session.ContextPercent,
        session.ContextEstimated,
        session.PendingTool,
        session.PendingSummary);
}
