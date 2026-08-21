using ClaudeDeck.Core.Transcripts;

namespace ClaudeDeck.Core.Sessions;

public enum SessionState
{
    /// <summary>The turn is over and it is the user's move.</summary>
    Idle,

    /// <summary>The model is working, possibly running a tool.</summary>
    Working,

    /// <summary>A permission request is on screen. Set by our own gate, not by any hook.</summary>
    WaitingApproval,

    Compacting,

    /// <summary>No sign of life, and no <c>SessionEnd</c> ever arrived.</summary>
    Stale,
}

/// <summary>
/// One Claude Code session as the deck sees it.
/// </summary>
public sealed record Session
{
    public required string Id { get; init; }

    public required SessionState State { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset LastEventAt { get; init; }

    public string? Cwd { get; init; }

    public string? TranscriptPath { get; init; }

    public string? PermissionMode { get; init; }

    /// <summary>The tool currently running, when one is.</summary>
    public string? CurrentTool { get; init; }

    /// <summary>
    /// How many subagents have finished. Subagents belong to their parent and never get a
    /// slot of their own.
    /// </summary>
    public int SubagentRuns { get; init; }

    /// <summary>
    /// What the session is called. From the transcript, and the only thing that tells two
    /// sessions in the same repository apart on a key.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The model finished a turn and it is the user's move. Narrower than <c>Idle</c>, which
    /// a session also sits in from the moment it starts: this is set only by <c>Stop</c>, so
    /// it means something happened that somebody should look at.
    /// </summary>
    public bool AwaitingUser { get; init; }

    /// <summary>The model in use. From the transcript: no hook payload carries it.</summary>
    public string? Model { get; init; }

    /// <summary>The git branch, also from the transcript, for a key label.</summary>
    public string? Branch { get; init; }

    /// <summary>How full the context is, once a transcript has said so.</summary>
    public ContextFill? Context { get; init; }

    /// <summary>The last folder component of the working directory, for a key label.</summary>
    public string? Project => string.IsNullOrEmpty(Cwd) ? null : new DirectoryInfo(Cwd).Name;
}
