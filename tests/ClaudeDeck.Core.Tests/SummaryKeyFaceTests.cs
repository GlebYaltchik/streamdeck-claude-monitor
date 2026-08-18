using System.Text;
using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Core.Tests;

public class SummaryKeyFaceTests
{
    [Fact]
    public void With_no_agent_the_count_is_unknown_rather_than_zero()
    {
        // The distinction that matters on the hardware: nothing is reporting, which is not
        // the same as a machine with nothing running.
        var svg = Decode(SummaryKeyFace.Render(agents: 0, sessions: 0));

        Assert.Contains("no agents", svg);
        Assert.Contains(">--<", svg);
    }

    [Fact]
    public void The_session_count_and_the_agent_count_are_both_shown()
    {
        var svg = Decode(SummaryKeyFace.Render(agents: 2, sessions: 3));

        Assert.Contains(">3<", svg);
        Assert.Contains("2 agents", svg);
    }

    [Fact]
    public void A_single_agent_is_not_reported_in_the_plural()
    {
        var svg = Decode(SummaryKeyFace.Render(agents: 1, sessions: 1));

        Assert.Contains("1 agent<", svg);
    }

    [Fact]
    public void A_connected_agent_with_nothing_running_shows_zero()
    {
        var svg = Decode(SummaryKeyFace.Render(agents: 1, sessions: 0));

        Assert.Contains(">0<", svg);
        Assert.DoesNotContain("no agents", svg);
    }

    private static string Decode(string dataUrl)
    {
        const string prefix = "data:image/svg+xml;base64,";
        Assert.StartsWith(prefix, dataUrl);
        return Encoding.UTF8.GetString(Convert.FromBase64String(dataUrl[prefix.Length..]));
    }
}
