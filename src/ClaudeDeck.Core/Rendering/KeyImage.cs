using System.Globalization;
using System.Text;

namespace ClaudeDeck.Core.Rendering;

/// <summary>
/// Composes a Stream Deck key face as SVG. The device renders SVG data URLs directly, so
/// drawing a key is string building rather than rasterization.
/// </summary>
public sealed class KeyImage
{
    public const int Size = 144;

    private const int Centre = Size / 2;

    private readonly StringBuilder _elements = new();

    public KeyImage Background(string colour)
    {
        _elements.Append($"""<rect width="{Size}" height="{Size}" fill="{colour}"/>""");
        return this;
    }

    /// <summary>
    /// A progress ring drawn clockwise from the top.
    /// </summary>
    /// <param name="fraction">Filled portion, clamped to 0..1.</param>
    public KeyImage Ring(double fraction, string colour, string trackColour, int radius = 46, int width = 10)
    {
        var circumference = 2 * Math.PI * radius;
        var filled = circumference * Math.Clamp(fraction, 0, 1);

        // Numbers must be invariant. On a comma-decimal locale "72,26 289,03" is read by SVG
        // as four dash lengths rather than two, which silently draws the wrong ring.
        var dashArray = $"{Number(filled)} {Number(circumference)}";

        _elements.Append($"""
            <circle cx="{Centre}" cy="{Centre}" r="{radius}" fill="none" stroke="{trackColour}" stroke-width="{width}"/>
            """);
        _elements.Append($"""
            <circle cx="{Centre}" cy="{Centre}" r="{radius}" fill="none" stroke="{colour}" stroke-width="{width}"
                    stroke-linecap="round" stroke-dasharray="{dashArray}"
                    transform="rotate(-90 {Centre} {Centre})"/>
            """);
        return this;
    }

    public KeyImage Text(string value, int y, int fontSize, string colour, bool bold = false)
    {
        var weight = bold ? """ font-weight="bold" """ : " ";
        _elements.Append($"""
            <text x="{Centre}" y="{y}" text-anchor="middle" font-family="sans-serif"
                  font-size="{fontSize}"{weight}fill="{colour}">{Escape(value)}</text>
            """);
        return this;
    }

    public string ToSvg()
    {
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{Size}" height="{Size}" viewBox="0 0 {Size} {Size}">{_elements}</svg>
            """;
    }

    public string ToDataUrl()
    {
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(ToSvg()));
    }

    private static string Number(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
