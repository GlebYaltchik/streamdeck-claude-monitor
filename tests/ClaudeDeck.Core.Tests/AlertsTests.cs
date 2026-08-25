using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Core.Tests;

public class AlertsTests
{
    [Fact]
    public void A_session_waiting_for_the_user_alerts()
    {
        var alerts = new Alerts();

        Assert.True(alerts.Alerting("session-1", waiting: true));
        Assert.False(alerts.Alerting("session-1", waiting: false));
    }

    [Fact]
    public void Muting_silences_every_session()
    {
        var alerts = new Alerts();
        alerts.ToggleMute();

        Assert.False(alerts.Alerting("session-1", waiting: true));
        Assert.False(alerts.Alerting("session-2", waiting: true));
    }

    /// <summary>
    /// Muting stops the deck asking; it does not answer for the user. Everything that was
    /// waiting is still waiting when the mute comes off.
    /// </summary>
    [Fact]
    public void Unmuting_shows_everything_that_was_waiting_all_along()
    {
        var alerts = new Alerts();
        alerts.ToggleMute();
        Assert.False(alerts.Alerting("session-1", waiting: true));

        alerts.ToggleMute();

        Assert.False(alerts.Muted);
        Assert.True(alerts.Alerting("session-1", waiting: true));
    }

    [Fact]
    public void A_tap_stops_one_session_alerting_and_leaves_the_others()
    {
        var alerts = new Alerts();

        alerts.Acknowledge("session-1");

        Assert.False(alerts.Alerting("session-1", waiting: true));
        Assert.True(alerts.Alerting("session-2", waiting: true));
    }

    /// <summary>
    /// The failure this guards against: a tap in the morning swallowing every alert the
    /// session raises for the rest of the day.
    /// </summary>
    [Fact]
    public void The_end_of_the_next_turn_alerts_again_after_a_tap()
    {
        var alerts = new Alerts();
        alerts.Acknowledge("session-1");

        // The user went back to it, so it is no longer waiting.
        alerts.Settle([]);

        // Its next turn ends.
        Assert.True(alerts.Alerting("session-1", waiting: true));
    }

    [Fact]
    public void A_session_still_waiting_keeps_its_acknowledgement()
    {
        var alerts = new Alerts();
        alerts.Acknowledge("session-1");

        alerts.Settle(["session-1"]);

        Assert.False(alerts.Alerting("session-1", waiting: true));
    }

    [Fact]
    public void Muting_is_off_to_begin_with()
    {
        Assert.False(new Alerts().Muted);
    }

    [Fact]
    public void A_change_of_mute_is_announced_once()
    {
        var alerts = new Alerts();
        var changes = 0;
        alerts.Changed += () => changes++;

        alerts.ToggleMute();
        alerts.ToggleMute();

        Assert.Equal(2, changes);
    }
}
