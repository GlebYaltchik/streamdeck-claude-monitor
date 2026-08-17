using System.Text.Json;
using ClaudeDeck.Agent;

namespace ClaudeDeck.Agent.Tests;

public class EventLogTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "claudedeck-tests", Guid.NewGuid().ToString("N"));

    private EventLog Create() => new(Path.Combine(_directory, "events.ndjson"));

    [Fact]
    public void An_event_is_one_line_carrying_the_payload()
    {
        var log = Create();

        log.Append("PreToolUse", """{"session_id":"abc","tool_name":"Bash"}""");

        var lines = File.ReadAllLines(log.Path);
        Assert.Single(lines);

        var record = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Equal("PreToolUse", record.GetProperty("event").GetString());
        Assert.Equal("abc", record.GetProperty("payload").GetProperty("session_id").GetString());
        Assert.True(record.TryGetProperty("receivedAt", out _));
    }

    [Fact]
    public void A_multi_line_payload_still_occupies_one_line()
    {
        var log = Create();

        // Whatever arrives, the file has to stay newline-delimited to be readable.
        log.Append("Stop", "{\n  \"session_id\": \"abc\"\n}");

        Assert.Single(File.ReadAllLines(log.Path));
    }

    [Fact]
    public void An_unparseable_payload_is_kept_rather_than_dropped()
    {
        var log = Create();

        log.Append("Stop", "not json at all");

        var record = JsonDocument.Parse(File.ReadAllLines(log.Path)[0]).RootElement;

        // An event we cannot read is still evidence that it arrived.
        Assert.Equal("not json at all", record.GetProperty("payload").GetString());
    }

    [Fact]
    public void Events_append_rather_than_replace()
    {
        var log = Create();

        log.Append("SessionStart", """{"session_id":"a"}""");
        log.Append("Stop", """{"session_id":"a"}""");

        Assert.Equal(2, File.ReadAllLines(log.Path).Length);
    }

    [Fact]
    public void The_directory_is_created_on_first_write()
    {
        var log = Create();

        Assert.False(Directory.Exists(_directory));
        log.Append("SessionStart", "{}");

        Assert.True(File.Exists(log.Path));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
