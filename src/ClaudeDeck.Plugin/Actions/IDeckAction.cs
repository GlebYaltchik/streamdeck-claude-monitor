namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// Handles the events for one action UUID from the manifest.
/// </summary>
internal interface IDeckAction
{
    string Uuid { get; }

    Task HandleAsync(DeckEvent deckEvent);
}
