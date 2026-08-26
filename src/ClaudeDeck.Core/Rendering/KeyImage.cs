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

    /// <summary>
    /// A horizontal progress bar. Cheaper on vertical space than a ring, which matters at
    /// 144 pixels once the text is large enough to read at arm's length.
    /// </summary>
    /// <param name="fraction">Filled portion, clamped to 0..1.</param>
    public KeyImage Bar(double fraction, string colour, string trackColour, int y, int height = 10, int margin = 14)
    {
        var full = Size - 2 * margin;
        var filled = (int)Math.Round(full * Math.Clamp(fraction, 0, 1));
        var radius = height / 2;

        _elements.Append($"""
            <rect x="{margin}" y="{y}" width="{full}" height="{height}" rx="{radius}" fill="{trackColour}"/>
            """);

        if (filled > 0)
        {
            _elements.Append($"""
                <rect x="{margin}" y="{y}" width="{filled}" height="{height}" rx="{radius}" fill="{colour}"/>
                """);
        }

        return this;
    }

    /// <summary>An outline just inside the key's edge, drawn over whatever is already there.</summary>
    public KeyImage Frame(string colour, int width)
    {
        var inset = width / 2;

        _elements.Append($"""
            <rect x="{inset}" y="{inset}" width="{Size - width}" height="{Size - width}"
                  fill="none" stroke="{colour}" stroke-width="{width}"/>
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
