using System.Text.Json;
using ClaudeDeck.Core.Permissions;

namespace ClaudeDeck.Plugin;

/// <summary>
/// The plugin's own settings, as opposed to a control's.
///
/// What lives here is what outlives any one key: how far the deck is allowed into permission
/// decisions, and which way round the pair of answer keys is. Both belong to the plugin
/// rather than to a key because a deck may carry no such key at all, and "whatever the last
/// key to appear happened to say" is not an answer for a deck that has none.
/// </summary>
internal static class PluginSettings
{
    /// <summary>
    /// The mode a settings payload names. Anything unreadable — no settings yet, a value from
    /// a newer build, a file edited by hand — reads as Observe, which is what a plugin that
    /// has never been told anything does.
    /// </summary>
    public static DeckMode Mode(JsonElement payload) =>
        DeckModes.Parse(Field(payload, "mode") is { ValueKind: JsonValueKind.String } mode
            ? mode.GetString()
            : null);

    /// <summary>
    /// Whether the answer keys are the other way round. Unreadable reads as not swapped,
    /// which is the arrangement a pair takes when nobody has said anything about it.
    /// </summary>
    public static bool Swapped(JsonElement payload) =>
        Field(payload, "swapped") is { ValueKind: JsonValueKind.True };

    private static JsonElement? Field(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object &&
        payload.TryGetProperty("settings", out var settings) &&
        settings.ValueKind == JsonValueKind.Object &&
        settings.TryGetProperty(name, out var value)
            ? value
            : null;
}
