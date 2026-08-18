namespace ClaudeDeck.Core.Rendering;

/// <summary>
/// Draws the summary key: how many sessions are live, and how many agents report them.
///
/// With no agent connected the count is unknown rather than zero, and the key says so. A
/// plain "0" would read as a working deck watching a quiet machine, which is the one thing
/// this key must never claim while it is blind.
/// </summary>
public static class SummaryKeyFace
{
    private const string Label = "SESSIONS";

    public static string Render(int agents, int sessions) =>
        agents <= 0 ? Offline() : Connected(agents, sessions);

    private static string Connected(int agents, int sessions) =>
        new KeyImage()
            .Background(KeyPalette.Background)
            .Text(Label, 28, 19, KeyPalette.Muted)
            .Text($"{sessions}", 92, 54, sessions == 0 ? KeyPalette.Muted : KeyPalette.Primary, bold: true)
            .Text(Agents(agents), 133, 19, KeyPalette.Muted)
            .ToDataUrl();

    private static string Offline() =>
        new KeyImage()
            .Background(KeyPalette.Background)
            .Text(Label, 28, 19, KeyPalette.Muted)
            .Text("--", 92, 54, KeyPalette.Dim, bold: true)
            .Text("no agents", 133, 19, KeyPalette.Warning)
            .ToDataUrl();

    private static string Agents(int agents) => agents == 1 ? "1 agent" : $"{agents} agents";
}
