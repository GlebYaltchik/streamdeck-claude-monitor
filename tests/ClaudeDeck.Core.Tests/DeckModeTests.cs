using System.Text;
using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Core.Tests;

public class DeckModeTests
{
    /// <summary>
    /// Off, observe, active, and round again. The dangerous mode is two presses from the
    /// harmless one in each direction, so it is never reached by brushing the key once.
    /// </summary>
    [Fact]
    public void The_key_cycles_through_all_three_modes()
    {
        var modes = new DeckModes();
        Assert.Equal(DeckMode.Observe, modes.Current);

        modes.Cycle();
        Assert.Equal(DeckMode.Active, modes.Current);

        modes.Cycle();
        Assert.Equal(DeckMode.Off, modes.Current);

        modes.Cycle();
        Assert.Equal(DeckMode.Observe, modes.Current);
    }

    [Fact]
    public void Changing_the_mode_is_announced_once()
    {
        var modes = new DeckModes();
        var changes = 0;
        modes.Changed += () => changes++;

        modes.Set(DeckMode.Active);
        modes.Set(DeckMode.Active);

        Assert.Equal(1, changes);
    }

    /// <summary>
    /// A name from a newer build, or a settings file written by hand, must not turn the deck
    /// on by accident. Unreadable reads as observe, the same as never having been told.
    /// </summary>
    [Fact]
    public void An_unknown_mode_name_reads_as_observe()
    {
        Assert.Equal(DeckMode.Observe, DeckModes.Parse(null));
        Assert.Equal(DeckMode.Observe, DeckModes.Parse("whatever-comes-next"));
        Assert.Equal(DeckMode.Off, DeckModes.Parse("off"));
        Assert.Equal(DeckMode.Active, DeckModes.Parse("ACTIVE"));
    }

    [Fact]
    public void The_key_says_the_mode_and_what_it_does()
    {
        Assert.Contains(">off<", Decode(ModeKeyFace.Render(DeckMode.Off)));
        Assert.Contains(">not watching<", Decode(ModeKeyFace.Render(DeckMode.Off)));
        Assert.Contains(">active<", Decode(ModeKeyFace.Render(DeckMode.Active)));
        Assert.Contains(">answer here<", Decode(ModeKeyFace.Render(DeckMode.Active)));
    }

    private static string Decode(string dataUrl)
    {
        const string prefix = "data:image/svg+xml;base64,";
        Assert.StartsWith(prefix, dataUrl);
        return Encoding.UTF8.GetString(Convert.FromBase64String(dataUrl[prefix.Length..]));
    }
}
