using System.Text;
using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Core.Tests;

public class DeckModeTests
{
    /// <summary>
    /// Two states, so the key and the settings checkbox can say the same thing. It starts on
    /// the one that cannot act.
    /// </summary>
    [Fact]
    public void The_key_switches_between_watching_and_answering()
    {
        var modes = new DeckModes();
        Assert.Equal(DeckMode.Observe, modes.Current);

        modes.Toggle();
        Assert.Equal(DeckMode.Active, modes.Current);

        modes.Toggle();
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
    /// A name from a newer build, a settings file written by hand, or "off" from a build that
    /// had three modes: none of them may turn answering on by accident.
    /// </summary>
    [Fact]
    public void An_unknown_mode_name_reads_as_observe()
    {
        Assert.Equal(DeckMode.Observe, DeckModes.Parse(null));
        Assert.Equal(DeckMode.Observe, DeckModes.Parse("whatever-comes-next"));
        Assert.Equal(DeckMode.Observe, DeckModes.Parse("off"));
        Assert.Equal(DeckMode.Active, DeckModes.Parse("ACTIVE"));
    }

    [Fact]
    public void The_key_says_the_mode()
    {
        Assert.Contains(">observe<", Decode(ModeKeyFace.Render(DeckMode.Observe)));
        Assert.Contains(">watch only<", Decode(ModeKeyFace.Render(DeckMode.Observe)));
        Assert.Contains(">active<", Decode(ModeKeyFace.Render(DeckMode.Active)));
    }

    /// <summary>
    /// Active used to say "answer here", which reads as an instruction to answer on this key.
    /// Nothing on it can: it is the switch, and the answering happens elsewhere.
    /// </summary>
    [Fact]
    public void The_key_does_not_claim_a_question_can_be_answered_on_it()
    {
        Assert.DoesNotContain("answer here", Decode(ModeKeyFace.Render(DeckMode.Active)));
    }

    private static string Decode(string dataUrl)
    {
        const string prefix = "data:image/svg+xml;base64,";
        Assert.StartsWith(prefix, dataUrl);
        return Encoding.UTF8.GetString(Convert.FromBase64String(dataUrl[prefix.Length..]));
    }
}
