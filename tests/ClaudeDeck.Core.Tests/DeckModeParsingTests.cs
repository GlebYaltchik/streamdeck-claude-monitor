using ClaudeDeck.Core.Permissions;

namespace ClaudeDeck.Core.Tests;

/// <summary>
/// What a saved mode has to survive. The plugin keeps this in its own settings file, which
/// outlives builds and can be edited by hand, so every unreadable answer has to land on the
/// harmless mode rather than on the one that lets a key run a command.
/// </summary>
public class DeckModeParsingTests
{
    [Theory]
    [InlineData("observe", DeckMode.Observe)]
    [InlineData("active", DeckMode.Active)]
    [InlineData("Active", DeckMode.Active)]
    public void A_saved_name_comes_back_as_the_mode_it_named(string saved, DeckMode expected) =>
        Assert.Equal(expected, DeckModes.Parse(saved));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("allow-everything")]
    [InlineData("off")]
    public void Anything_else_is_observe(string? saved) =>
        Assert.Equal(DeckMode.Observe, DeckModes.Parse(saved));

    [Fact]
    public void A_mode_survives_being_written_and_read_back() =>
        Assert.Equal(DeckMode.Active, DeckModes.Parse(DeckModes.Name(DeckMode.Active)));
}
