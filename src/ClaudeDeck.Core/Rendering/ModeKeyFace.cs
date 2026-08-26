using ClaudeDeck.Core.Permissions;

namespace ClaudeDeck.Core.Rendering;

/// <summary>
/// Draws the mode key: how far the deck is allowed into permission decisions.
///
/// The mode is the word, and the colour is how much power it grants — blue for watching,
/// amber for able to answer. Amber rather than green: this is the setting that lets a key
/// press run a command, and it should look like something switched on rather than something
/// that is fine.
///
/// Only observe carries a caption. Active had one reading "answer here", which was read on
/// the device as an instruction to answer on this key — and nothing on it can. The word alone
/// says what the mode is; what may answer is the pair and the session keys.
/// </summary>
public static class ModeKeyFace
{
    private const string Label = "APPROVALS";

    private const string ObserveColour = "#5b93d6";

    private const string ActiveColour = "#e0a03a";

    public static string Render(DeckMode mode)
    {
        var image = new KeyImage()
            .Background(KeyPalette.Background)
            .Text(Label, 30, 19, KeyPalette.Muted)
            .Text(DeckModes.Name(mode), 82, 30, Colour(mode), bold: true);

        if (mode == DeckMode.Observe)
        {
            image.Text("watch only", 124, 17, KeyPalette.Dim);
        }

        return image.ToDataUrl();
    }

    private static string Colour(DeckMode mode) =>
        mode == DeckMode.Active ? ActiveColour : ObserveColour;
}
