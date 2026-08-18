using System.Text.Json;

namespace ClaudeDeck.Core.Usage;

/// <summary>
/// The access token written by Claude Code's own login.
///
/// Only the token is taken. Refreshing is deliberately not done here — see
/// <see cref="ClaudeUsageProvider"/> — so the refresh token and the stored expiry are none
/// of our business. The value lives in memory only: never logged, never persisted by us,
/// never sent anywhere except the usage request it authorizes.
/// </summary>
public sealed record ClaudeCredentials(string AccessToken);

public enum CredentialsOutcome
{
    Ok,

    /// <summary>The file was read and holds no usable token. The user has to log in.</summary>
    NoToken,

    /// <summary>
    /// The file could not be reached. This is our problem, not the user's: a credentials
    /// file on a WSL share stops being readable when the distribution sleeps, and telling
    /// someone to log in because of that is both wrong and impossible to act on.
    /// </summary>
    Unreachable,
}

public sealed record CredentialsResult(CredentialsOutcome Outcome, ClaudeCredentials? Credentials, string? Error = null)
{
    public static CredentialsResult Ok(ClaudeCredentials credentials) => new(CredentialsOutcome.Ok, credentials);

    public static CredentialsResult NoToken(string reason) => new(CredentialsOutcome.NoToken, null, reason);

    public static CredentialsResult Unreachable(string reason) => new(CredentialsOutcome.Unreachable, null, reason);
}

public interface ICredentialsStore
{
    /// <summary>Never throws; the failure is described by the outcome.</summary>
    CredentialsResult Read();
}

/// <summary>
/// Reads <c>.credentials.json</c> from disk.
///
/// The path is configurable because the file does not always sit in the local home
/// directory: on a Windows host whose sessions run in WSL, the token may only exist inside
/// the distribution, reachable as <c>\wsl.localhost\{distro}\home\{user}\.claude</c>.
/// </summary>
public sealed class FileCredentialsStore(string? path = null) : ICredentialsStore
{
    private readonly string _path = string.IsNullOrWhiteSpace(path) ? DefaultPath() : path;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    public CredentialsResult Read()
    {
        string contents;
        try
        {
            contents = File.ReadAllText(_path);
        }
        catch (FileNotFoundException)
        {
            return CredentialsResult.NoToken("Credentials file not found.");
        }
        catch (Exception ex)
        {
            // Everything else — a sleeping WSL share, a broken UNC session, a lock — is an
            // outage rather than a missing login. The reason is carried so a log can show it
            // instead of swallowing it.
            return CredentialsResult.Unreachable($"Credentials file unreadable: {ex.GetType().Name}.");
        }

        try
        {
            using var document = JsonDocument.Parse(contents);
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                return CredentialsResult.NoToken("Credentials file has no OAuth section.");
            }

            // Observed in the wild: the client blanks the token in place, leaving the other
            // fields behind. An empty string is no token at all.
            var accessToken = ReadString(oauth, "accessToken");
            if (string.IsNullOrEmpty(accessToken))
            {
                return CredentialsResult.NoToken("Credentials file has no access token.");
            }

            return CredentialsResult.Ok(new ClaudeCredentials(accessToken));
        }
        catch (JsonException)
        {
            return CredentialsResult.NoToken("Credentials file is not valid JSON.");
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
