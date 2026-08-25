namespace ClaudeDeck.Core.Sessions;

/// <summary>
/// Which permission modes can be answered from outside the session.
///
/// Measured: a <c>PermissionRequest</c> decision is honoured in <c>default</c> mode — labelled
/// Manual, and accepting <c>manual</c> as an alias — and in <c>dontAsk</c>. Everywhere else the
/// hook still fires and the decision is ignored (findings/holding-a-hook.md).
///
/// That is not a cosmetic distinction. Holding a request nobody can answer would stall a
/// session for as long as the hold lasts and gain nothing, so an unknown mode counts as
/// unanswerable: the cost of being wrong that way is a key that does not light up, and the
/// cost of the other way is somebody's session sitting still.
/// </summary>
public static class PermissionModes
{
    public static bool AnswerableFromOutside(string? mode) =>
        mode is "default" or "manual" or "dontAsk";
}
