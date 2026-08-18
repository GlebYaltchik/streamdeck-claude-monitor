namespace ClaudeDeck.Core.Transcripts;

/// <summary>
/// How much context a model holds, and whether that number is known or guessed.
/// </summary>
public sealed record ContextWindow(int Tokens, bool Estimated);

/// <summary>
/// The denominator for context fill: model identifier to window size.
///
/// The table is the current Claude line-up, where a million tokens is the rule and 200k the
/// exception. That is the opposite of what design §4.2 first assumed, and it was measured
/// here: a single request on a plain <c>claude-opus-5</c> — no suffix — read 638,450 tokens
/// of context. Treating that model as 200k would have shown a key three times past full.
/// </summary>
public static class ContextWindows
{
    /// <summary>What an unrecognised model is assumed to hold.</summary>
    public const int Fallback = 200_000;

    public const int Million = 1_000_000;

    /// <summary>
    /// Ordered longest identifier first, so a dated snapshot matches its own family rather
    /// than a shorter name that happens to prefix it.
    /// </summary>
    private static readonly (string Model, int Tokens)[] Known =
    [
        ("claude-sonnet-4-5", Fallback),
        ("claude-sonnet-4-6", Million),
        ("claude-sonnet-4-0", Fallback),
        ("claude-haiku-4-5", Fallback),
        ("claude-opus-4-5", Fallback),
        ("claude-opus-4-6", Million),
        ("claude-opus-4-7", Million),
        ("claude-opus-4-8", Million),
        ("claude-opus-4-1", Fallback),
        ("claude-opus-4-0", Fallback),
        ("claude-sonnet-5", Million),
        ("claude-fable-5", Million),
        ("claude-opus-5", Million),
    ];

    /// <summary>
    /// The window for a model as the transcript names it. An unknown model is reported as an
    /// estimate rather than a fact, so a key can say the percentage is not to be trusted.
    /// </summary>
    public static ContextWindow For(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return new ContextWindow(Fallback, Estimated: true);
        }

        var (name, suffix) = Split(model.Trim());

        // The suffix names the window outright, which settles the question whatever the
        // model is. Documented in design §4.2 as appearing in real tool responses.
        if (suffix is not null && suffix.Equals("1m", StringComparison.OrdinalIgnoreCase))
        {
            return new ContextWindow(Million, Estimated: false);
        }

        foreach (var (known, tokens) in Known)
        {
            if (name.StartsWith(known, StringComparison.OrdinalIgnoreCase))
            {
                return new ContextWindow(tokens, Estimated: false);
            }
        }

        // Falling back low rather than high on purpose. Understating the window overstates
        // how full the context is, which warns early; the opposite would let a session hit
        // the limit while the key still looked calm.
        return new ContextWindow(Fallback, Estimated: true);
    }

    /// <summary>Separates <c>claude-opus-5[1m]</c> into the model and what the brackets held.</summary>
    private static (string Name, string? Suffix) Split(string model)
    {
        var opening = model.IndexOf('[');

        return opening > 0 && model.EndsWith(']')
            ? (model[..opening], model[(opening + 1)..^1])
            : (model, null);
    }
}
