using System.Security.Cryptography;
using System.Text;

namespace ClaudeDeck.Hub;

/// <summary>
/// The HTTP upgrade that precedes a websocket. Only the handshake is hand-written; framing
/// is left to <see cref="System.Net.WebSockets.WebSocket.CreateFromStream"/>.
///
/// The listener is a raw <c>TcpListener</c> rather than <c>HttpListener</c> because binding
/// a non-loopback address with <c>HttpListener</c> needs a URL reservation, and the plugin
/// does not run elevated.
/// </summary>
internal static class WebSocketHandshake
{
    /// <summary>Fixed by RFC 6455 section 1.3.</summary>
    private const string ProtocolGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private const int MaximumRequestBytes = 8 * 1024;

    public static string AcceptKey(string clientKey) =>
        Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(clientKey + ProtocolGuid)));

    /// <summary>Answers the upgrade request, leaving the stream ready for websocket frames.</summary>
    public static async Task<bool> TryAcceptAsync(Stream stream, CancellationToken cancellationToken)
    {
        var request = await ReadRequestAsync(stream, cancellationToken);
        var key = request is null ? null : FindHeader(request, "Sec-WebSocket-Key");

        if (key is null)
        {
            await WriteAsync(stream, "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n", cancellationToken);
            return false;
        }

        await WriteAsync(
            stream,
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {AcceptKey(key)}\r\n\r\n",
            cancellationToken);

        return true;
    }

    private static async Task<string?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        // A byte at a time so nothing past the blank line is consumed: what follows is
        // already websocket framing and belongs to the WebSocket, which cannot be handed
        // bytes we swallowed.
        var request = new StringBuilder();
        var buffer = new byte[1];

        while (request.Length < MaximumRequestBytes)
        {
            if (await stream.ReadAsync(buffer, cancellationToken) == 0)
            {
                return null;
            }

            request.Append((char)buffer[0]);

            if (request.Length >= 4 &&
                request[^4] == '\r' && request[^3] == '\n' && request[^2] == '\r' && request[^1] == '\n')
            {
                return request.ToString();
            }
        }

        return null;
    }

    private static string? FindHeader(string request, string name)
    {
        foreach (var line in request.Split("\r\n"))
        {
            var separator = line.IndexOf(':');
            if (separator > 0 && line.AsSpan(0, separator).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    private static Task WriteAsync(Stream stream, string response, CancellationToken cancellationToken) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).AsTask();
}
