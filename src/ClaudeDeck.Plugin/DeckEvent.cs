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
    DeckCoordinates? Coordinates,
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
            Coordinates: DeckCoordinates.Parse(payload),
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

/// <summary>
/// Where a key sits on its device. Stream Deck sends this with every appearance, which is
/// what lets session slots be ordered without the user configuring a number per key.
/// </summary>
internal sealed record DeckCoordinates(int Column, int Row)
{
    public static DeckCoordinates? Parse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("coordinates", out var coordinates) ||
            coordinates.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return coordinates.TryGetProperty("column", out var column) && column.TryGetInt32(out var x) &&
               coordinates.TryGetProperty("row", out var row) && row.TryGetInt32(out var y)
            ? new DeckCoordinates(x, y)
            : null;
    }
}
