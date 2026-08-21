using System.Globalization;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Agent;

/// <summary>
/// Retires sessions that have gone without ever saying so.
///
/// <c>SessionEnd</c> cannot be relied on: a session left open emits none, measured directly
/// (findings/hooks.md). Without this, a slot taken by a session whose terminal was closed
/// hours ago is held for as long as the agent runs.
///
/// Two signs of life, both already to hand: the last hook the session caused, and the last
/// write to its transcript. The transcript earns its place because it moves during a turn
/// that fires no hook we subscribe to, and because it is written by the session's own
/// process — if it is growing, something is alive.
///
/// The PID is deliberately not among them. Design §4.1 described walking the hook process's
/// ancestors, which the interim <c>curl</c> shim cannot do: it forwards stdin and exits, and
/// no hook payload carries a process id. That leg waits for the shim of design §3.2.
///
/// Silence is answered in two stages. <see cref="SessionState.Stale"/> first, which says no
/// more than it knows and is undone by the session's next event; removal only much later,
/// because that is what actually frees the slot.
/// </summary>
internal sealed class LivenessMonitor(
    SessionRegistry sessions,
    TimeSpan staleAfter,
    TimeSpan forgetAfter,
    Action<string> log,
    Func<DateTimeOffset>? clock = null)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Raised after a sweep that retired something.</summary>
    public event Action? Changed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (Sweep())
                {
                    Changed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                log($"liveness sweep failed: {ex.Message}");
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass over the live sessions. Returns whether anything changed.</summary>
    internal bool Sweep()
    {
        var now = _clock();
        var changed = false;

        foreach (var session in sessions.Snapshot())
        {
            var silence = now - LastSignOfLife(session);

            if (silence >= forgetAfter)
            {
                sessions.Forget(session.Id);
                log($"session {session.Id} dropped after {Describe(silence)} without a sign of life");
                changed = true;
                continue;
            }

            if (silence >= staleAfter && session.State != SessionState.Stale)
            {
                sessions.MarkStale(session.Id);
                log($"session {session.Id} stale after {Describe(silence)} without a sign of life");
                changed = true;
            }
        }

        return changed;
    }

    private static DateTimeOffset LastSignOfLife(Session session)
    {
        var written = LastWritten(session.TranscriptPath);
        return written > session.LastEventAt ? written : session.LastEventAt;
    }

    /// <summary>
    /// When the transcript was last appended to, or the beginning of time when there is no
    /// transcript to ask. Falling back low leaves the hook events as the only evidence,
    /// which is the right answer for a session whose file has been deleted under it.
    /// </summary>
    private static DateTimeOffset LastWritten(string? path)
    {
        if (path is not { Length: > 0 })
        {
            return DateTimeOffset.MinValue;
        }

        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.LastWriteTimeUtc : DateTimeOffset.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    // Invariant: this machine is a comma-decimal locale and "2,5h" in a log reads as two
    // values.
    private static string Describe(TimeSpan silence) =>
        silence.TotalHours >= 1
            ? silence.TotalHours.ToString("F1", CultureInfo.InvariantCulture) + "h"
            : silence.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture) + "m";
}
