using ClaudeDeck.Core.Transcripts;

namespace ClaudeDeck.Core.Sessions;

/// <summary>
/// Turns a stream of hook events into the current state of every live session.
///
/// The transitions are design §4.1, and three of them exist because Phase 0 measured
/// behaviour that was not obvious:
///
/// - a compaction emits a second <c>SessionStart</c> with the same session id, so it
///   continues a session instead of starting one;
/// - <c>SubagentStop</c> belongs to its parent session and never creates one;
/// - <c>Notification</c> is not handled at all, because it does not fire for permission
///   prompts, which is the only thing it would have been used for.
/// </summary>
public sealed class SessionRegistry(Func<DateTimeOffset>? clock = null)
{
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock _gate = new();

    public IReadOnlyList<Session> Snapshot()
    {
        lock (_gate)
        {
            return [.. _sessions.Values.OrderBy(session => session.StartedAt)];
        }
    }

    public Session? Find(string sessionId)
    {
        lock (_gate)
        {
            return _sessions.GetValueOrDefault(sessionId);
        }
    }

    public void Apply(HookEvent hookEvent)
    {
        lock (_gate)
        {
            if (hookEvent.Name == "SessionEnd")
            {
                _sessions.Remove(hookEvent.SessionId);
                return;
            }

            var session = _sessions.GetValueOrDefault(hookEvent.SessionId) ?? New(hookEvent);
            _sessions[hookEvent.SessionId] = Advance(session, hookEvent);
        }
    }

    /// <summary>
    /// Attaches what a transcript says about a session. A session that has already ended is
    /// ignored rather than resurrected: the reader can still be mid-pass when it goes.
    ///
    /// A reading that names no model or branch leaves the known ones alone. Only records
    /// that carry usage produce a reading, and not every one of those repeats the rest.
    ///
    /// No reading at all means the context is no longer known — what a compaction leaves
    /// behind until the next turn. The model and branch survive it; they are still true.
    /// </summary>
    public void Report(string sessionId, TranscriptReading? reading)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return;
            }

            if (reading is null)
            {
                _sessions[sessionId] = session with { Context = null };
                return;
            }

            // The window is resolved from the model the session knows, not from whatever
            // this one reading happened to name. A reading without a model would otherwise
            // take the 200k fallback and turn a 30% key into 150%.
            var model = reading.Model ?? session.Model;

            _sessions[sessionId] = session with
            {
                Title = reading.Title ?? session.Title,
                Model = model,
                Branch = reading.Branch ?? session.Branch,
                Context = ContextFill.Of(reading with { Model = model }),
            };
        }
    }

    /// <summary>
    /// Marks a session as showing no sign of life. Not a removal: it may still be sitting in
    /// an open terminal with nothing to do, and its next event takes the mark off again.
    /// </summary>
    public void MarkStale(string sessionId)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                // A turn that ended two hours ago is no longer news worth flashing about.
                _sessions[sessionId] = session with { State = SessionState.Stale, AwaitingUser = false };
            }
        }
    }

    /// <summary>
    /// Drops one session for good. <c>SessionEnd</c> was measured not to arrive for a session
    /// that is simply left open, so a slot held by something long gone is released on silence
    /// alone or never.
    /// </summary>
    public void Forget(string sessionId)
    {
        lock (_gate)
        {
            _sessions.Remove(sessionId);
        }
    }

    /// <summary>Drops sessions whose id is not in the given set, used after a rescan.</summary>
    public void RetainOnly(IReadOnlySet<string> sessionIds)
    {
        lock (_gate)
        {
            foreach (var id in _sessions.Keys.Where(id => !sessionIds.Contains(id)).ToList())
            {
                _sessions.Remove(id);
            }
        }
    }

    private Session New(HookEvent hookEvent) => new()
    {
        Id = hookEvent.SessionId,
        State = SessionState.Idle,
        StartedAt = hookEvent.ReceivedAt,
        LastEventAt = hookEvent.ReceivedAt,
    };

    private static Session Advance(Session session, HookEvent hookEvent)
    {
        var updated = session with
        {
            // An event of any kind is proof of life. A session marked stale on silence is
            // only stale until it speaks again, and most of the transitions below overwrite
            // this anyway.
            State = session.State == SessionState.Stale ? SessionState.Idle : session.State,

            // Anything happening means the turn is no longer sitting finished and unread.
            // Only Stop below sets this, so it survives exactly one event.
            AwaitingUser = false,
            LastEventAt = hookEvent.ReceivedAt,
            Cwd = hookEvent.Cwd ?? session.Cwd,
            TranscriptPath = hookEvent.TranscriptPath ?? session.TranscriptPath,
            PermissionMode = hookEvent.PermissionMode ?? session.PermissionMode,
        };

        return hookEvent.Name switch
        {
            // A compaction keeps the session and whatever it was doing.
            "SessionStart" when hookEvent.Source == HookEvent.CompactSource => updated,
            "SessionStart" => updated with { State = SessionState.Idle, CurrentTool = null },

            "UserPromptSubmit" => updated with { State = SessionState.Working },
            "PreToolUse" => updated with { State = SessionState.Working, CurrentTool = hookEvent.ToolName },
            "PostToolUse" => updated with { State = SessionState.Working, CurrentTool = null },

            // The turn is over, which is exactly "waiting for the user".
            "Stop" => updated with { State = SessionState.Idle, CurrentTool = null, AwaitingUser = true },

            "PreCompact" => updated with { State = SessionState.Compacting },
            "SubagentStop" => updated with { SubagentRuns = session.SubagentRuns + 1 },

            _ => updated,
        };
    }
}
