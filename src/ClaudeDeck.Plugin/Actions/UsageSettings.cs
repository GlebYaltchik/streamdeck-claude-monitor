using System.Text.Json;
using ClaudeDeck.Core.Usage;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// Per-key settings from the Property Inspector.
/// </summary>
internal sealed record UsageSettings(string? CredentialsPath, string Window)
{
    public static UsageSettings Default { get; } = new(null, UsageSnapshot.SessionGroup);

    public string Label => Window == UsageSnapshot.WeeklyGroup ? "WEEK" : "5 HOUR";

    public static UsageSettings From(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("settings", out var settings) ||
            settings.ValueKind != JsonValueKind.Object)
        {
            return Default;
        }

        var window = Read(settings, "window");
        return new UsageSettings(
            CredentialsPath: Read(settings, "credentialsPath"),
            Window: window == UsageSnapshot.WeeklyGroup ? UsageSnapshot.WeeklyGroup : UsageSnapshot.SessionGroup);
    }

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } text ? text : null
            : null;
}
