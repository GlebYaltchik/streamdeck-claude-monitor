using ClaudeDeck.Core.Permissions;

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
}
