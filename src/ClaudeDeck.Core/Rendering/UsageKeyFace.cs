using ClaudeDeck.Core.Usage;

namespace ClaudeDeck.Core.Rendering;

/// <summary>
/// Draws one usage window on a key: the percentage, a bar, and when the window resets.
///
/// The key is read at arm's length, so text is sized for legibility first and everything
/// that does not earn its space is dropped.
/// </summary>
public static class UsageKeyFace
{
    private const string Background = "#1b1f24";
    private const string Track = "#2b313a";
    private const string Primary = "#ffffff";
    private const string Muted = "#9aa4b2";
    private const string Warning = "#c9873a";

    public static string Render(UsageSnapshot snapshot, string group, string label, DateTimeOffset now)
    {
        var window = snapshot.Find(group);
        return window is null
            ? Unavailable(snapshot, label)
            : Window(window, label, snapshot.Stale, now);
    }

    private static string Window(UsageWindow window, string label, bool stale, DateTimeOffset now)
    {
        var colour = Colour(window);
        var footer = stale ? "stale" : Remaining(window.ResetsAt - now);

        return new KeyImage()
            .Background(Background)
            .Text(label, 28, 19, Muted)
            .Text($"{window.Percent}%", 86, 50, stale ? Muted : Primary, bold: true)
            .Bar(window.Percent / 100d, colour, Track, y: 100)
            .Text(footer, 133, 19, stale ? Warning : Muted)
            .ToDataUrl();
    }

    private static string Unavailable(UsageSnapshot snapshot, string label)
    {
        var reason = snapshot.Status switch
        {
            UsageStatus.AuthRequired => "log in",
            UsageStatus.RateLimited => "throttled",
            _ => "no data",
        };

        return new KeyImage()
            .Background(Background)
            .Text(label, 28, 19, Muted)
            .Text("--", 86, 50, "#5c6672", bold: true)
            .Bar(0, Track, Track, y: 100)
            .Text(reason, 133, 19, Warning)
            .ToDataUrl();
    }

    /// <summary>
    /// The same window on an encoder's touch strip. The strip is wide and short, so the
    /// reset time rides along with the label instead of taking its own line.
    /// </summary>
    public static UsageStripFace RenderStrip(UsageSnapshot snapshot, string group, string label, DateTimeOffset now)
    {
        var window = snapshot.Find(group);
        if (window is null)
        {
            var reason = snapshot.Status switch
            {
                UsageStatus.AuthRequired => "log in",
                UsageStatus.RateLimited => "throttled",
                _ => "no data",
            };

            return new UsageStripFace($"{label} · {reason}", "--", 0, Track);
        }

        var suffix = snapshot.Stale ? "stale" : Remaining(window.ResetsAt - now);
        var title = suffix.Length == 0 ? label : $"{label} · {suffix}";

        return new UsageStripFace(title, $"{window.Percent}%", window.Percent, Colour(window));
    }

    /// <summary>
    /// How long is left, at the coarsest useful precision. A weekly window measured in hours
    /// is a number nobody can act on, so past a day it reads in days.
    /// </summary>
    public static string Remaining(TimeSpan? remaining)
    {
        if (remaining is null)
        {
            return "";
        }

        var value = remaining.Value;
        if (value <= TimeSpan.Zero)
        {
            return "resetting";
        }

        if (value.TotalDays >= 1)
        {
            return value.Hours == 0 ? $"{value.Days}d" : $"{value.Days}d {value.Hours}h";
        }

        if (value.TotalHours >= 1)
        {
            return value.Minutes == 0 ? $"{value.Hours}h" : $"{value.Hours}h {value.Minutes}m";
        }

        return $"{Math.Max(1, value.Minutes)}m";
    }

    /// <summary>
    /// Severity comes from the server, so the key agrees with the client even when the plan's
    /// own thresholds change. Percentage is only the fallback for a response without one.
    /// </summary>
    private static string Colour(UsageWindow window) => window.Severity.ToLowerInvariant() switch
    {
        "warning" or "warn" => "#e0a03a",
        "critical" or "error" or "exceeded" => "#e05252",
        _ => ByPercent(window.Percent),
    };

    private static string ByPercent(int percent) => percent switch
    {
        >= 90 => "#e05252",
        >= 70 => "#e0a03a",
        _ => "#4f9cf9",
    };
}
