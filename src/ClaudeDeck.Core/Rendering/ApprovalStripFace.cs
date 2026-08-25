namespace ClaudeDeck.Core.Rendering;

/// <summary>What the touch strip shows about the question a session is waiting on.</summary>
public sealed record ApprovalStripFace(string Title, string Value);

/// <summary>
/// Draws the pending question on an encoder's touch strip.
///
/// The strip is where the command goes in full, because it is the widest surface the deck
/// has and the key beside it has room for two short lines at most. It is not the only place
/// the command can be read — the session's own prompt is on screen the whole time — so the
/// strip is what says which session to walk to, not the last word on what it asked.
/// </summary>
public static class ApprovalStrip
{
    private const string Idle = "APPROVALS";

    public static ApprovalStripFace Render(string? session, string? tool, string? summary)
    {
        if (tool is not { Length: > 0 })
        {
            return new ApprovalStripFace(Idle, "nothing waiting");
        }

        var name = session is { Length: > 0 } ? session : "session";
        return new ApprovalStripFace($"{tool} · {name}", summary is { Length: > 0 } ? summary : tool);
    }
}
