using System.Globalization;
using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Core.Tests;

public class KeyImageTests
{
    /// <summary>
    /// Regression: the ring used plain interpolation for its dash lengths. On a comma-decimal
    /// locale that produced "72,26 289,03", which SVG reads as four dash values instead of
    /// two — a wrong ring, drawn without any error.
    /// </summary>
    [Theory]
    [InlineData("ru-RU")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("")]
    public void The_ring_is_drawn_with_invariant_numbers(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            var svg = new KeyImage().Ring(0.25, "#4f9cf9", "#2b313a").ToSvg();
            var dashArray = Between(svg, "stroke-dasharray=\"", "\"");

            Assert.DoesNotContain(",", dashArray);
            Assert.Equal(2, dashArray.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Text_is_escaped_so_a_project_name_cannot_break_the_document()
    {
        var svg = new KeyImage().Text("a<b&c>d", 60, 16, "#ffffff").ToSvg();

        Assert.Contains("a&lt;b&amp;c&gt;d", svg);
    }

    [Fact]
    public void The_data_url_carries_the_document()
    {
        var image = new KeyImage().Background("#1b1f24");

        var dataUrl = image.ToDataUrl();

        Assert.StartsWith("data:image/svg+xml;base64,", dataUrl);
        var decoded = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(dataUrl["data:image/svg+xml;base64,".Length..]));
        Assert.Equal(image.ToSvg(), decoded);
    }

    private static string Between(string value, string start, string end)
    {
        var from = value.IndexOf(start, StringComparison.Ordinal) + start.Length;
        var to = value.IndexOf(end, from, StringComparison.Ordinal);
        return value[from..to];
    }
}
