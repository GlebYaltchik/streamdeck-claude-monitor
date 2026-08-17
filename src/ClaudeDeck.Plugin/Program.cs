using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ClaudeDeck.Plugin;

/// <summary>
/// Phase 0 probe. Speaks the Elgato WebSocket protocol directly, with no SDK library, and
/// logs everything the device reports. Its job is to answer two questions: what a
/// Stream Deck + XL reports about itself, and whether SVG images and encoder feedback work.
/// </summary>
internal static class Program
{
    private static ClientWebSocket? _socket;
    private static readonly Dictionary<string, int> DialCounters = new(StringComparer.Ordinal);

    private static async Task<int> Main(string[] args)
    {
        var arguments = StreamDeckArguments.Parse(args);
        if (arguments is null)
        {
            ProbeLog.Write("launched without Stream Deck arguments, exiting");
            return 1;
        }

        ProbeLog.Write($"launch port={arguments.Port} uuid={arguments.PluginUuid}");
        ProbeLog.Write($"info {arguments.Info}");

        using var socket = new ClientWebSocket();
        _socket = socket;

        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{arguments.Port}"), CancellationToken.None);
        await SendAsync(new { @event = arguments.RegisterEvent, uuid = arguments.PluginUuid });
        ProbeLog.Write("registered");

        await ReceiveLoopAsync(socket);
        ProbeLog.Write("socket closed");
        return 0;
    }

    private static async Task ReceiveLoopAsync(ClientWebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        var message = new StringBuilder();

        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            }
            catch (Exception ex)
            {
                ProbeLog.Write($"receive failed: {ex.Message}");
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
            {
                continue;
            }

            var text = message.ToString();
            message.Clear();

            try
            {
                using var document = JsonDocument.Parse(text);
                await HandleAsync(document.RootElement);
            }
            catch (Exception ex)
            {
                ProbeLog.Write($"handling failed: {ex.Message} for {text}");
            }
        }
    }

    private static async Task HandleAsync(JsonElement message)
    {
        var eventName = message.TryGetProperty("event", out var value) ? value.GetString() ?? "" : "";
        ProbeLog.Write($"<= {message.GetRawText()}");

        switch (eventName)
        {
            case "willAppear":
                await OnWillAppearAsync(message);
                break;
            case "dialRotate":
                await OnDialRotateAsync(message);
                break;
        }
    }

    private static async Task OnWillAppearAsync(JsonElement message)
    {
        var context = message.GetProperty("context").GetString();
        if (context is null)
        {
            return;
        }

        var controller = message.TryGetProperty("payload", out var payload) &&
                         payload.TryGetProperty("controller", out var value)
            ? value.GetString()
            : null;

        if (controller == "Encoder")
        {
            await SetFeedbackAsync(context, "ClaudeDeck", "turn me", 0);
        }
        else
        {
            await SetImageAsync(context, ProbeIcon.Render("SVG", "works"));
        }
    }

    private static async Task OnDialRotateAsync(JsonElement message)
    {
        var context = message.GetProperty("context").GetString();
        if (context is null)
        {
            return;
        }

        var ticks = message.GetProperty("payload").GetProperty("ticks").GetInt32();
        DialCounters.TryGetValue(context, out var total);
        total = Math.Clamp(total + ticks, 0, 100);
        DialCounters[context] = total;

        await SetFeedbackAsync(context, "ClaudeDeck", $"{total}%", total);
    }

    private static Task SetImageAsync(string context, string svg)
    {
        var dataUrl = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        return SendAsync(new
        {
            @event = "setImage",
            context,
            payload = new { image = dataUrl, target = 0 },
        });
    }

    private static Task SetFeedbackAsync(string context, string title, string value, int indicator)
    {
        return SendAsync(new
        {
            @event = "setFeedback",
            context,
            payload = new { title, value, indicator },
        });
    }

    private static async Task SendAsync(object message)
    {
        if (_socket is not { State: WebSocketState.Open })
        {
            return;
        }

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        ProbeLog.Write($"=> {json}");
    }
}
