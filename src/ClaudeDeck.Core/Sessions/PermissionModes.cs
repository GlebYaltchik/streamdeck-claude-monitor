namespace ClaudeDeck.Core.Sessions;

/// <summary>
/// Which permission modes can be answered from outside the session.
///
/// A <c>PermissionRequest</c> decision is honoured in <c>default</c> mode — labelled Manual,
/// and accepting <c>manual</c> as an alias — and in <c>dontAsk</c>: measured
/// (findings/holding-a-hook.md).
///
/// <c>acceptEdits</c> is here on a different footing and is being measured now. The finding
/// declared it unanswerable, but that line covers <c>auto</c>, <c>acceptEdits</c> and
/// everything else in one sentence and no row of its table names a mode. Meanwhile the mode
/// plainly does still ask: it accepts edits on its own and stops for everything else, an MCP
/// call among them, with the ordinary three-way prompt on screen. If that prompt is real then
/// two of its three answers are exactly what the deck sends.
///
/// The old reason for keeping the list short does not survive either. It was that holding a
/// request nobody can answer stalls a session for the length of the hold — but the event
/// fires only when the client is about to ask a person, so a session whose request is held is
/// one already standing still in front of somebody. The prompt stays on screen throughout and
/// answering it there closes ours. Holding costs nothing; the only thing a mode decides is
/// whether our answer counts.
///
/// This decides two things and must not be asked a third. It decides whether the agent holds
/// a request open, and whether the deck may offer to answer it. It does <em>not</em> decide
/// whether a session is shown as waiting: that is true in every mode, and conflating the two
/// once had the deck reporting a stopped session as working.
/// </summary>
public static class PermissionModes
{
    public static bool AnswerableFromOutside(string? mode) =>
        mode is "default" or "manual" or "dontAsk" or "acceptEdits";
}
