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

    private static string Decode(string dataUrl)
    {
        const string prefix = "data:image/svg+xml;base64,";
        Assert.StartsWith(prefix, dataUrl);
        return Encoding.UTF8.GetString(Convert.FromBase64String(dataUrl[prefix.Length..]));
    }
}
