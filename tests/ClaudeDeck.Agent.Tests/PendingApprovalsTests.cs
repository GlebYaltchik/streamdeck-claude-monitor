using ClaudeDeck.Agent;
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
        var approvals = new PendingApprovals(sessions, TimeSpan.FromMinutes(15), _ => { });
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
        var approvals = new PendingApprovals(sessions, TimeSpan.Zero, _ => { });
        approvals.Changed += () => changed++;

        await approvals.HoldAsync("session-1", CancellationToken.None);

        Assert.Equal(SessionState.WaitingApproval, Only(sessions).State);
        Assert.Equal(0, changed);
    }

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
