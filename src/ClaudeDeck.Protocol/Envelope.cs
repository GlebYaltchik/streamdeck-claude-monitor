using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeDeck.Protocol;

/// <summary>
/// One message on the wire: a version, a type, and the payload that type implies.
/// </summary>
public sealed record Envelope
{
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web);

    [JsonPropertyName("v")]
    public required int Version { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }

    public static string Write(string type) =>
        JsonSerializer.Serialize(new { v = HubProtocol.Version, type }, Format);

    public static string Write<T>(string type, T payload) =>
        JsonSerializer.Serialize(new { v = HubProtocol.Version, type, payload }, Format);

    /// <summary>Returns null for anything that is not a readable envelope.</summary>
    public static Envelope? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Envelope>(json, Format);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public T? PayloadAs<T>()
        where T : class
    {
        try
        {
            return Payload.ValueKind == JsonValueKind.Object ? Payload.Deserialize<T>(Format) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
