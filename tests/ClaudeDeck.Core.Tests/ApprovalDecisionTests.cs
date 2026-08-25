using System.Text;
using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Core.Tests;

public class ApprovalDecisionTests
{
    /// <summary>
    /// Byte for byte the shape that was measured to work on the device. It is asserted whole
    /// rather than by parts because getting it wrong is silent: an unknown or misspelled
    /// field fails schema validation for the entire answer, and the client then behaves as if
    /// no decision was given at all. That cost four rounds of measurement to find.
    /// </summary>
    [Fact]
    public void A_denial_prints_the_shape_the_client_accepts()
    {
        var expected = """
            {"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Denied on the Stream Deck. Stop and wait for the user."}}}
            """.Trim();

        Assert.Equal(expected, ApprovalDecision.Denied().ToHookOutput());
    }

    /// <summary>Allowing carries no message: it is a deny-only field, and unknown fields fail.</summary>
    [Fact]
    public void Allowing_says_nothing_more_than_allow()
    {
        var expected = """
            {"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}
            """.Trim();

        Assert.Equal(expected, new ApprovalDecision(ApprovalDecision.Allow, "ignored").ToHookOutput());
    }

    /// <summary>
    /// A key that looks the same when it will do nothing is pressed twice and then distrusted.
    /// </summary>
    [Fact]
    public void The_deny_key_says_why_it_cannot_do_anything()
    {
        Assert.Contains(">not active<", Decode(DenyKeyFace.Render(DeckMode.Observe, waiting: 2)));
        Assert.Contains(">none waiting<", Decode(DenyKeyFace.Render(DeckMode.Active, waiting: 0)));
        Assert.Contains(">ready<", Decode(DenyKeyFace.Render(DeckMode.Active, waiting: 1)));
        Assert.Contains(">oldest of 3<", Decode(DenyKeyFace.Render(DeckMode.Active, waiting: 3)));
    }

    private static string Decode(string dataUrl)
    {
        const string prefix = "data:image/svg+xml;base64,";
        Assert.StartsWith(prefix, dataUrl);
        return Encoding.UTF8.GetString(Convert.FromBase64String(dataUrl[prefix.Length..]));
    }
}
