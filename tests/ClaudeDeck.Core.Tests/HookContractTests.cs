using System.Text.Json;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Core.Tests;

/// <summary>
/// Replays the payloads captured from a real Claude Code install in Phase 0.
///
/// This is the guard against the format changing underneath us: if a future version renames
/// a field the registry depends on, these fail instead of a key quietly going blank.
/// </summary>
public class HookContractTests
{
    private static readonly string SamplesDirectory = FindSamples();

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(SamplesDirectory, "*.jsonl"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Fact]
    public void The_captured_samples_are_present()
    {
        var files = Directory.GetFiles(SamplesDirectory, "*.jsonl").Select(Path.GetFileNameWithoutExtension).ToList();

        Assert.Contains("SessionStart", files);
        Assert.Contains("PreToolUse", files);
        Assert.Contains("Stop", files);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_captured_payload_still_parses(string fileName)
    {
        var records = Load(Path.Combine(SamplesDirectory, fileName)).ToList();

        Assert.NotEmpty(records);
        Assert.All(records, record =>
            Assert.NotNull(HookEvent.Parse(record.Name, record.Payload, record.CapturedAt)));
    }

    [Fact]
    public void Tool_events_still_carry_the_tool_name_and_the_permission_mode()
    {
        foreach (var name in new[] { "PreToolUse", "PostToolUse" })
        {
            var events = Parse(name).ToList();

            Assert.NotEmpty(events);
            Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e.ToolName)));

            // The live permission mode is what keeps the §6.3 predictor honest.
            Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e.PermissionMode)));
        }
    }

    [Fact]
    public void Session_starts_still_say_why_they_started()
    {
        var sources = Parse("SessionStart").Select(e => e.Source).ToList();

        Assert.All(sources, source => Assert.False(string.IsNullOrEmpty(source)));
        Assert.Contains("startup", sources);

        // The compaction case is the one that must not create a second session.
        Assert.Contains(HookEvent.CompactSource, sources);
    }

    [Fact]
    public void Every_event_still_points_at_a_transcript()
    {
        var events = Directory.GetFiles(SamplesDirectory, "*.jsonl")
            .SelectMany(file => Parse(Path.GetFileNameWithoutExtension(file)!))
            .ToList();

        // Context size comes from the transcript, so losing this link would cost the feature.
        Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e.TranscriptPath)));
    }

    [Fact]
    public void Replaying_a_real_compaction_keeps_one_session()
    {
        var compacted = Parse("SessionStart")
            .First(e => e.Source == HookEvent.CompactSource);

        var registry = new SessionRegistry();
        registry.Apply(Parse("SessionStart").First(e => e.SessionId == compacted.SessionId && e.Source == "startup"));
        registry.Apply(Parse("PreCompact").First(e => e.SessionId == compacted.SessionId));
        registry.Apply(compacted);

        var session = Assert.Single(registry.Snapshot());
        Assert.Equal(compacted.SessionId, session.Id);
    }

    [Fact]
    public void Replaying_everything_in_order_leaves_a_consistent_registry()
    {
        var registry = new SessionRegistry();
        var all = Directory.GetFiles(SamplesDirectory, "*.jsonl")
            .SelectMany(file => Parse(Path.GetFileNameWithoutExtension(file)!))
            .OrderBy(e => e.ReceivedAt)
            .ToList();

        foreach (var hookEvent in all)
        {
            registry.Apply(hookEvent);
        }

        var live = registry.Snapshot();
        Assert.All(live, session => Assert.False(string.IsNullOrEmpty(session.Id)));
        Assert.All(live, session => Assert.True(Enum.IsDefined(session.State)));

        // The sanitizer truncated capturedAt to whole seconds, so events sharing a second
        // have no knowable order. Only sessions whose end is strictly the last thing they
        // did can be asserted to be gone.
        var endedCleanly = all
            .Where(e => e.Name == "SessionEnd")
            .Where(end => all.Where(e => e.SessionId == end.SessionId).Max(e => e.ReceivedAt) < end.ReceivedAt
                          || all.Count(e => e.SessionId == end.SessionId && e.ReceivedAt == end.ReceivedAt) == 1)
            .Select(e => e.SessionId)
            .ToHashSet();

        Assert.NotEmpty(endedCleanly);
        Assert.DoesNotContain(live, session => endedCleanly.Contains(session.Id));
    }

    private static IEnumerable<HookEvent> Parse(string name) =>
        Load(Path.Combine(SamplesDirectory, name + ".jsonl"))
            .Select(record => HookEvent.Parse(record.Name, record.Payload, record.CapturedAt))
            .OfType<HookEvent>();

    private static IEnumerable<Sample> Load(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var root = JsonDocument.Parse(line).RootElement;
            yield return new Sample(
                root.GetProperty("hookArgument").GetString()!,
                root.GetProperty("payload").Clone(),
                root.GetProperty("capturedAt").GetDateTimeOffset());
        }
    }

    private static string FindSamples()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "findings", "hooks");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate docs/findings/hooks from the test output directory.");
    }

    private sealed record Sample(string Name, JsonElement Payload, DateTimeOffset CapturedAt);
}
