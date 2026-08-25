using System.Text.Json;
using ClaudeDeck.Core.Transcripts;

namespace ClaudeDeck.Core.Tests;

public class TranscriptReaderTests : IDisposable
{
    private static readonly string Fixture = FindFixture();

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "claudedeck-transcripts-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public void The_context_size_is_the_sum_of_the_three_input_counts()
    {
        // Design §4.2, verified against real data: 1 + 3928 + 79076.
        var reading = new TranscriptReader(Fixture).Read();

        Assert.NotNull(reading);
        Assert.Equal(83_005, reading.Tokens);
        Assert.Equal("claude-opus-5", reading.Model);

        // Neither the model nor the branch appears in any hook payload; both ride on the
        // same transcript record as the token count.
        Assert.Equal("main", reading.Branch);
    }

    /// <summary>
    /// Measured in a real transcript: a <c>&lt;synthetic&gt;</c> record ("No response
    /// requested.") is written with every count zeroed. It is the last assistant record in
    /// the fixture, so taking the last one blindly would report an empty context.
    /// </summary>
    [Fact]
    public void A_record_that_accounts_for_nothing_is_not_a_reading()
    {
        var reading = new TranscriptReader(Fixture).Read();

        Assert.Equal(83_005, reading?.Tokens);
    }

    [Fact]
    public void The_fixture_carries_no_message_content_and_no_real_paths()
    {
        var text = File.ReadAllText(Fixture);

        Assert.DoesNotContain(":\\", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", text, StringComparison.Ordinal);

        foreach (var line in File.ReadLines(Fixture))
        {
            if (!JsonDocument.Parse(line).RootElement.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var block in content.EnumerateArray())
            {
                Assert.Equal("", block.GetProperty("text").GetString());
            }
        }
    }

    [Fact]
    public void Only_what_was_appended_is_read_again()
    {
        var path = Write("first.jsonl", Assistant(1, 500, 39_499));
        var reader = new TranscriptReader(path);

        Assert.Equal(40_000, reader.Read()?.Tokens);
        var afterFirst = reader.Offset;

        Append(path, Assistant(1, 3_928, 79_076));

        Assert.Equal(83_005, reader.Read()?.Tokens);
        Assert.True(reader.Offset > afterFirst);
    }

    [Fact]
    public void A_half_written_record_waits_for_the_rest_of_itself()
    {
        // Claude Code appends while this reads, so the tail is routinely an incomplete line.
        var path = Write("partial.jsonl", Assistant(1, 500, 39_499));
        var reader = new TranscriptReader(path);
        Assert.Equal(40_000, reader.Read()?.Tokens);

        var complete = Assistant(1, 3_928, 79_076);
        var half = complete[..(complete.Length / 2)];

        File.AppendAllText(path, half);
        Assert.Equal(40_000, reader.Read()?.Tokens);

        File.AppendAllText(path, complete[(complete.Length / 2)..] + "\n");
        Assert.Equal(83_005, reader.Read()?.Tokens);
    }

    [Fact]
    public void A_file_that_shrank_is_read_from_the_start()
    {
        var path = Write("replaced.jsonl", Assistant(1, 3_928, 79_076));
        var reader = new TranscriptReader(path);
        Assert.Equal(83_005, reader.Read()?.Tokens);

        // A new session writing over the same name has nothing to do with the old reading.
        File.WriteAllText(path, Assistant(1, 500, 39_499) + "\n");

        Assert.Equal(40_000, reader.Read()?.Tokens);
    }

    [Fact]
    public void A_transcript_with_no_usage_yet_reads_as_nothing()
    {
        var path = Write("empty.jsonl", """{"type":"user","message":{"role":"user"}}""" + "\n");

        Assert.Null(new TranscriptReader(path).Read());
    }

    /// <summary>
    /// Captured from a real compaction: it is written into the same transcript the session
    /// was already using, and no assistant record follows until the next turn. Holding the
    /// old number would describe a context that has just been thrown away.
    /// </summary>
    [Fact]
    public void A_compaction_drops_the_reading_until_a_new_one_arrives()
    {
        var path = Write("compacted.jsonl", Assistant(1, 3_928, 79_076));
        var reader = new TranscriptReader(path);
        Assert.Equal(83_005, reader.Read()?.Tokens);

        Append(path, Boundary());
        Assert.Null(reader.Read());

        Append(path, Assistant(1, 500, 39_499));
        Assert.Equal(40_000, reader.Read()?.Tokens);
    }

    [Fact]
    public void A_missing_file_is_not_an_error()
    {
        var reader = new TranscriptReader(Path.Combine(_directory, "never-written.jsonl"));

        Assert.Null(reader.Read());
    }

    private static string Assistant(int input, int cacheCreation, int cacheRead) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            message = new
            {
                model = "claude-opus-5",
                usage = new
                {
                    input_tokens = input,
                    cache_creation_input_tokens = cacheCreation,
                    cache_read_input_tokens = cacheRead,
                    output_tokens = 7,
                },
            },
        });

    /// <summary>The shape Claude Code writes, minus the uuids and the preserved segment.</summary>
    private static string Boundary() =>
        JsonSerializer.Serialize(new
        {
            type = "system",
            subtype = "compact_boundary",
            compactMetadata = new { trigger = "manual", preTokens = 42_701 },
        });

    /// <summary>
    /// The only trace a denied tool call leaves: no hook fires for it, and the turn ends
    /// where it stands. Taken rather than read, because the session goes idle once.
    /// </summary>
    [Fact]
    public void An_interrupted_turn_is_reported_once()
    {
        var path = Write("interrupted.jsonl", """
            {"type":"assistant","message":{"model":"claude-opus-5","usage":{"input_tokens":10}}}
            {"type":"user","message":{"role":"user","content":[{"type":"text","text":"[Request interrupted by user for tool use]"}]}}
            """);
        var reader = new TranscriptReader(path);

        reader.Read();

        Assert.True(reader.TakeInterruption());
        Assert.False(reader.TakeInterruption());
    }

    [Fact]
    public void An_ordinary_turn_reports_no_interruption()
    {
        var path = Write("plain.jsonl", """
            {"type":"user","message":{"role":"user","content":[{"type":"text","text":"run the tests"}]}}
            {"type":"assistant","message":{"model":"claude-opus-5","usage":{"input_tokens":10}}}
            """);
        var reader = new TranscriptReader(path);

        reader.Read();

        Assert.False(reader.TakeInterruption());
    }

    private string Write(string name, string content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content.EndsWith('\n') ? content : content + "\n");
        return path;
    }

    private static void Append(string path, string line) => File.AppendAllText(path, line + "\n");

    private static string FindFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "transcript.jsonl");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("tests/fixtures/transcript.jsonl was not found from " + AppContext.BaseDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
