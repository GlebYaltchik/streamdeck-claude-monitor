using System.Text.Json;
using ClaudeDeck.Agent;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Agent.Tests;

public class LivenessMonitorTests : IDisposable
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(6);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "claudedeck-liveness-" + Guid.NewGuid().ToString("n"));

    private DateTimeOffset _now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_session_that_has_just_spoken_is_left_alone()
    {
        var sessions = Started("session-1", Write("session.jsonl"));

        Assert.False(Monitor(sessions).Sweep());
        Assert.Equal(SessionState.Idle, sessions.Snapshot().Single().State);
    }

    /// <summary>
    /// The failure this exists to fix: a session left open emits no <c>SessionEnd</c>, so
    /// nothing else would ever retire its slot.
    /// </summary>
    [Fact]
    public void A_session_silent_past_the_timeout_goes_stale()
    {
        var sessions = Started("session-1", Write("session.jsonl"));
        var monitor = Monitor(sessions);

        _now += StaleAfter;

        Assert.True(monitor.Sweep());
        Assert.Equal(SessionState.Stale, sessions.Snapshot().Single().State);
    }

    [Fact]
    public void A_session_already_stale_is_not_reported_again()
    {
        var sessions = Started("session-1", Write("session.jsonl"));
        var monitor = Monitor(sessions);
        _now += StaleAfter;
        Assert.True(monitor.Sweep());

        Assert.False(monitor.Sweep());
    }

    [Fact]
    public void A_stale_session_comes_back_on_its_next_event()
    {
        var sessions = Started("session-1", Write("session.jsonl"));
        var monitor = Monitor(sessions);
        _now += StaleAfter;
        monitor.Sweep();

        sessions.Apply(Hook("UserPromptSubmit", "session-1", null));

        Assert.Equal(SessionState.Working, sessions.Snapshot().Single().State);
    }

    /// <summary>
    /// A session can work for an hour without a hook we subscribe to, so hook silence alone
    /// would grey out a key while the model was mid-turn. The transcript is the second
    /// witness, and it is written by the session's own process.
    /// </summary>
    [Fact]
    public void A_growing_transcript_keeps_a_session_alive_on_its_own()
    {
        var path = Write("session.jsonl");
        var sessions = Started("session-1", path);
        var monitor = Monitor(sessions);

        _now += StaleAfter + TimeSpan.FromMinutes(5);
        File.SetLastWriteTimeUtc(path, _now.UtcDateTime);

        Assert.False(monitor.Sweep());
        Assert.Equal(SessionState.Idle, sessions.Snapshot().Single().State);
    }

    [Fact]
    public void Silence_long_enough_frees_the_slot_altogether()
    {
        var sessions = Started("session-1", Write("session.jsonl"));
        var monitor = Monitor(sessions);

        _now += ForgetAfter;

        Assert.True(monitor.Sweep());
        Assert.Empty(sessions.Snapshot());
    }

    /// <summary>
    /// No process id is available with the interim curl shim, so a session whose transcript
    /// has been deleted has nothing left but its hooks. It must still be retired rather than
    /// crash the sweep.
    /// </summary>
    [Fact]
    public void A_session_whose_transcript_is_gone_is_judged_on_its_hooks_alone()
    {
        var sessions = Started("session-1", Path.Combine(_directory, "never-written.jsonl"));
        var monitor = Monitor(sessions);

        Assert.False(monitor.Sweep());

        _now += StaleAfter;

        Assert.True(monitor.Sweep());
        Assert.Equal(SessionState.Stale, sessions.Snapshot().Single().State);
    }

    [Fact]
    public void A_session_with_no_transcript_path_at_all_is_still_swept()
    {
        var sessions = new SessionRegistry();
        sessions.Apply(Hook("SessionStart", "session-1", null));
        var monitor = Monitor(sessions);

        _now += StaleAfter;

        Assert.True(monitor.Sweep());
        Assert.Equal(SessionState.Stale, sessions.Snapshot().Single().State);
    }

    [Fact]
    public void One_silent_session_does_not_retire_a_busy_one()
    {
        var busy = Write("busy.jsonl");
        var sessions = new SessionRegistry();
        sessions.Apply(Hook("SessionStart", "quiet", Write("quiet.jsonl")));
        sessions.Apply(Hook("SessionStart", "busy", busy));
        var monitor = Monitor(sessions);

        _now += StaleAfter;
        File.SetLastWriteTimeUtc(busy, _now.UtcDateTime);

        Assert.True(monitor.Sweep());

        var states = sessions.Snapshot().ToDictionary(session => session.Id, session => session.State);
        Assert.Equal(SessionState.Stale, states["quiet"]);
        Assert.Equal(SessionState.Idle, states["busy"]);
    }

    private LivenessMonitor Monitor(SessionRegistry sessions) =>
        new(sessions, StaleAfter, ForgetAfter, _ => { }, () => _now);

    private SessionRegistry Started(string sessionId, string transcriptPath)
    {
        var registry = new SessionRegistry();
        registry.Apply(Hook("SessionStart", sessionId, transcriptPath));
        return registry;
    }

    private HookEvent Hook(string name, string sessionId, string? transcriptPath)
    {
        var payload = JsonSerializer.Serialize(new
        {
            session_id = sessionId,
            cwd = "/work/project",
            transcript_path = transcriptPath,
            source = "startup",
        });

        return HookEvent.Parse(name, JsonDocument.Parse(payload).RootElement, _now)!;
    }

    private string Write(string name)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "{}\n");
        File.SetLastWriteTimeUtc(path, _now.UtcDateTime);
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
