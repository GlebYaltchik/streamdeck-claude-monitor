using System.Text.Json;

namespace ClaudeDeck.Core.Sessions;

/// <summary>
/// The fields of a Claude Code hook payload that the registry acts on.
///
/// Notably absent: the model and the git branch. No payload carries either, so they come
/// from the transcript instead.
/// </summary>
public sealed record HookEvent(
    string Name,
    string SessionId,
    DateTimeOffset ReceivedAt,
    string? Cwd = null,
    string? TranscriptPath = null,
    string? PermissionMode = null,
    string? ToolName = null,
    string? Source = null,
    string? Reason = null)
{
    /// <summary>Marks a compaction continuing an existing session rather than a new one.</summary>
    public const string CompactSource = "compact";

    public static HookEvent? Parse(string name, JsonElement payload, DateTimeOffset receivedAt)
    {
        if (payload.ValueKind != JsonValueKind.Object || Read(payload, "session_id") is not { } sessionId)
        {
            return null;
        }

        return new HookEvent(
            Name: name,
            SessionId: sessionId,
            ReceivedAt: receivedAt,
            Cwd: Read(payload, "cwd"),
            TranscriptPath: Read(payload, "transcript_path"),
            PermissionMode: Read(payload, "permission_mode"),
            ToolName: Read(payload, "tool_name"),
            Source: Read(payload, "source"),
            Reason: Read(payload, "reason"));
    }

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
