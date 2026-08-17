using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ClaudeDeck.Core;

namespace ClaudeDeck.Plugin;

/// <summary>
/// Speaks the Elgato websocket protocol directly. There is no SDK wrapper: the protocol is
/// small, and a wrapper that does not recognise a new device type would be a liability
/// rather than a convenience.
/// </summary>
internal sealed class StreamDeckConnection : IDeckConnection, IAsyncDisposable
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(250);

    private readonly StreamDeckArguments _arguments;
    private readonly ClientWebSocket _socket = new();
    private readonly UpdateCoalescer<DeckUpdate> _updates;

    public StreamDeckConnection(StreamDeckArguments arguments)
    {
        _arguments = arguments;
        _updates = new UpdateCoalescer<DeckUpdate>(UpdateInterval, SendUpdateAsync);
    }

    public event Func<DeckEvent, Task>? EventReceived;

    public void Update(string context, DeckUpdate update) => _updates.Submit(context, update);

    public void Forget(string context) => _updates.Forget(context);

    public Task SaveSettingsAsync(string context, object settings) =>
        SendAsync(new { @event = "setSettings", context, payload = settings });

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_arguments.Port}"), cancellationToken);
        await SendAsync(new { @event = _arguments.RegisterEvent, uuid = _arguments.PluginUuid });
        PluginLog.Write("registered with Stream Deck");

        var flushing = _updates.RunAsync(cancellationToken);
        await ReceiveLoopAsync(cancellationToken);
        await flushing;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var message = new StringBuilder();

        while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (Exception ex)
            {
                PluginLog.Write($"receive failed: {ex.Message}");
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                PluginLog.Write("Stream Deck closed the connection");
                return;
            }

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
            {
                continue;
            }

            var text = message.ToString();
            message.Clear();
            await DispatchAsync(text);
        }
    }

    private async Task DispatchAsync(string text)
    {
        DeckEvent deckEvent;
        try
        {
            using var document = JsonDocument.Parse(text);
            deckEvent = DeckEvent.Parse(document.RootElement);
        }
        catch (Exception ex)
        {
            PluginLog.Write($"could not parse message: {ex.Message}");
            return;
        }

        var handler = EventReceived;
        if (handler is null)
        {
            return;
        }

        try
        {
            await handler(deckEvent);
        }
        catch (Exception ex)
        {
            PluginLog.Write($"handler for {deckEvent.Name} failed: {ex}");
        }
    }

    private Task SendUpdateAsync(string context, DeckUpdate update)
    {
        return update switch
        {
            ImageUpdate image => SendAsync(new
            {
                @event = "setImage",
                context,
                payload = new { image = image.DataUrl, target = 0 },
            }),
            // The indicator accepts either a bare value or an object carrying layout
            // properties. The object form is only used when there is a colour to set, so a
            // layout that does not understand it is never handed one.
            FeedbackUpdate { IndicatorColour: null } feedback => SendAsync(new
            {
                @event = "setFeedback",
                context,
                payload = new { title = feedback.Title, value = feedback.Value, indicator = feedback.Indicator },
            }),
            FeedbackUpdate feedback => SendAsync(new
            {
                @event = "setFeedback",
                context,
                payload = new
                {
                    title = feedback.Title,
                    value = feedback.Value,
                    indicator = new { value = feedback.Indicator, bar_fill_c = feedback.IndicatorColour },
                },
            }),
            _ => Task.CompletedTask,
        };
    }

    private async Task SendAsync(object message)
    {
        if (_socket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutting down", CancellationToken.None);
        }

        _socket.Dispose();
    }
}
