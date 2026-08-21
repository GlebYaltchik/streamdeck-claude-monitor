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

    /// <summary>
    /// The signal a flashing key rests on. Narrower than Idle, which a session also sits in
    /// from the moment it starts — nobody needs to be called over to a session that has done
    /// nothing yet.
    /// </summary>
    [Fact]
    public void The_end_of_a_turn_is_what_asks_for_the_user()
    {
        var registry = new SessionRegistry();
        registry.Apply(Event("SessionStart", source: "startup"));
        Assert.False(Only(registry).AwaitingUser);

        registry.Apply(Event("UserPromptSubmit"));
        Assert.False(Only(registry).AwaitingUser);

        registry.Apply(Event("Stop"));
        Assert.True(Only(registry).AwaitingUser);
    }

    [Fact]
    public void Going_back_to_a_session_stops_it_asking()
    {
        var registry = Started();
        registry.Apply(Event("Stop"));

        registry.Apply(Event("UserPromptSubmit"));

        Assert.False(Only(registry).AwaitingUser);
    }

    /// <summary>A turn that ended two hours ago is no longer news worth flashing about.</summary>
    [Fact]
    public void A_session_that_goes_stale_stops_asking()
    {
        var registry = Started();
        registry.Apply(Event("Stop"));

        registry.MarkStale("session-1");

        Assert.False(Only(registry).AwaitingUser);
    }

    /// <summary>
    /// Stale is a reading, not a verdict. With no process id to ask, it rests on silence
    /// alone, and a session sitting untouched in an open terminal is indistinguishable from
    /// one whose terminal is gone until it speaks.
    /// </summary>
    [Fact]
    public void A_stale_session_is_alive_again_the_moment_it_speaks()
    {
        var registry = Started();
        registry.MarkStale("session-1");
        Assert.Equal(SessionState.Stale, Only(registry).State);

        registry.Apply(Event("SubagentStop"));

        Assert.Equal(SessionState.Idle, Only(registry).State);
    }

    [Fact]
    public void Forgetting_a_session_frees_its_slot()
    {
        var registry = Started();
        registry.Forget("session-1");

        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void Retiring_a_session_that_is_already_gone_is_harmless()
    {
        var registry = new SessionRegistry();

        registry.MarkStale("never-seen");
        registry.Forget("never-seen");

        Assert.Empty(registry.Snapshot());
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
