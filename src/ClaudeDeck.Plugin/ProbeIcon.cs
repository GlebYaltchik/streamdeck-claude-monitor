namespace ClaudeDeck.Plugin;

/// <summary>
/// Composes a key image as SVG. Proving this works on the device is half the point of the
/// probe: if it does, the project needs no imaging library at all.
/// </summary>
internal static class ProbeIcon
{
    private const int Size = 144;

    public static string Render(string topLine, string bottomLine)
    {
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{Size}" height="{Size}" viewBox="0 0 {Size} {Size}">
              <rect width="{Size}" height="{Size}" fill="#1b1f24"/>
              <circle cx="72" cy="60" r="34" fill="none" stroke="#4f9cf9" stroke-width="8"
                      stroke-dasharray="160 214" stroke-linecap="round" transform="rotate(-90 72 60)"/>
              <text x="72" y="66" text-anchor="middle" font-family="sans-serif" font-size="24"
                    fill="#ffffff">{Escape(topLine)}</text>
              <text x="72" y="124" text-anchor="middle" font-family="sans-serif" font-size="20"
                    fill="#9aa4b2">{Escape(bottomLine)}</text>
            </svg>
            """;
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
