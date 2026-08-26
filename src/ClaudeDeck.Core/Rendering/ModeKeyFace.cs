using ClaudeDeck.Core.Permissions;

namespace ClaudeDeck.Core.Rendering;

/// <summary>
/// Draws the mode key: how far the deck is allowed into permission decisions.
///
/// The mode is the word, and the colour is how much power it grants — blue for watching,
/// amber for able to answer. Amber rather than green: this is the setting that lets a key
/// press run a command, and it should look like something switched on rather than something
/// that is fine.
/// </summary>
public static class ModeKeyFace
{
    private const string Label = "APPROVALS";

    private const string ObserveColour = "#5b93d6";

    private const string ActiveColour = "#e0a03a";

    public static string Render(DeckMode mode) =>
        new KeyImage()
            .Background(KeyPalette.Background)
            .Text(Label, 30, 19, KeyPalette.Muted)
            .Text(DeckModes.Name(mode), 82, 30, Colour(mode), bold: true)
            .Text(Explanation(mode), 124, 17, KeyPalette.Dim)
            .ToDataUrl();

    private static string Colour(DeckMode mode) =>
        mode == DeckMode.Active ? ActiveColour : ObserveColour;

    /// <summary>
    /// What the mode actually does, in the words the design uses for it. The key is read by
    /// someone deciding whether to change it, and "observe" alone does not say enough.
    /// </summary>
    private static string Explanation(DeckMode mode) =>
        mode == DeckMode.Active ? "answer here" : "watch only";
}
