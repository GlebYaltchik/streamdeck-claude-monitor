namespace ClaudeDeck.Core.Rendering;

/// <summary>What the touch strip shows about the question a session is waiting on.</summary>
public sealed record ApprovalStripFace(string Title, string Value);

/// <summary>
/// Draws who is waiting on an encoder's touch strip.
///
/// The command is deliberately not here. The strip was tried with it and rejected on the
/// device: one dial's segment holds about two dozen characters, and at a size that fits them
/// nothing is readable at arm's length. A command that has to be cut in half is worse than
/// no command, because the half that survives can change what the rest of it meant.
///
/// So the strip answers "who", the key answers "what", and the whole command stays where it
/// already is in full — in the session's own window, on screen the entire time.
/// </summary>
public static class ApprovalStrip
{
    private const string Idle = "APPROVALS";

    public static ApprovalStripFace Render(string? session, string? tool)
    {
        if (tool is not { Length: > 0 })
        {
            return new ApprovalStripFace(Idle, "none waiting");
        }

        return new ApprovalStripFace($"{Idle} · {tool}", session is { Length: > 0 } ? session : "session");
    }
}
