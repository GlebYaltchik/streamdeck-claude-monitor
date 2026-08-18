using System.Text.Json;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Core.Tests;

public class SessionRegistryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_session_starts_idle()
    {
        var registry = new SessionRegistry();
        registry.Apply(Event("SessionStart", source: "startup"));

        var session = Assert.Single(registry.Snapshot());
        Assert.Equal(SessionState.Idle, session.State);
    }

    [Fact]
    public void Submitting_a_prompt_starts_work()
    {
        var registry = Started();
        registry.Apply(Event("UserPromptSubmit"));

        Assert.Equal(SessionState.Working, Only(registry).State);
    }

    [Fact]
    public void A_tool_call_names_the_running_tool_and_clears_it_afterwards()
    {
        var registry = Started();

        registry.Apply(Event("PreToolUse", tool: "Bash"));
        Assert.Equal("Bash", Only(registry).CurrentTool);

        registry.Apply(Event("PostToolUse", tool: "Bash"));
        Assert.Null(Only(registry).CurrentTool);
        Assert.Equal(SessionState.Working, Only(registry).State);
    }

    [Fact]
    public void Stop_means_the_user_is_being_waited_for()
    {
        var registry = Started();
        registry.Apply(Event("UserPromptSubmit"));
        registry.Apply(Event("PreToolUse", tool: "Bash"));
        registry.Apply(Event("Stop"));

        var session = Only(registry);
        Assert.Equal(SessionState.Idle, session.State);
        Assert.Null(session.CurrentTool);
    }

    [Fact]
    public void Compacting_a_session_does_not_start_a_new_one()
    {
        var registry = Started();
        registry.Apply(Event("UserPromptSubmit"));
        registry.Apply(Event("PreCompact"));

        Assert.Equal(SessionState.Compacting, Only(registry).State);

        // Measured in Phase 0: compaction emits a second SessionStart carrying the same
        // session id. Treating it as new would move the session to a different key.
        registry.Apply(Event("SessionStart", source: "compact"));

        Assert.Single(registry.Snapshot());
        Assert.Equal(SessionState.Compacting, Only(registry).State);
    }

    [Fact]
    public void A_subagent_belongs_to_its_parent_rather_than_getting_its_own_slot()
    {
        var registry = Started();
        registry.Apply(Event("UserPromptSubmit"));
        registry.Apply(Event("SubagentStop"));
        registry.Apply(Event("SubagentStop"));

        var session = Assert.Single(registry.Snapshot());
        Assert.Equal(2, session.SubagentRuns);
        Assert.Equal(SessionState.Working, session.State);
    }

    [Fact]
    public void Ending_a_session_removes_it()
    {
        var registry = Started();
        registry.Apply(Event("SessionEnd", reason: "other"));

        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void Sessions_are_tracked_independently()
    {
        var registry = new SessionRegistry();
        registry.Apply(Event("SessionStart", session: "one"));
        registry.Apply(Event("SessionStart", session: "two"));
        registry.Apply(Event("UserPromptSubmit", session: "two"));

        var sessions = registry.Snapshot();
        Assert.Equal(2, sessions.Count);
        Assert.Equal(SessionState.Idle, sessions.Single(s => s.Id == "one").State);
        Assert.Equal(SessionState.Working, sessions.Single(s => s.Id == "two").State);
    }

    [Fact]
    public void Details_carried_by_later_events_are_kept()
    {
        var registry = new SessionRegistry();
        registry.Apply(Event("SessionStart", cwd: @"D:\work\project", transcript: "/t.jsonl"));
        registry.Apply(Event("PreToolUse", tool: "Bash", mode: "acceptEdits"));

        var session = Only(registry);
        Assert.Equal("project", session.Project);
        Assert.Equal("/t.jsonl", session.TranscriptPath);
        Assert.Equal("acceptEdits", session.PermissionMode);
    }

    [Fact]
    public void An_event_without_a_session_id_is_not_an_event_we_can_use()
    {
        using var document = JsonDocument.Parse("""{"hook_event_name":"Stop"}""");

        Assert.Null(HookEvent.Parse("Stop", document.RootElement, Start));
    }

    private static SessionRegistry Started()
    {
        var registry = new SessionRegistry();
        registry.Apply(Event("SessionStart", source: "startup"));
        return registry;
    }

    private static Session Only(SessionRegistry registry) => Assert.Single(registry.Snapshot());

    private static HookEvent Event(
        string name,
        string session = "session-1",
        string? tool = null,
        string? source = null,
        string? reason = null,
        string? cwd = null,
        string? transcript = null,
        string? mode = null) =>
        new(name, session, Start, cwd, transcript, mode, tool, source, reason);
}
