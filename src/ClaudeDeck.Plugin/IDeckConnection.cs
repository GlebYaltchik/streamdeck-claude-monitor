namespace ClaudeDeck.Plugin;

/// <summary>
/// Everything the plugin needs from Stream Deck. Actions depend on this rather than on the
/// websocket, so they can be exercised without a device attached.
/// </summary>
internal interface IDeckConnection
{
    /// <summary>Queues an update; delivery is rate limited per control.</summary>
    void Update(string context, DeckUpdate update);

    /// <summary>Drops any queued state for a control that has gone away.</summary>
    void Forget(string context);
}
