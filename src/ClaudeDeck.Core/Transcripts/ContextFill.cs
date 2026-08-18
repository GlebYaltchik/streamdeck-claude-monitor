namespace ClaudeDeck.Core.Transcripts;

/// <summary>
/// How full a session's context is: what it holds, out of what the model allows.
/// </summary>
public sealed record ContextFill(int Tokens, int Window, bool Estimated)
{
    /// <summary>
    /// Deliberately not clamped. A session past its window means the window is wrong — an
    /// unrecognised model taking the conservative fallback — and hiding that behind 100%
    /// would hide the one number that says so. The ring that draws it clamps instead.
    /// </summary>
    public int Percent => Window <= 0 ? 0 : (int)Math.Round(100.0 * Tokens / Window);

    public static ContextFill Of(TranscriptReading reading)
    {
        var window = ContextWindows.For(reading.Model);
        return new ContextFill(reading.Tokens, window.Tokens, window.Estimated);
    }
}
