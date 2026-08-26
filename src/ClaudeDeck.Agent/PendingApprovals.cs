using System.Collections.Concurrent;
using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Agent;

/// <summary>
/// Keeps a permission request open for as long as its question is on screen, and answers it
/// if the deck says so.
///
/// Measured behaviour this rests on (findings/holding-a-hook.md): the session shows its own
/// prompt while a <c>PermissionRequest</c> hook is still running, and the client closes the
/// hook's connection the moment the permission is resolved without it. So holding the request
/// costs the user nothing — the question is answerable where it always was — and the close is
/// the only signal that says the question is gone.
///
/// The hold is therefore deliberately long, and shorter than the hook's own timeout, so that
/// a connection dropped inside it means an answer rather than a client giving up on us. When
/// the hold runs out first the session is left marked as waiting: at that point we no longer
/// know, and the last thing we did know was that somebody was being asked.
///
/// One question per session, because a session waiting on one is not running anything else.
/// </summary>
internal sealed class PendingApprovals(
    SessionRegistry sessions,
    TimeSpan hold,
    Action<string> log)
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalDecision?>> _waiting =
        new(StringComparer.Ordinal);

    /// <summary>Raised when a held request ends, so the deck stops showing it.</summary>
    public event Action? Changed;

    /// <summary>
    /// Whether this event is one worth holding. A question in a mode whose decisions the
    /// client ignores is not: holding it would stall the session and gain nothing.
    /// </summary>
    public bool Holds(HookEvent hookEvent) =>
        hookEvent.Name == "PermissionRequest" &&
        PermissionModes.AnswerableFromOutside(hookEvent.PermissionMode);

    /// <summary>
    /// Waits for an answer from the deck, for the session to answer for itself, or for the
    /// hold to run out. Returns what to print, and null for "no opinion" — which leaves the
    /// session exactly as it would be without the agent.
    /// </summary>
    public async Task<ApprovalDecision?> HoldAsync(string sessionId, CancellationToken abandoned)
    {
        var answer = new TaskCompletionSource<ApprovalDecision?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[sessionId] = answer;

        try
        {
            // Three ways this ends, and they are told apart by which task finished rather
            // than by an exception: a cancelled Task.Delay inside WhenAny does not throw.
            var finished = await Task.WhenAny(answer.Task, Task.Delay(hold, abandoned));

            if (finished == answer.Task)
            {
                var decision = await answer.Task;
                log($"permission request in {sessionId} answered on the deck: {decision?.Behaviour}");
                sessions.ClearApproval(sessionId);
                Changed?.Invoke();
                return decision;
            }

            if (abandoned.IsCancellationRequested)
            {
                log($"permission request in {sessionId} was answered in the session");
                sessions.ClearApproval(sessionId);
                Changed?.Invoke();
                return null;
            }

            log($"permission request in {sessionId} outlived the hold, still marked waiting");
        }
        finally
        {
            _waiting.TryRemove(sessionId, out _);
        }

        return null;
    }

    /// <summary>
    /// The deck's answer. Ignored when the session is no longer waiting on us — it may have
    /// been answered in its own window a moment earlier, and a key press that arrives late
    /// must do nothing rather than something.
    /// </summary>
    public bool Resolve(string sessionId, ApprovalDecision decision) =>
        _waiting.TryGetValue(sessionId, out var answer) && answer.TrySetResult(decision);
}
