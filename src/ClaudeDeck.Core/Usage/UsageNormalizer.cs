using System.Text.Json;

namespace ClaudeDeck.Core.Usage;

/// <summary>
/// Turns the raw usage response into a snapshot.
///
/// The endpoint is unofficial, so every field is treated as optional and anything
/// unrecognised is ignored rather than displayed. The response carries several codenamed
/// windows that appear to be internal experiments; they must never reach a key.
/// </summary>
public static class UsageNormalizer
{
    private static readonly string[] KnownGroups = [UsageSnapshot.SessionGroup, UsageSnapshot.WeeklyGroup];

    public static UsageSnapshot Normalize(JsonElement root, DateTimeOffset retrievedAt)
    {
        var windows = FromLimits(root).ToList();
        if (windows.Count == 0)
        {
            windows = FromNamedWindows(root).ToList();
        }

        return windows.Count == 0
            ? UsageSnapshot.Failure(UsageStatus.Unavailable, "No usage windows in the response.", retrievedAt)
            : new UsageSnapshot(UsageStatus.Ok, windows, retrievedAt);
    }

    /// <summary>
    /// The preferred source. It supplies the server's own `severity` and says which window is
    /// currently binding, so neither has to be invented on our side.
    /// </summary>
    private static IEnumerable<UsageWindow> FromLimits(JsonElement root)
    {
        if (!root.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var limit in limits.EnumerateArray())
        {
            if (limit.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var group = ReadString(limit, "group");
            if (group is null || !KnownGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var percent = ReadNumber(limit, "percent");
            if (percent is null)
            {
                continue;
            }

            yield return new UsageWindow(
                Group: group,
                Kind: ReadString(limit, "kind") ?? group,
                Percent: Clamp(percent.Value),
                Severity: ReadString(limit, "severity") ?? "normal",
                ResetsAt: ReadTimestamp(limit, "resets_at"),
                IsActive: limit.TryGetProperty("is_active", out var active) && active.ValueKind == JsonValueKind.True);
        }
    }

    /// <summary>
    /// Fallback for a response without <c>limits</c>. Carries no severity, so callers get
    /// "normal" and colour by percentage instead.
    /// </summary>
    private static IEnumerable<UsageWindow> FromNamedWindows(JsonElement root)
    {
        foreach (var (property, group) in new[]
                 {
                     ("five_hour", UsageSnapshot.SessionGroup),
                     ("seven_day", UsageSnapshot.WeeklyGroup),
                 })
        {
            if (!root.TryGetProperty(property, out var window) || window.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var utilization = ReadNumber(window, "utilization");
            if (utilization is null)
            {
                continue;
            }

            yield return new UsageWindow(
                Group: group,
                Kind: property,
                Percent: Clamp(utilization.Value),
                Severity: "normal",
                ResetsAt: ReadTimestamp(window, "resets_at"),
                IsActive: false);
        }
    }

    private static int Clamp(double value) => (int)Math.Round(Math.Clamp(value, 0, 100));

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ReadNumber(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string name) =>
        ReadString(element, name) is { } text && DateTimeOffset.TryParse(text, out var parsed)
            ? parsed
            : null;
}
