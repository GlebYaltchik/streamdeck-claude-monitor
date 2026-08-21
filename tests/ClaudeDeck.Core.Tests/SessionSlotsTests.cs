using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Core.Tests;

public class SessionSlotsTests
{
    [Fact]
    public void Sessions_take_the_lowest_slots_in_the_order_they_are_given()
    {
        var slots = new SessionSlots();

        var placed = slots.Assign(["a", "b", "c"]);

        Assert.Equal(0, placed["a"]);
        Assert.Equal(1, placed["b"]);
        Assert.Equal(2, placed["c"]);
    }

    /// <summary>
    /// The property the whole design rests on: a key must not move under your fingers while
    /// you are looking at it, whatever happens to the other sessions.
    /// </summary>
    [Fact]
    public void A_session_keeps_its_slot_when_an_earlier_one_ends()
    {
        var slots = new SessionSlots();
        slots.Assign(["a", "b", "c"]);

        var placed = slots.Assign(["b", "c"]);

        Assert.Equal(1, placed["b"]);
        Assert.Equal(2, placed["c"]);
        Assert.DoesNotContain("a", placed.Keys);
    }

    [Fact]
    public void A_freed_slot_goes_to_the_next_new_session()
    {
        var slots = new SessionSlots();
        slots.Assign(["a", "b", "c"]);
        slots.Assign(["a", "c"]);

        var placed = slots.Assign(["a", "c", "d"]);

        Assert.Equal(1, placed["d"]);
        Assert.Equal(0, placed["a"]);
        Assert.Equal(2, placed["c"]);
    }

    [Fact]
    public void A_session_that_comes_back_is_treated_as_new()
    {
        // Nothing remembers a session across its own end; the id is free again.
        var slots = new SessionSlots();
        slots.Assign(["a", "b"]);
        slots.Assign(["b"]);

        var placed = slots.Assign(["b", "a"]);

        Assert.Equal(1, placed["b"]);
        Assert.Equal(0, placed["a"]);
    }

    [Fact]
    public void Assignments_survive_a_pass_that_changes_nothing()
    {
        var slots = new SessionSlots();
        var first = slots.Assign(["a", "b"]);

        var second = slots.Assign(["a", "b"]);

        Assert.Equal(first["a"], second["a"]);
        Assert.Equal(first["b"], second["b"]);
    }

    [Fact]
    public void Nothing_live_means_nothing_placed()
    {
        var slots = new SessionSlots();
        slots.Assign(["a"]);

        Assert.Empty(slots.Assign([]));
    }
}
