using ClaudeDeck.Core.Transcripts;

namespace ClaudeDeck.Core.Tests;

public class ContextWindowTests
{
    [Fact]
    public void The_suffix_names_the_window_outright()
    {
        // Design §4.2: `claude-opus-5[1m]` appears in real tool responses.
        var window = ContextWindows.For("claude-opus-5[1m]");

        Assert.Equal(1_000_000, window.Tokens);
        Assert.False(window.Estimated);
    }

    [Fact]
    public void A_suffix_makes_a_window_known_even_for_a_model_that_is_not()
    {
        var window = ContextWindows.For("claude-something-new[1m]");

        Assert.Equal(1_000_000, window.Tokens);
        Assert.False(window.Estimated);
    }

    /// <summary>
    /// Measured on this machine: one request on a plain <c>claude-opus-5</c> read 638,450
    /// tokens of context. The suffix is not what makes it a million-token model, and the
    /// 200k assumption design §4.2 started from would have shown a key three times past full.
    /// </summary>
    [Theory]
    [InlineData("claude-opus-5")]
    [InlineData("claude-opus-4-8")]
    [InlineData("claude-sonnet-5")]
    public void A_current_model_holds_a_million_tokens_without_any_suffix(string model)
    {
        var window = ContextWindows.For(model);

        Assert.Equal(1_000_000, window.Tokens);
        Assert.False(window.Estimated);
    }

    [Fact]
    public void The_small_window_models_are_not_widened()
    {
        var window = ContextWindows.For("claude-haiku-4-5");

        Assert.Equal(200_000, window.Tokens);
        Assert.False(window.Estimated);
    }

    [Fact]
    public void A_dated_snapshot_is_read_as_its_own_family()
    {
        // Not seen in any transcript here, but older releases wrote dated identifiers.
        var window = ContextWindows.For("claude-haiku-4-5-20251001");

        Assert.Equal(200_000, window.Tokens);
        Assert.False(window.Estimated);
    }

    [Fact]
    public void An_unknown_model_is_flagged_rather_than_reported_as_fact()
    {
        var window = ContextWindows.For("claude-something-nobody-shipped-yet");

        Assert.Equal(200_000, window.Tokens);
        Assert.True(window.Estimated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_model_is_an_estimate(string? model)
    {
        Assert.True(ContextWindows.For(model).Estimated);
    }
}
