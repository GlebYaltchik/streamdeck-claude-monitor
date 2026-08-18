using System.Security.Cryptography;
using System.Text;

namespace ClaudeDeck.Protocol;

/// <summary>
/// The secret an agent presents at handshake. Required on every bind address, loopback
/// included: any local process can reach the port, and the deck must only show what our own
/// agents report.
///
/// The hub owns the file and creates it on first run. An agent on the same machine reads it;
/// an agent inside WSL cannot see the Windows profile, so it takes the value from the
/// environment instead.
/// </summary>
public static class HubToken
{
    public const string EnvironmentVariable = "CLAUDEDECK_HUB_TOKEN";

    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeDeck",
            "hub-token");

    /// <summary>The token if one is available, null if the agent has not been given one.</summary>
    public static string? Read()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } fromEnvironment)
        {
            return fromEnvironment;
        }

        try
        {
            var path = DefaultPath();
            return File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } stored ? stored : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>For the hub, which owns the token and mints one when there is none.</summary>
    public static string ReadOrCreate(Action<string>? log = null)
    {
        if (Read() is { } existing)
        {
            return existing;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        try
        {
            var path = DefaultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The hub still works, but nothing else can learn the token, so no agent will
            // connect until the directory is writable again. That is a far better outcome
            // than taking the plugin down over it.
            log?.Invoke($"could not store the hub token: {ex.Message}");
        }

        return token;
    }

    /// <summary>Compared without an early exit, so a wrong token leaks no position.</summary>
    public static bool Matches(string expected, string presented) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(presented));
}
