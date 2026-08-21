using System.Text;
using ClaudeDeck.Core.Rendering;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Core.Tests;

public class AlertKeyFaceTests
{
    [Fact]
    public void The_key_says_whether_it_is_muted()
    {
        Assert.Contains(">on<", Decode(AlertKeyFace.Render(muted: false, waiting: 0)));
        Assert.Contains(">muted<", Decode(AlertKeyFace.Render(muted: true, waiting: 0)));
    }

    /// <summary>
    /// Muting hides the flashing, which is the only other thing that says anything is
    /// waiting. A mute that leaves no way to tell how much it is suppressing is one people
    /// stop trusting.
    /// </summary>
    [Fact]
    public void A_muted_key_still_says_how_many_are_waiting()
    {
        Assert.Contains(">3 waiting<", Decode(AlertKeyFace.Render(muted: true, waiting: 3)));
    }

    [Fact]
    public void One_waiting_session_is_not_called_one_sessions()
    {
        Assert.Contains(">1 waiting<", Decode(AlertKeyFace.Render(muted: false, waiting: 1)));
        Assert.Contains(">none waiting<", Decode(AlertKeyFace.Render(muted: false, waiting: 0)));
    }

    private static string Decode(string dataUrl)
    {
        const string prefix = "data:image/svg+xml;base64,";
        Assert.StartsWith(prefix, dataUrl);
        return Encoding.UTF8.GetString(Convert.FromBase64String(dataUrl[prefix.Length..]));
    }
}
