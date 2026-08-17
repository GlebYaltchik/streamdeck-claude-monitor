using System.Text.Json;

namespace ClaudeDeck.Plugin;

/// <summary>
/// One inbound message from Stream Deck, with the fields every handler needs lifted out and
/// the payload kept for the rest.
/// </summary>
internal sealed record DeckEvent(
    string Name,
    string? Context,
    string? Action,
    string? Device,
    string? Controller,
    JsonElement Payload)
{
    public static DeckEvent Parse(JsonElement message)
    {
        var payload = message.TryGetProperty("payload", out var value) ? value.Clone() : default;

        return new DeckEvent(
            Name: ReadString(message, "event") ?? "",
            Context: ReadString(message, "context"),
            Action: ReadString(message, "action"),
            Device: ReadString(message, "device"),
            Controller: payload.ValueKind == JsonValueKind.Object ? ReadString(payload, "controller") : null,
            Payload: payload);
    }

    public bool IsEncoder => Controller == "Encoder";

    private static string? ReadString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.GetString()
            : null;
    }
}
