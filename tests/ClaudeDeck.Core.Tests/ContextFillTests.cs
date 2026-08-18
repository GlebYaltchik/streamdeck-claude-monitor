using ClaudeDeck.Core.Transcripts;

namespace ClaudeDeck.Core.Tests;

public class ContextFillTests
{
    [Fact]
    public void The_percentage_is_the_reading_over_the_window()
    {
        var fill = ContextFill.Of(new TranscriptReading(250_000, "claude-opus-5", "main"));

        Assert.Equal(1_000_000, fill.Window);
        Assert.Equal(25, fill.Percent);
        Assert.False(fill.Estimated);
    }

    [Fact]
    public void An_unknown_model_carries_the_doubt_into_the_fill()
    {
        var fill = ContextFill.Of(new TranscriptReading(50_000, "claude-not-shipped", null));

        Assert.Equal(200_000, fill.Window);
        Assert.Equal(25, fill.Percent);
        Assert.True(fill.Estimated);
    }

    /// <summary>
    /// Past the window means the window is wrong — a model that fell back to 200k while
    /// actually holding a million. Clamping here would hide the only number that says so.
    /// </summary>
    [Fact]
    public void A_reading_beyond_the_window_is_reported_as_it_is()
    {
        var fill = ContextFill.Of(new TranscriptReading(638_450, "claude-not-shipped", null));

        Assert.Equal(319, fill.Percent);
        Assert.True(fill.Estimated);
    }

    [Fact]
    public void The_measured_session_reads_as_two_thirds_full()
    {
        // The real reading from this machine's largest transcript.
        var fill = ContextFill.Of(new TranscriptReading(638_450, "claude-opus-5", "main"));

        Assert.Equal(64, fill.Percent);
    }
}
