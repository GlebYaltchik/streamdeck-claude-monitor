using ClaudeDeck.Core.Sessions;
using ClaudeDeck.Core.Transcripts;

namespace ClaudeDeck.Agent;

/// <summary>
/// Follows the transcript of every live session and keeps its context fill current.
///
/// Polling, not file watching: design §3.1. A watcher on a file another process appends to
/// several times a second delivers a storm of notifications for a number that only has to be
/// right to the nearest second.
///
/// The poll is debounced twice over. A transcript whose length has not moved is skipped
/// without the file being opened at all, and a pass that yields the same reading as before
/// tells nobody. Between them the agent touches a transcript only when it has something new
/// to say.
/// </summary>
internal sealed class ContextTracker(SessionRegistry sessions, Action<string> log)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, TranscriptReader> _readers = new(StringComparer.Ordinal);

    /// <summary>Raised after a pass that changed something worth sending.</summary>
    public event Action? Changed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (Poll())
                {
                    Changed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                // Reading a transcript is a convenience. Failing at it must not stop the
                // agent recording hooks, which is the part a session depends on.
                log($"context poll failed: {ex.Message}");
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
    internal bool Poll()
    {
        var live = sessions.Snapshot();
        var changed = false;

        foreach (var session in live)
        {
            if (session.TranscriptPath is not { Length: > 0 } path)
            {
                continue;
            }

            var reader = Reader(session.Id, path);

            // Length comes from the directory entry, so an idle session costs no open file
            // handle at all. Inequality rather than "greater than": a shorter file is a
            // different file, and the reader has to see that.
            if (Length(path) == reader.Offset)
            {
                continue;
            }

            var reading = reader.Read();

            if (reading is null)
            {
                // A compaction drops the reading. Saying nothing would leave the old number
                // on the key, describing a context that has just been thrown away.
                if (session.Context is not null)
                {
                    sessions.Report(session.Id, null);
                    changed = true;
                }

                continue;
            }

            if (Moved(session, reading))
            {
                sessions.Report(session.Id, reading);
                changed = true;
            }
        }

        Forget(live);
        return changed;
    }

    /// <summary>
    /// Whether a reading says anything the session does not already know. Records without
    /// usage leave the reading untouched, so most passes during a turn land here and stop.
    /// </summary>
    private static bool Moved(Session session, TranscriptReading reading) =>
        session.Context?.Tokens != reading.Tokens ||
        (reading.Model is not null && session.Model != reading.Model) ||
        (reading.Title is not null && session.Title != reading.Title);

    private TranscriptReader Reader(string sessionId, string path)
    {
        if (!_readers.TryGetValue(sessionId, out var reader))
        {
            reader = new TranscriptReader(path);
            _readers[sessionId] = reader;
        }

        return reader;
    }

    private void Forget(IReadOnlyList<Session> live)
    {
        var alive = live.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var id in _readers.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _readers.Remove(id);
        }
    }

    private static long Length(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
