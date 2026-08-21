using System.Text;
using System.Text.Json;

namespace ClaudeDeck.Core.Transcripts;

/// <summary>
/// What the last usable record in a transcript says about the session.
///
/// The model and the branch ride along because no hook payload carries either, and they come
/// out of the same record as the token count. The title comes from its own record and is what
/// tells two sessions in the same repository apart.
/// </summary>
public sealed record TranscriptReading(int Tokens, string? Model, string? Branch, string? Title = null);

/// <summary>
/// Follows one session's transcript and reports the size of its context.
///
/// The file only ever grows, so each pass reads from the byte offset the previous one
/// stopped at. A transcript reaches megabytes within a session, and a poll that re-read it
/// whole would cost more than the number is worth.
///
/// Only whole lines are consumed. Claude Code appends while this reads, so the tail is
/// routinely half a record; the offset stops at the last newline and the remainder is picked
/// up once it is complete.
/// </summary>
public sealed class TranscriptReader(string path)
{
    private const int ChunkSize = 64 * 1024;

    /// <summary>Where the next pass starts. Exposed so a test can prove nothing is re-read.</summary>
    public long Offset { get; private set; }

    /// <summary>
    /// The last reading obtained, kept when a pass brings nothing new. The title is tracked
    /// separately because it arrives in its own records, and survives a compaction: the
    /// session is renamed by neither.
    /// </summary>
    public TranscriptReading? Latest => _usage is null ? null : _usage with { Title = _title };

    private TranscriptReading? _usage;
    private string? _title;

    /// <summary>
    /// Consumes whatever has been appended since the last call. Returns the newest reading,
    /// which is unchanged when the new records carry no usage.
    /// </summary>
    public TranscriptReading? Read()
    {
        try
        {
            // The file is open for writing in the session that owns it.
            using var file = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (file.Length < Offset)
            {
                // Shorter than last time means a different file under the same name, so
                // everything known about the old one is wrong.
                Reset();
            }

            if (file.Length == Offset)
            {
                return Latest;
            }

            file.Seek(Offset, SeekOrigin.Begin);
            Consume(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A transcript that cannot be read right now is not a reason to lose the reading
            // that was already taken.
        }

        return Latest;
    }

    private void Reset()
    {
        Offset = 0;
        _usage = null;
        _title = null;
    }

    /// <summary>
    /// Reads from <see cref="Offset"/> to the end, advancing it one complete line at a time.
    ///
    /// The half-written tail is deliberately not carried across passes. It stays on disk and
    /// is read again from the same offset, which costs one re-read of at most one record and
    /// removes any chance of a buffered fragment being counted twice.
    /// </summary>
    private void Consume(Stream file)
    {
        var line = new List<byte>();
        var chunk = new byte[ChunkSize];
        int count;

        while ((count = file.Read(chunk)) > 0)
        {
            var region = chunk.AsSpan(0, count);

            while (!region.IsEmpty)
            {
                var newline = region.IndexOf((byte)'\n');
                if (newline < 0)
                {
                    // A record longer than one chunk; keep it and read on. Whatever is still
                    // held when the file ends was an incomplete line and is dropped.
                    line.AddRange(region);
                    break;
                }

                line.AddRange(region[..newline]);
                Apply(Encoding.UTF8.GetString(line.ToArray()));

                Offset += line.Count + 1;
                line.Clear();
                region = region[(newline + 1)..];
            }
        }
    }

    private void Apply(string line)
    {
        if (line.AsSpan().Trim().IsEmpty)
        {
            return;
        }

        try
        {
            var record = JsonDocument.Parse(line).RootElement;
            var type = Read(record, "type");

            // Measured on a real compaction: it is written into the same transcript as a
            // `compact_boundary` record, carrying the size it compacted away, and no
            // assistant record follows until the session takes its next turn. The reading
            // held until then describes a context that no longer exists, so it is dropped
            // rather than shown. What replaced it is unknown until the next turn says so.
            if (type == "system" && Read(record, "subtype") == "compact_boundary")
            {
                _usage = null;
                return;
            }

            // Two record types name a session: the one the user set and the one Claude wrote.
            // Whichever came last is its current name.
            if ((Read(record, "customTitle") ?? Read(record, "aiTitle")) is { Length: > 0 } title)
            {
                _title = title;
                return;
            }

            if (type != "assistant" ||
                !record.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("usage", out var usage))
            {
                return;
            }

            var tokens = Count(usage, "input_tokens")
                         + Count(usage, "cache_creation_input_tokens")
                         + Count(usage, "cache_read_input_tokens");

            // Measured: a `<synthetic>` record ("No response requested.") is written with
            // every count zeroed. Taking it would report an empty context for a session
            // holding half a million tokens, so a record that accounts for nothing is not a
            // reading.
            if (tokens > 0)
            {
                _usage = new TranscriptReading(tokens, Read(message, "model"), Read(record, "gitBranch"));
            }
        }
        catch (JsonException)
        {
            // Transcripts are written by another process and read while it writes. A line
            // that will not parse is skipped; the next one usually does.
        }
    }

    private static int Count(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.TryGetInt32(out var count) ? count : 0;

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
