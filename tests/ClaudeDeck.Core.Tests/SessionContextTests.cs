using System.Text.Json;
using ClaudeDeck.Core.Sessions;
using ClaudeDeck.Core.Transcripts;

namespace ClaudeDeck.Core.Tests;

public class SessionContextTests
{
    [Fact]
    public void A_reading_fills_in_what_no_hook_payload_carries()
    {
        var registry = Started("session-1");

        registry.Report("session-1", new TranscriptReading(250_000, "claude-opus-5", "main"));

        var session = registry.Snapshot().Single();
        Assert.Equal("claude-opus-5", session.Model);
        Assert.Equal("main", session.Branch);
        Assert.Equal(25, session.Context?.Percent);
    }

    [Fact]
    public void A_later_reading_that_names_nothing_keeps_the_model_and_branch()
    {
        var registry = Started("session-1");
        registry.Report("session-1", new TranscriptReading(250_000, "claude-opus-5", "main"));

        registry.Report("session-1", new TranscriptReading(300_000, null, null));

        var session = registry.Snapshot().Single();
        Assert.Equal("claude-opus-5", session.Model);
        Assert.Equal("main", session.Branch);
        Assert.Equal(30, session.Context?.Percent);
    }

    [Fact]
    public void A_session_that_has_ended_is_not_brought_back()
    {
        // The reader can still be mid-pass when SessionEnd arrives.
        var registry = Started("session-1");
        registry.Apply(new HookEvent("SessionEnd", "session-1", DateTimeOffset.UtcNow));

        registry.Report("session-1", new TranscriptReading(250_000, "claude-opus-5", "main"));

        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void Compaction_shows_up_as_the_context_dropping()
    {
        var registry = Started("session-1");
        registry.Report("session-1", new TranscriptReading(820_000, "claude-opus-5", "main"));
        Assert.Equal(82, registry.Snapshot().Single().Context?.Percent);

        registry.Apply(new HookEvent("PreCompact", "session-1", DateTimeOffset.UtcNow));
        registry.Report("session-1", new TranscriptReading(90_000, "claude-opus-5", "main"));

        Assert.Equal(9, registry.Snapshot().Single().Context?.Percent);
    }

    private static SessionRegistry Started(string sessionId)
    {
        var registry = new SessionRegistry();
        var payload = JsonDocument.Parse(
            $$"""{"session_id":"{{sessionId}}","cwd":"/work/project","source":"startup"}""").RootElement;

        registry.Apply(HookEvent.Parse("SessionStart", payload, DateTimeOffset.UtcNow)!);
        return registry;
    }
}
