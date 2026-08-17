using System.Text;
using System.Text.Json;

namespace ClaudeDeck.Agent;

/// <summary>
/// Appends hook events as newline-delimited JSON.
///
/// The file lives outside any install directory so it survives an upgrade, and each line is
/// one event wrapped with the moment it arrived and which hook sent it.
/// </summary>
internal sealed class EventLog(string? path = null)
{
    private readonly string _path = path ?? DefaultPath();
    private readonly Lock _gate = new();

    public string Path => _path;

    public static string DefaultPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeDeck",
            "agent",
            "events.ndjson");

    public void Append(string hookEvent, string payload)
    {
        var line = new StringBuilder()
            .Append("{\"receivedAt\":\"")
            .Append(DateTimeOffset.UtcNow.ToString("O"))
            .Append("\",\"event\":")
            .Append(JsonSerializer.Serialize(hookEvent))
            .Append(",\"payload\":")
            .Append(Compact(payload))
            .Append("}")
            .ToString();

        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
    }

    /// <summary>
    /// Keeps one event on one line. A payload that will not parse is preserved as a string
    /// rather than dropped: an unreadable event is still evidence.
    /// </summary>
    private static string Compact(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch
        {
            return JsonSerializer.Serialize(payload);
        }
    }
}
