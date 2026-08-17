namespace ClaudeDeck.Plugin;

/// <summary>
/// A pending change to one control. Keys and encoders reach the device through different
/// messages, so the coalescer carries the intent and the connection decides how to send it.
/// </summary>
internal abstract record DeckUpdate;

internal sealed record ImageUpdate(string DataUrl) : DeckUpdate;

internal sealed record FeedbackUpdate(string Title, string Value, int Indicator, string? IndicatorColour = null)
    : DeckUpdate;
