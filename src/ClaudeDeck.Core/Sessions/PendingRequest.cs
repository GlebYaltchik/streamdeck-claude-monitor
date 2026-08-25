using System.Text.Json;

namespace ClaudeDeck.Core.Sessions;

/// <summary>
/// What a session is being asked to be allowed to do: the tool, and the one line that says
/// what it was asked for.
/// </summary>
public sealed record PendingRequest(string Tool, string? Summary);

/// <summary>
/// Pulls a readable line out of a tool's input.
///
/// Every tool has its own shape and there is no list of them worth keeping in step with, so
/// this takes the first field it recognises. The order matters: <c>command</c> before
/// <c>description</c>, because the command is what the permission is actually about and the
/// description is the model's account of it.
///
/// Nothing is a fair answer. A tool whose input says nothing recognisable still has a name,
/// and the name alone is what the key shows.
/// </summary>
public static class ToolInputs
{
    private static readonly string[] Fields =
        ["command", "file_path", "path", "url", "pattern", "description"];

    public static string? Summarise(JsonElement toolInput)
    {
        if (toolInput.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var field in Fields)
        {
            if (toolInput.TryGetProperty(field, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                value.GetString() is { Length: > 0 } text)
            {
                return Flatten(text);
            }
        }

        return null;
    }

    /// <summary>
    /// One line, however many the command had. A key and a touch strip both draw a line at a
    /// time, and a heredoc would otherwise take the face over.
    /// </summary>
    private static string Flatten(string text)
    {
        var collapsed = string.Join(' ', text.Split(
            ['\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return collapsed.Length == 0 ? text.Trim() : collapsed;
    }
}
