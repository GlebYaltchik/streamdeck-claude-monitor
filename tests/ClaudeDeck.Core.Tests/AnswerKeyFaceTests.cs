using System.Text;
using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Core.Tests;

public class AnswerKeyFaceTests
{
    /// <summary>
    /// The roles come from where the keys sit, so a pair works with nothing configured, and
    /// swapping is one value for the pair rather than one per key — which is what makes two
    /// Allows impossible to express.
    /// </summary>
    [Fact]
    public void The_first_key_of_the_pair_allows_until_the_pair_is_swapped()
    {
        var roles = new AnswerRoles();

        Assert.Equal(AnswerRole.Allow, roles.Of(0));
        Assert.Equal(AnswerRole.Deny, roles.Of(1));

        roles.Set(true);

        Assert.Equal(AnswerRole.Deny, roles.Of(0));
        Assert.Equal(AnswerRole.Allow, roles.Of(1));
    }

    [Fact]
    public void Swapping_the_pair_is_announced_once()
    {
        var roles = new AnswerRoles();
        var changes = 0;
        roles.Changed += () => changes++;

        roles.Set(true);
        roles.Set(true);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_key_says_which_half_of_the_pair_it_is()
    {
        Assert.Contains(">allow<", Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 2, answering: true)));
        Assert.Contains(">deny<", Decode(AnswerKeyFace.Render(AnswerRole.Deny, keys: 2, answering: true)));
    }

    /// <summary>
    /// A deck is arranged by dragging keys onto it, so a key that is not half of a pair has to
    /// say what to do about it rather than wait silently for a press that will do nothing.
    /// </summary>
    [Fact]
    public void A_key_without_a_pair_says_what_is_missing()
    {
        var alone = Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 1, answering: true));
        Assert.Contains(">no pair<", alone);
        Assert.Contains(">add one more<", alone);

        var crowd = Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 3, answering: true));
        Assert.Contains(">no pair<", crowd);
        Assert.Contains(">keep only two<", crowd);
    }

    /// <summary>
    /// The mode is what says whether a press can answer anything, and the pair has to say so
    /// itself: a key that looks the same either way is one that stops being trusted.
    /// </summary>
    [Fact]
    public void The_pair_says_when_the_deck_may_not_answer()
    {
        Assert.Contains(">watch only<", Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 2, answering: false)));
        Assert.DoesNotContain("watch only", Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 2, answering: true)));
    }

    /// <summary>
    /// A key with nothing to answer must not ask to be pressed. Nothing can be addressed yet,
    /// so the pair waits in silence rather than instructing a gesture that does nothing.
    /// </summary>
    [Fact]
    public void A_pair_with_nothing_to_answer_asks_for_nothing()
    {
        Assert.DoesNotContain("tap", Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 2, answering: true)));
    }

    /// <summary>
    /// The instruction appears when there is something to follow it with, and goes again the
    /// moment the press it was asking for has happened.
    /// </summary>
    [Fact]
    public void The_pair_asks_for_a_tap_only_while_a_session_is_waiting()
    {
        Assert.Contains(
            ">tap a session<",
            Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 2, answering: true, waiting: true)));

        Assert.DoesNotContain(
            "tap a session",
            Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 2, answering: true, waiting: true, remaining: 0.5)));
    }

    /// <summary>
    /// Twenty seconds is short enough that keys going quiet with no warning would read as a
    /// fault, so what is left of it is drawn where the instruction was.
    /// </summary>
    [Fact]
    public void An_addressed_session_puts_the_time_left_on_the_pair()
    {
        var armed = Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 2, answering: true, remaining: 0.5));

        Assert.Contains("<rect", armed);
        Assert.Contains("y=\"116\"", armed);
    }

    /// <summary>
    /// Full brightness is the one thing that says the next press will answer something. It
    /// must not arrive while the deck is only watching.
    /// </summary>
    [Fact]
    public void The_pair_does_not_light_up_while_the_deck_may_not_answer()
    {
        var watching = Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 2, answering: false, remaining: 0.5));

        Assert.DoesNotContain("#4f9d69", watching);
        Assert.Contains("#4f9d69", Decode(AnswerKeyFace.Render(AnswerRole.Allow, keys: 2, answering: true, remaining: 0.5)));
    }

    /// <summary>
    /// The warning goes where the permission is given. Red cannot be the word: red is Deny,
    /// and two red words side by side say neither - so the background turns and the word keeps
    /// saying which half of the pair this is.
    /// </summary>
    [Fact]
    public void A_dangerous_request_turns_the_allow_key_and_says_so()
    {
        var armed = Decode(AnswerKeyFace.Render(
            AnswerRole.Allow, keys: 2, answering: true, remaining: 0.5, dangerous: true));

        Assert.Contains(">DANGER<", armed);
        Assert.Contains("#5c1d1d", armed);
        Assert.Contains(">allow<", armed);
        Assert.Contains("#4f9d69", armed);
    }

    /// <summary>Denying runs nothing, so it carries no warning and is never made harder.</summary>
    [Fact]
    public void The_deny_key_carries_no_warning()
    {
        var deny = Decode(AnswerKeyFace.Render(
            AnswerRole.Deny, keys: 2, answering: true, remaining: 0.5, dangerous: true));

        Assert.DoesNotContain("DANGER", deny);
        Assert.DoesNotContain("#5c1d1d", deny);
    }

    /// <summary>
    /// Nothing addressed, nothing to warn about. The classification belongs to a question, and
    /// with no question the pair has no opinion.
    /// </summary>
    [Fact]
    public void A_pair_with_nothing_addressed_shows_no_warning()
    {
        var idle = Decode(AnswerKeyFace.Render(
            AnswerRole.Allow, keys: 2, answering: true, waiting: true, dangerous: true));

        Assert.DoesNotContain("DANGER", idle);
    }

    private static string Decode(string dataUrl)
    {
        const string prefix = "data:image/svg+xml;base64,";
        Assert.StartsWith(prefix, dataUrl);
        return Encoding.UTF8.GetString(Convert.FromBase64String(dataUrl[prefix.Length..]));
    }
}
