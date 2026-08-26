using System.Text.Json;
using ClaudeDeck.Core.Permissions;

namespace ClaudeDeck.Plugin;

/// <summary>
/// The plugin's own settings, as opposed to a control's.
///
/// Only one thing lives here so far: how far the deck is allowed into permission decisions.
/// It belongs to the plugin rather than to a key because a deck may carry no Approvals key at
/// all, and "whatever the last key to appear happened to say" is not an answer for a deck that
/// has none.
/// </summary>
internal static class PluginSettings
{
    /// <summary>
    /// The mode a settings payload names. Anything unreadable — no settings yet, a value from
    /// a newer build, a file edited by hand — reads as Observe, which is what a plugin that
    /// has never been told anything does.
    /// </summary>
    public static DeckMode Mode(JsonElement payload) =>
        DeckModes.Parse(
            payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("settings", out var settings) &&
            settings.ValueKind == JsonValueKind.Object &&
            settings.TryGetProperty("mode", out var mode) &&
            mode.ValueKind == JsonValueKind.String
                ? mode.GetString()
                : null);
}
