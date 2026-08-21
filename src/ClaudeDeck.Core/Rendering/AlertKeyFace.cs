namespace ClaudeDeck.Core.Rendering;

/// <summary>
/// Draws the alert mute key: whether slots are allowed to flash, and how many are waiting.
///
/// The count is on the key because muting hides the thing that would otherwise say it. A
/// mute that leaves no way to tell how much is being suppressed is a mute people stop
/// trusting, and then stop using.
/// </summary>
public static class AlertKeyFace
{
    private const string Label = "ALERTS";

    private const string On = "#4f9d69";

    private const string Off = "#c9873a";

    public static string Render(bool muted, int waiting) =>
        new KeyImage()
            .Background(KeyPalette.Background)
            .Text(Label, 30, 19, KeyPalette.Muted)
            .Text(muted ? "muted" : "on", 82, 30, muted ? Off : On, bold: true)
            .Text(Waiting(waiting), 124, 19, waiting == 0 ? KeyPalette.Dim : KeyPalette.Primary)
            .ToDataUrl();

    private static string Waiting(int waiting) => waiting switch
    {
        0 => "none waiting",
        1 => "1 waiting",
        _ => $"{waiting} waiting",
    };
}
