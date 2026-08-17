using System.Text.Json;
using ClaudeDeck.Core.Usage;

namespace ClaudeDeck.Core.Tests;

public class UsageNormalizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The real response shape, with synthetic numbers. Includes the codenamed windows that
    /// the live endpoint returns, because ignoring them is a requirement.
    /// </summary>
    private const string FullResponse = """
        {
          "five_hour": { "utilization": 24.0, "resets_at": "2026-01-01T13:59:59+00:00" },
          "seven_day": { "utilization": 23.0, "resets_at": "2026-01-03T00:59:59+00:00" },
          "seven_day_opus": null,
          "nimbus_quill": { "utilization": 0.0, "resets_at": null },
          "tangelo": null,
          "limits": [
            { "kind": "session", "group": "session", "percent": 24, "severity": "normal",
              "resets_at": "2026-01-01T13:59:59+00:00", "is_active": true },
            { "kind": "weekly_all", "group": "weekly", "percent": 23, "severity": "warning",
              "resets_at": "2026-01-03T00:59:59+00:00", "is_active": false }
          ]
        }
        """;

    [Fact]
    public void Limits_are_preferred_over_the_named_windows()
    {
        var snapshot = Normalize(FullResponse);

        Assert.Equal(UsageStatus.Ok, snapshot.Status);
        Assert.Equal(2, snapshot.Windows.Count);

        var session = snapshot.Session;
        Assert.NotNull(session);
        Assert.Equal(24, session.Percent);
        Assert.Equal("session", session.Kind);
        Assert.True(session.IsActive);

        // Severity comes from the server rather than thresholds of our own.
        Assert.Equal("warning", snapshot.Weekly!.Severity);
    }

    [Fact]
    public void Codenamed_windows_never_reach_a_key()
    {
        var snapshot = Normalize(FullResponse);

        Assert.All(snapshot.Windows, window =>
            Assert.Contains(window.Group, new[] { UsageSnapshot.SessionGroup, UsageSnapshot.WeeklyGroup }));
    }

    [Fact]
    public void Named_windows_are_used_when_limits_is_absent()
    {
        var snapshot = Normalize("""
            {
              "five_hour": { "utilization": 42.0, "resets_at": "2026-01-01T13:59:59+00:00" },
              "seven_day": { "utilization": 7.5, "resets_at": null }
            }
            """);

        Assert.Equal(UsageStatus.Ok, snapshot.Status);
        Assert.Equal(42, snapshot.Session!.Percent);
        Assert.Equal(8, snapshot.Weekly!.Percent);
        Assert.Null(snapshot.Weekly.ResetsAt);
        Assert.Equal("normal", snapshot.Weekly.Severity);
    }

    [Fact]
    public void A_response_with_nothing_usable_is_unavailable()
    {
        var snapshot = Normalize("""{ "seven_day_opus": null, "tangelo": null }""");

        Assert.Equal(UsageStatus.Unavailable, snapshot.Status);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public void Entries_without_a_percentage_are_skipped()
    {
        var snapshot = Normalize("""
            {
              "limits": [
                { "kind": "session", "group": "session", "severity": "normal" },
                { "kind": "weekly_all", "group": "weekly", "percent": 10 }
              ]
            }
            """);

        Assert.Single(snapshot.Windows);
        Assert.Equal(UsageSnapshot.WeeklyGroup, snapshot.Windows[0].Group);
    }

    [Theory]
    [InlineData("-5", 0)]
    [InlineData("0", 0)]
    [InlineData("99.6", 100)]
    [InlineData("140", 100)]
    public void Percentages_are_clamped(string raw, int expected)
    {
        // The value is written as JSON text rather than interpolated, so the test cannot be
        // rewritten by the machine's decimal separator.
        var snapshot = Normalize($$"""
            { "limits": [ { "kind": "session", "group": "session", "percent": {{raw}} } ] }
            """);

        Assert.Equal(expected, snapshot.Session!.Percent);
    }

    private static UsageSnapshot Normalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        return UsageNormalizer.Normalize(document.RootElement, Now);
    }
}
