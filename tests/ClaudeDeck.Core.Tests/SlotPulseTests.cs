using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Core.Tests;

public class SlotPulseTests
{
    [Fact]
    public void A_breath_starts_and_ends_at_rest_with_its_peak_in_the_middle()
    {
        Assert.Equal(0, SlotPulse.Glow(TimeSpan.Zero), 3);
        Assert.Equal(1, SlotPulse.Glow(SlotPulse.Breath / 2), 3);
        Assert.Equal(0, SlotPulse.Glow(SlotPulse.Breath), 3);
    }

    /// <summary>
    /// The glow comes from the clock, not from a count of frames. A late or dropped frame
    /// then costs one skipped value instead of stretching the breath — the unevenness that
    /// got the counted version rejected on the device.
    /// </summary>
    [Fact]
    public void The_same_moment_of_any_breath_looks_the_same()
    {
        var early = SlotPulse.Glow(TimeSpan.FromSeconds(0.4));
        var late = SlotPulse.Glow(TimeSpan.FromSeconds(0.4) + (SlotPulse.Breath * 40));

        Assert.Equal(early, late, 6);
    }

    [Fact]
    public void The_swell_rises_without_pausing()
    {
        var rising = Enumerable.Range(0, 13)
            .Select(step => SlotPulse.Glow(SlotPulse.Breath * (step / 24d)))
            .ToList();

        Assert.Equal(rising.OrderBy(glow => glow), rising);
        Assert.Equal(rising.Count, rising.Distinct().Count());
    }

    [Fact]
    public void The_swell_stays_within_its_bounds_however_long_it_runs()
    {
        foreach (var step in Enumerable.Range(0, 200))
        {
            Assert.InRange(SlotPulse.Glow(TimeSpan.FromMilliseconds(step * 80)), 0, 1);
        }
    }
}
