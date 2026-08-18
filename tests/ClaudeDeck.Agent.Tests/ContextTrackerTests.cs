using System.Text.Json;
using ClaudeDeck.Agent;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Agent.Tests;

public class ContextTrackerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "claudedeck-tracker-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public void A_live_session_gets_its_context_from_its_transcript()
    {
        var path = Write("session.jsonl", Assistant(1, 3_928, 79_076));
        var sessions = Started("session-1", path);
        var tracker = new ContextTracker(sessions, _ => { });

        Assert.True(tracker.Poll());

        var session = sessions.Snapshot().Single();
        Assert.Equal(83_005, session.Context?.Tokens);
        Assert.Equal("claude-opus-5", session.Model);
        Assert.Equal("main", session.Branch);
    }

    [Fact]
    public void A_pass_over_an_unchanged_transcript_reports_nothing()
    {
        var path = Write("session.jsonl", Assistant(1, 3_928, 79_076));
        var tracker = new ContextTracker(Started("session-1", path), _ => { });
        Assert.True(tracker.Poll());

        Assert.False(tracker.Poll());
    }

    [Fact]
    public void Records_without_usage_do_not_count_as_a_change()
    {
        // Most appends during a turn are user and tool records. Waking the deck for those
        // would be a message per tool call with the same number in it.
        var path = Write("session.jsonl", Assistant(1, 3_928, 79_076));
        var tracker = new ContextTracker(Started("session-1", path), _ => { });
        Assert.True(tracker.Poll());

        File.AppendAllText(path, """{"type":"user","message":{"role":"user"}}""" + "\n");

        Assert.False(tracker.Poll());
    }

    [Fact]
    public void A_growing_transcript_moves_the_reported_context()
    {
        var path = Write("session.jsonl", Assistant(1, 500, 39_499));
        var sessions = Started("session-1", path);
        var tracker = new ContextTracker(sessions, _ => { });
        Assert.True(tracker.Poll());
        Assert.Equal(40_000, sessions.Snapshot().Single().Context?.Tokens);

        File.AppendAllText(path, Assistant(1, 3_928, 79_076) + "\n");

        Assert.True(tracker.Poll());
        Assert.Equal(83_005, sessions.Snapshot().Single().Context?.Tokens);
    }

    /// <summary>
    /// The real sequence, measured: PreCompact and the SessionStart that follows it both
    /// name the same transcript, the boundary is written into it, and no assistant record
    /// arrives until the session's next turn. The key must not keep showing the old fill
    /// through all of that.
    /// </summary>
    [Fact]
    public void A_compaction_takes_the_context_off_the_session()
    {
        var path = Write("session.jsonl", Assistant(1, 3_928, 79_076));
        var sessions = Started("session-1", path);
        var tracker = new ContextTracker(sessions, _ => { });
        Assert.True(tracker.Poll());
        Assert.NotNull(sessions.Snapshot().Single().Context);

        File.AppendAllText(path, Boundary() + "\n");

        Assert.True(tracker.Poll());
        var session = sessions.Snapshot().Single();
        Assert.Null(session.Context);

        // What it is running and where stays true across a compaction.
        Assert.Equal("claude-opus-5", session.Model);
        Assert.Equal("main", session.Branch);
    }

    [Fact]
    public void A_session_without_a_transcript_yet_is_skipped()
    {
        var sessions = Started("session-1", Path.Combine(_directory, "not-written-yet.jsonl"));
        var tracker = new ContextTracker(sessions, _ => { });

        Assert.False(tracker.Poll());
        Assert.Null(sessions.Snapshot().Single().Context);
    }

    private static string Assistant(int input, int cacheCreation, int cacheRead) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            gitBranch = "main",
            message = new
            {
                model = "claude-opus-5",
                usage = new
                {
                    input_tokens = input,
                    cache_creation_input_tokens = cacheCreation,
                    cache_read_input_tokens = cacheRead,
                },
            },
        });

    private static string Boundary() =>
        JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "compact_boundary",
            compactMetadata = new { trigger = "manual", preTokens = 42_701 },
        });

    private static SessionRegistry Started(string sessionId, string transcriptPath)
    {
        var registry = new SessionRegistry();
        var payload = JsonSerializer.Serialize(new
        {
            session_id = sessionId,
            cwd = "/work/project",
            transcript_path = transcriptPath,
            source = "startup",
        });

        registry.Apply(HookEvent.Parse("SessionStart", JsonDocument.Parse(payload).RootElement, DateTimeOffset.UtcNow)!);
        return registry;
    }

    private string Write(string name, string content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content + "\n");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
