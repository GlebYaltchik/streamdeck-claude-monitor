using ClaudeDeck.Core.Permissions;

namespace ClaudeDeck.Core.Tests;

public class AddressingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_addressed_session_is_the_one_the_pair_means()
    {
        var addressing = new Addressing();

        addressing.Address("session-1", "Bash", "npm test", Now);

        Assert.Equal("session-1", addressing.Current(Now)?.SessionId);
    }

    /// <summary>Pressing the same key twice means never mind.</summary>
    [Fact]
    public void Addressing_the_same_session_again_drops_the_address()
    {
        var addressing = new Addressing();

        addressing.Address("session-1", "Bash", "npm test", Now);
        addressing.Address("session-1", "Bash", "npm test", Now);

        Assert.Null(addressing.Current(Now));
    }

    [Fact]
    public void Addressing_another_session_moves_the_address()
    {
        var addressing = new Addressing();

        addressing.Address("session-1", "Bash", "npm test", Now);
        addressing.Address("session-2", "Edit", "src/Program.cs", Now);

        Assert.Equal("session-2", addressing.Current(Now)?.SessionId);
    }

    /// <summary>
    /// An address is a sentence half spoken. One left lying around is how the wrong session
    /// gets answered, so it runs out on its own.
    /// </summary>
    [Fact]
    public void An_address_lapses_when_its_window_runs_out()
    {
        var addressing = new Addressing();

        addressing.Address("session-1", "Bash", "npm test", Now);

        Assert.NotNull(addressing.Current(Now + Addressing.Window - TimeSpan.FromSeconds(1)));
        Assert.Null(addressing.Current(Now + Addressing.Window));
    }

    [Fact]
    public void What_is_left_of_the_window_drains_to_nothing()
    {
        var addressing = new Addressing();

        addressing.Address("session-1", "Bash", "npm test", Now);

        Assert.Equal(1, addressing.Remaining(Now));
        Assert.Equal(0.5, addressing.Remaining(Now + (Addressing.Window / 2)), 2);
        Assert.Equal(0, addressing.Remaining(Now + Addressing.Window));
    }

    /// <summary>
    /// The question is part of the address. A session answered in its own window and then
    /// asked something else is the same session with a different question, and a press meant
    /// for the first must not answer the second.
    /// </summary>
    [Fact]
    public void An_address_is_dropped_when_its_own_question_is_gone()
    {
        var addressing = new Addressing();
        addressing.Address("session-1", "Bash", "npm test", Now);

        addressing.Settle([("session-1", "Bash", "rm -rf build")]);

        Assert.Null(addressing.Current(Now));
    }

    [Fact]
    public void An_address_survives_while_its_own_question_is_still_open()
    {
        var addressing = new Addressing();
        addressing.Address("session-1", "Bash", "npm test", Now);

        addressing.Settle([("session-2", "Edit", "src/Program.cs"), ("session-1", "Bash", "npm test")]);

        Assert.NotNull(addressing.Current(Now));
    }

    /// <summary>
    /// Two presses arriving together must not both find the address live: the second would be
    /// answering a question nobody addressed, which is what the window exists to stop.
    /// </summary>
    [Fact]
    public void Taking_the_address_leaves_none_behind()
    {
        var addressing = new Addressing();
        addressing.Address("session-1", "Bash", "npm test", Now);

        Assert.Equal("session-1", addressing.Take(Now)?.SessionId);
        Assert.Null(addressing.Take(Now));
        Assert.Null(addressing.Current(Now));
    }

    [Fact]
    public void A_lapsed_address_cannot_be_taken()
    {
        var addressing = new Addressing();
        addressing.Address("session-1", "Bash", "npm test", Now);

        Assert.Null(addressing.Take(Now + Addressing.Window));
    }

    [Fact]
    public void A_lapsed_address_is_announced_once()
    {
        var addressing = new Addressing();
        var changes = 0;

        addressing.Address("session-1", "Bash", "npm test", Now);
        addressing.Changed += () => changes++;

        addressing.Expire(Now + TimeSpan.FromSeconds(1));
        Assert.Equal(0, changes);

        addressing.Expire(Now + Addressing.Window);
        addressing.Expire(Now + Addressing.Window);

        Assert.Equal(1, changes);
    }
}
