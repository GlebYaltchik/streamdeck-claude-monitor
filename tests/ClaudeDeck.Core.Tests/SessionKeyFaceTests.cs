using System.Text;
using ClaudeDeck.Core.Rendering;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Core.Tests;

public class SessionKeyFaceTests
{
    /// <summary>
    /// The failure this face exists to fix: three sessions from one repository drew the same
    /// project and the same branch, and could not be told apart on the deck.
    /// </summary>
    [Fact]
    public void Sessions_in_one_repository_are_told_apart_by_their_names()
    {
        var handoff = Decode(SessionKeyFace.Render(
            new SessionSlotFace(SessionState.Working, "Claudedeck handoff", "streamdeck-claude-monitor", 74, false)));
        var status = Decode(SessionKeyFace.Render(
            new SessionSlotFace(SessionState.Idle, "Git status", "streamdeck-claude-monitor", 61, false)));

        Assert.Contains("Claudedeck", handoff);
        Assert.Contains("Git status", status);
        Assert.NotEqual(handoff, status);
    }

    [Fact]
    public void A_session_with_no_name_falls_back_to_its_project()
    {
        var svg = Decode(SessionKeyFace.Render(
            new SessionSlotFace(SessionState.Idle, null, "claudedeck", 10, false)));

        Assert.Contains(">claudedeck<", svg);
    }

    [Fact]
    public void A_long_name_wraps_onto_a_second_line_and_is_cut_after_that()
    {
        var svg = Decode(SessionKeyFace.Render(
            new SessionSlotFace(SessionState.Working, "Stream Deck plugin for monitoring", null, 5, false)));

        Assert.Contains(">Stream Deck<", svg);
        Assert.Contains("…", svg);
    }

    /// <summary>
    /// Measured reason: a compaction leaves the size unknown until the session's next turn.
    /// A bar that looks empty would say the context is free, which is the opposite of what
    /// has just happened.
    /// </summary>
    [Fact]
    public void An_unknown_context_leaves_the_bar_empty_and_says_so()
    {
        var svg = Decode(SessionKeyFace.Render(
            new SessionSlotFace(SessionState.Compacting, "Claudedeck", null, null, false)));

        Assert.Contains(">–<", svg);
        Assert.DoesNotContain("%", svg);
    }

    [Fact]
    public void A_guessed_window_is_marked_so_the_number_is_not_mistaken_for_a_measured_one()
    {
        var svg = Decode(SessionKeyFace.Render(
            new SessionSlotFace(SessionState.Working, "Probe", null, 42, true)));

        Assert.Contains(">42%?<", svg);
    }

    [Fact]
    public void The_bar_fills_with_the_context()
    {
        var empty = Decode(SessionKeyFace.Render(new SessionSlotFace(SessionState.Idle, "A", null, 0, false)));
        var half = Decode(SessionKeyFace.Render(new SessionSlotFace(SessionState.Idle, "A", null, 50, false)));
        var full = Decode(SessionKeyFace.Render(new SessionSlotFace(SessionState.Idle, "A", null, 100, false)));

        // The track is always drawn; the filled rectangle only when there is something to fill.
        Assert.Equal(1, Rectangles(empty));
        Assert.Equal(2, Rectangles(half));
        Assert.Equal(2, Rectangles(full));
        Assert.NotEqual(half, full);
    }

    [Theory]
    [InlineData(SessionState.Working)]
    [InlineData(SessionState.Idle)]
    [InlineData(SessionState.WaitingApproval)]
    [InlineData(SessionState.Compacting)]
    [InlineData(SessionState.Stale)]
    public void Every_state_draws_a_face(SessionState state)
    {
        Assert.Contains("<svg", Decode(SessionKeyFace.Render(new SessionSlotFace(state, "A", null, 10, false))));
    }

    /// <summary>
    /// The state used to colour the bar, which left a session at one percent — or one whose
    /// context is unknown — with almost no coloured pixels to read it from.
    /// </summary>
    [Fact]
    public void The_states_are_told_apart_by_the_background_even_with_an_empty_bar()
    {
        var faces = new[]
        {
            SessionState.Working,
            SessionState.Idle,
            SessionState.WaitingApproval,
            SessionState.Compacting,
            SessionState.Stale,
        }.Select(state => Decode(SessionKeyFace.Render(new SessionSlotFace(state, "A", null, null, false))));

        Assert.Equal(5, faces.Distinct().Count());
    }

    [Fact]
    public void The_bar_reddens_as_the_context_fills()
    {
        var roomy = Decode(SessionKeyFace.Render(new SessionSlotFace(SessionState.Idle, "A", null, 20, false)));
        var filling = Decode(SessionKeyFace.Render(new SessionSlotFace(SessionState.Idle, "A", null, 70, false)));
        var full = Decode(SessionKeyFace.Render(new SessionSlotFace(SessionState.Idle, "A", null, 90, false)));

        Assert.Equal(3, new[] { Bar(roomy), Bar(filling), Bar(full) }.Distinct().Count());
    }

    /// <summary>The same fill means the same colour whatever the session is doing.</summary>
    [Fact]
    public void The_bar_says_nothing_about_the_state()
    {
        var working = Decode(SessionKeyFace.Render(new SessionSlotFace(SessionState.Working, "A", null, 50, false)));
        var waiting = Decode(SessionKeyFace.Render(new SessionSlotFace(SessionState.WaitingApproval, "A", null, 50, false)));

        Assert.Equal(Bar(working), Bar(waiting));
    }

    /// <summary>
    /// The swell has to be visible at the edge of vision, and nothing but the background may
    /// move: a slot that rearranges itself while swelling cannot be read at a glance.
    /// </summary>
    [Fact]
    public void A_slot_asking_for_attention_only_changes_its_background()
    {
        var session = new SessionSlotFace(SessionState.Idle, "Claudedeck", null, 40, false);

        var resting = Decode(SessionKeyFace.Render(session));
        var swollen = Decode(SessionKeyFace.Render(session, 1));

        Assert.NotEqual(resting, swollen);
        Assert.Contains(">Claudedeck<", swollen);
        Assert.Contains(">40%<", swollen);
        Assert.Equal(WithoutBackground(resting), WithoutBackground(swollen));
    }

    /// <summary>
    /// A slot at rest must be the face it always was, byte for byte: an unchanged face is
    /// never resent, and every frame that goes out costs a message.
    /// </summary>
    [Fact]
    public void A_slot_at_rest_is_the_face_it_always_was()
    {
        var session = new SessionSlotFace(SessionState.Working, "Claudedeck", null, 40, false);

        Assert.Equal(SessionKeyFace.Render(session), SessionKeyFace.Render(session, 0));
    }

    [Fact]
    public void The_swell_passes_through_the_shades_between()
    {
        var session = new SessionSlotFace(SessionState.Idle, "Claudedeck", null, 40, false);

        var shades = new[] { 0, 0.25, 0.5, 0.75, 1 }
            .Select(glow => Decode(SessionKeyFace.Render(session, glow)))
            .ToList();

        Assert.Equal(5, shades.Distinct().Count());
    }

    /// <summary>The face with its background rectangle, the first one drawn, taken out.</summary>
    private static string WithoutBackground(string svg)
    {
        var start = svg.IndexOf("<rect", StringComparison.Ordinal);
        var end = svg.IndexOf("/>", start, StringComparison.Ordinal) + 2;

        return svg.Remove(start, end - start);
    }

    [Fact]
    public void An_empty_slot_carries_no_name_and_no_bar()
    {
        var svg = Decode(SessionKeyFace.Empty());

        Assert.DoesNotContain("<rect x=", svg);
        Assert.DoesNotContain("%", svg);
    }

    /// <summary>The filled part of the bar, which is the last rectangle drawn.</summary>
    private static string Bar(string svg) =>
        svg.Split("<rect x=")[^1];

    private static int Rectangles(string svg) =>
        svg.Split("<rect x=").Length - 1;

    private static string Decode(string dataUrl)
    {
        const string prefix = "data:image/svg+xml;base64,";
        Assert.StartsWith(prefix, dataUrl);
        return Encoding.UTF8.GetString(Convert.FromBase64String(dataUrl[prefix.Length..]));
    }
}
