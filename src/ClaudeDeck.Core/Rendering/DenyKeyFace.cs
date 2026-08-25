using ClaudeDeck.Core.Permissions;

namespace ClaudeDeck.Core.Rendering;

/// <summary>
/// Draws the deny key: whether there is anything to deny, and whether this deck is allowed to.
///
/// Three faces, because a key that looks the same when it will do nothing is a key people
/// press twice and then distrust. Armed only when a question is waiting and the mode is
/// active; otherwise it says which of the two is missing.
/// </summary>
public static class DenyKeyFace
{
    private const string Label = "DENY";

    private const string Armed = "#e05c5c";

    public static string Render(DeckMode mode, int waiting)
    {
        var armed = mode == DeckMode.Active && waiting > 0;

        return new KeyImage()
            .Background(KeyPalette.Background)
            .Text(Label, 34, 24, armed ? Armed : KeyPalette.Dim, bold: true)
            .Text(State(mode, waiting), 78, 17, KeyPalette.Muted)
            .Text(waiting > 1 ? "oldest of " + waiting : string.Empty, 108, 15, KeyPalette.Dim)
            .ToDataUrl();
    }

    private static string State(DeckMode mode, int waiting) => mode switch
    {
        DeckMode.Active when waiting > 0 => "ready",
        DeckMode.Active => "none waiting",
        _ => "not active",
    };
}
