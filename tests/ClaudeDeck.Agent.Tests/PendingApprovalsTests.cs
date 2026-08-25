using ClaudeDeck.Agent;
using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Agent.Tests;

public class PendingApprovalsTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The whole point of holding the request: the client closes it when the question is
    /// answered in the session, and that close is the only thing that says so.
    /// </summary>
    [Fact]
    public async Task A_request_dropped_by_the_client_means_the_question_was_answered()
    {
        var sessions = Waiting();
        var changed = 0;
        var approvals = new PendingApprovals(sessions, new DeckModes(), TimeSpan.FromMinutes(15), _ => { });
        approvals.Changed += () => changed++;

        using var abandoned = new CancellationTokenSource();
        var holding = approvals.HoldAsync("session-1", abandoned.Token);
        await abandoned.CancelAsync();
        await holding;

        Assert.Equal(SessionState.Working, Only(sessions).State);
        Assert.Equal(1, changed);
    }

    /// <summary>
    /// A question can outlive the hold. Nothing then says whether it was answered, and the
    /// last thing known is that somebody was being asked — so the mark stays.
    /// </summary>
    [Fact]
    public async Task A_hold_that_runs_out_leaves_the_session_waiting()
    {
        var sessions = Waiting();
        var changed = 0;
        var approvals = new PendingApprovals(sessions, new DeckModes(), TimeSpan.Zero, _ => { });
        approvals.Changed += () => changed++;

        await approvals.HoldAsync("session-1", CancellationToken.None);

        Assert.Equal(SessionState.WaitingApproval, Only(sessions).State);
        Assert.Equal(0, changed);
    }

    /// <summary>
    /// The answer from the deck reaches the held request, and the session stops waiting.
    /// </summary>
    [Fact]
    public async Task An_answer_from_the_deck_is_what_the_hook_prints()
    {
        var sessions = Waiting();
        var approvals = new PendingApprovals(sessions, new DeckModes(), TimeSpan.FromMinutes(15), _ => { });

        var holding = approvals.HoldAsync("session-1", CancellationToken.None);
        while (!approvals.Resolve("session-1", ApprovalDecision.Denied()))
        {
            await Task.Delay(5);
        }

        var decision = await holding;

        Assert.Equal(ApprovalDecision.Deny, decision?.Behaviour);
        Assert.Equal(SessionState.Working, Only(sessions).State);
    }

    /// <summary>
    /// A key press that arrives after the session answered for itself must do nothing. The
    /// deck is one of two ways to answer, and the slower one loses.
    /// </summary>
    [Fact]
    public void An_answer_for_a_session_that_is_no_longer_waiting_does_nothing()
    {
        var approvals = new PendingApprovals(new SessionRegistry(), new DeckModes(), TimeSpan.Zero, _ => { });

        Assert.False(approvals.Resolve("session-1", ApprovalDecision.Denied()));
    }

    /// <summary>
    /// The switch design §6.4 asks for, and the reason it exists before anything can decide:
    /// off means the question is the session's own affair.
    /// </summary>
    [Fact]
    public void Nothing_is_held_while_the_deck_is_off()
    {
        var modes = new DeckModes();
        modes.Set(DeckMode.Off);
        var approvals = new PendingApprovals(new SessionRegistry(), modes, TimeSpan.Zero, _ => { });

        Assert.False(approvals.Holds(Request("default")));
    }

    [Fact]
    public void A_question_nobody_on_the_deck_could_answer_is_not_held()
    {
        var approvals = new PendingApprovals(new SessionRegistry(), new DeckModes(), TimeSpan.Zero, _ => { });

        Assert.True(approvals.Holds(Request("default")));
        Assert.False(approvals.Holds(Request("auto")));
    }

    private static HookEvent Request(string mode) =>
        new("PermissionRequest", "session-1", Start, PermissionMode: mode, ToolName: "Bash");

    private static SessionRegistry Waiting()
    {
        var sessions = new SessionRegistry();
        sessions.Apply(new HookEvent("SessionStart", "session-1", Start, Source: "startup"));
        sessions.Apply(new HookEvent(
            "PermissionRequest", "session-1", Start, PermissionMode: "default", ToolName: "Bash"));
        return sessions;
    }

    private static Session Only(SessionRegistry sessions) => Assert.Single(sessions.Snapshot());
}
