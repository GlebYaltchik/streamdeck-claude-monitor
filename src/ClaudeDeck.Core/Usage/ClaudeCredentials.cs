using System.Text.Json;

namespace ClaudeDeck.Core.Usage;

/// <summary>
/// OAuth credentials written by Claude Code's own login. Token values live in memory only:
/// they are never logged, never persisted by us, and never leave the machine that read them.
/// </summary>
public sealed record ClaudeCredentials(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt)
{
    /// <summary>Treat a token expiring within this margin as already expired.</summary>
    public static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(1);

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is not null && ExpiresAt - ExpiryMargin <= now;
}

public interface ICredentialsStore
{
    /// <summary>Returns null when credentials are absent or unusable. Never throws.</summary>
    ClaudeCredentials? Read();
}

/// <summary>
/// Reads <c>.credentials.json</c> from disk.
///
/// The path is configurable because the file does not always sit in the local home
/// directory: on a Windows host whose sessions run in WSL, the token may only exist inside
/// the distribution, reachable as <c>\\wsl.localhost\{distro}\home\{user}\.claude</c>.
/// </summary>
public sealed class FileCredentialsStore(string? path = null) : ICredentialsStore
{
    private readonly string _path = string.IsNullOrWhiteSpace(path) ? DefaultPath() : path;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    public ClaudeCredentials? Read()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                return null;
            }

            var accessToken = ReadString(oauth, "accessToken");
            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            return new ClaudeCredentials(
                accessToken,
                ReadString(oauth, "refreshToken"),
                ReadExpiry(oauth));
        }
        catch
        {
            // Missing, locked or malformed all mean the same thing to the caller.
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadExpiry(JsonElement element) =>
        element.TryGetProperty("expiresAt", out var value) && value.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(value.GetInt64())
            : null;
}
