namespace ClaudeDeck.Plugin;

/// <summary>
/// Everything the plugin needs from Stream Deck. Actions depend on this rather than on the
/// websocket, so they can be exercised without a device attached.
/// </summary>
internal interface IDeckConnection
{
    /// <summary>Queues an update; delivery is rate limited per control.</summary>
    void Update(string context, DeckUpdate update);

    /// <summary>
    /// Sends one frame of an animation straight out, past the rate limit.
    ///
    /// The limit is there because a single dial spin delivered 116 events and would answer
    /// every one — it bounds what an input flood can cost. An animating key is the opposite
    /// case: the plugin decides the rate, there is at most a handful of them, and holding
    /// frames back is exactly what made the first attempt look stuttered.
    /// </summary>
    Task AnimateAsync(string context, DeckUpdate update);

    /// <summary>Drops any queued state for a control that has gone away.</summary>
    void Forget(string context);

    /// <summary>
    /// Persists a control's settings, so a choice made on the hardware survives a restart
    /// the same way one made in the Property Inspector does.
    /// </summary>
    Task SaveSettingsAsync(string context, object settings);

    /// <summary>
    /// Persists settings belonging to the plugin rather than to one control. What the deck
    /// may do about permissions is one of those: it outlives any key that happens to be on
    /// the deck at the time, and a deck with no such key still has to have an answer.
    /// </summary>
    Task SaveGlobalSettingsAsync(object settings);
}
