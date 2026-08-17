using ClaudeDeck.Core.Rendering;
using ClaudeDeck.Core.Usage;

namespace ClaudeDeck.Core.Tests;

public class UsageKeyFaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 45, "45m")]
    [InlineData(3, 20, "3h 20m")]
    [InlineData(3, 0, "3h")]
    public void Short_waits_read_in_hours_and_minutes(int hours, int minutes, string expected)
    {
        Assert.Equal(expected, UsageKeyFace.Remaining(new TimeSpan(hours, minutes, 0)));
    }

    [Fact]
    public void Under_a_minute_rounds_up_rather_than_showing_zero()
    {
        Assert.Equal("1m", UsageKeyFace.Remaining(TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(1, 0, "1d")]
    [InlineData(2, 4, "2d 4h")]
    [InlineData(6, 23, "6d 23h")]
    public void Long_waits_read_in_days(int days, int hours, string expected)
    {
        // A weekly window measured in hours is a number nobody can act on.
        Assert.Equal(expected, UsageKeyFace.Remaining(new TimeSpan(days, hours, 0, 0)));
    }

    [Fact]
    public void A_window_that_has_already_passed_says_so()
    {
        Assert.Equal("resetting", UsageKeyFace.Remaining(TimeSpan.Zero));
        Assert.Equal("resetting", UsageKeyFace.Remaining(TimeSpan.FromMinutes(-5)));
    }

    [Fact]
    public void An_unknown_reset_time_shows_nothing_rather_than_a_guess()
    {
        Assert.Equal("", UsageKeyFace.Remaining(null));
    }

    [Fact]
    public void A_missing_window_falls_back_to_the_unavailable_face()
    {
        var snapshot = UsageSnapshot.Failure(UsageStatus.AuthRequired, "no credentials", Now);

        var svg = Decode(UsageKeyFace.Render(snapshot, UsageSnapshot.SessionGroup, "5 HOUR", Now));

        Assert.Contains("log in", svg);
        Assert.Contains("--", svg);
    }

    [Fact]
    public void A_stale_value_is_shown_but_marked()
    {
        var snapshot = Ok(24) with { Stale = true };

        var svg = Decode(UsageKeyFace.Render(snapshot, UsageSnapshot.SessionGroup, "5 HOUR", Now));

        Assert.Contains("24%", svg);
        Assert.Contains("stale", svg);
    }

    [Fact]
    public void Severity_from_the_server_drives_the_colour()
    {
        var normal = Decode(Render(Ok(95) with { Windows = [Window(95, "normal")] }));
        var warning = Decode(Render(Ok(10) with { Windows = [Window(10, "warning")] }));

        // A low percentage the server calls a warning is still a warning, and a high one it
        // calls normal is not escalated on our side beyond the percentage fallback.
        Assert.Contains("#e0a03a", warning);
        Assert.Contains("#e05252", normal);
    }

    [Fact]
    public void The_strip_carries_the_reset_time_alongside_the_label()
    {
        var strip = UsageKeyFace.RenderStrip(Ok(24), UsageSnapshot.SessionGroup, "5 HOUR", Now);

        Assert.Equal("5 HOUR · 2h", strip.Title);
        Assert.Equal("24%", strip.Value);
        Assert.Equal(24, strip.Indicator);
    }

    [Fact]
    public void The_strip_drops_the_separator_when_there_is_no_reset_time()
    {
        var snapshot = new UsageSnapshot(
            UsageStatus.Ok,
            [new UsageWindow(UsageSnapshot.SessionGroup, "session", 24, "normal", null, true)],
            Now);

        var strip = UsageKeyFace.RenderStrip(snapshot, UsageSnapshot.SessionGroup, "5 HOUR", Now);

        Assert.Equal("5 HOUR", strip.Title);
    }

    [Fact]
    public void The_strip_says_why_it_has_nothing_to_show()
    {
        var snapshot = UsageSnapshot.Failure(UsageStatus.AuthRequired, "no credentials", Now);

        var strip = UsageKeyFace.RenderStrip(snapshot, UsageSnapshot.SessionGroup, "WEEK", Now);

        Assert.Equal("WEEK · log in", strip.Title);
        Assert.Equal("--", strip.Value);
        Assert.Equal(0, strip.Indicator);
    }

    private static string Render(UsageSnapshot snapshot) =>
        UsageKeyFace.Render(snapshot, UsageSnapshot.SessionGroup, "5 HOUR", Now);

    private static UsageSnapshot Ok(int percent) =>
        new(UsageStatus.Ok, [Window(percent, "normal")], Now);

    private static UsageWindow Window(int percent, string severity) =>
        new(UsageSnapshot.SessionGroup, "session", percent, severity, Now.AddHours(2), true);

    private static string Decode(string dataUrl) =>
        System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(dataUrl["data:image/svg+xml;base64,".Length..]));
}
