using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Agent;

/// <summary>
/// Keeps a permission request open for as long as its question is on screen.
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
/// Nothing here decides anything yet. The request is released with no opinion either way,
/// which leaves the session behaving exactly as it would without the agent.
/// </summary>
internal sealed class PendingApprovals(SessionRegistry sessions, TimeSpan hold, Action<string> log)
{
    /// <summary>Raised when a held request ends, so the deck stops showing it.</summary>
    public event Action? Changed;

    public async Task HoldAsync(string sessionId, CancellationToken abandoned)
    {
        try
        {
            await Task.Delay(hold, abandoned);
            log($"permission request in {sessionId} outlived the hold, still marked waiting");
        }
        catch (OperationCanceledException)
        {
            log($"permission request in {sessionId} was answered in the session");
            sessions.ClearApproval(sessionId);
            Changed?.Invoke();
        }
    }
}
