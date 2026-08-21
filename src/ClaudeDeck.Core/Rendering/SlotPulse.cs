namespace ClaudeDeck.Core.Rendering;

/// <summary>
/// The shape of a slot asking to be looked at: a slow swell to full and back.
///
/// Two cheaper ways were tried on the device and neither works. An animated SVG is rasterized
/// once, and an animated GIF is shown as a still: <c>setImage</c> takes a picture, not a film
/// (findings/streamdeck.md). Movement therefore costs one message per frame, and this is what
/// each frame should look like.
///
/// The glow comes from the clock rather than from a frame counter. A counter turns every late
/// or dropped frame into a stutter that never catches up; a clock turns it into one skipped
/// value, and the swell stays on time. That unevenness is what got the first attempt rejected.
/// </summary>
public static class SlotPulse
{
    /// <summary>One breath, in and out. Slow enough to be calm, quick enough to be noticed.</summary>
    public static readonly TimeSpan Breath = TimeSpan.FromSeconds(2.5);

    /// <summary>How lit the slot is at a point in time, from nothing to full and back.</summary>
    public static double Glow(TimeSpan elapsed)
    {
        var position = elapsed.TotalSeconds / Breath.TotalSeconds;
        return (1 - Math.Cos(2 * Math.PI * (position - Math.Floor(position)))) / 2;
    }
}
