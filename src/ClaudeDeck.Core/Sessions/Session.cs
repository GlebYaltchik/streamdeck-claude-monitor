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

    /// <summary>The last folder component of the working directory, for a key label.</summary>
    public string? Project => string.IsNullOrEmpty(Cwd) ? null : new DirectoryInfo(Cwd).Name;
}
