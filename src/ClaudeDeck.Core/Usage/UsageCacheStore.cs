using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeDeck.Core.Usage;

/// <summary>
/// What survives a restart. Carries no token material — only percentages, timings and the
/// backoff state.
/// </summary>
public sealed record UsageCacheState(
    UsageSnapshot? LastGood,
    DateTimeOffset LastFetch,
    DateTimeOffset NextAttempt,
    TimeSpan Backoff,
    /// <summary>
    /// What was last shown, which is not always the last success. A rate limit hit before any
    /// success has nothing good to fall back on, and that is precisely when the cooling
    /// period must survive a restart.
    /// </summary>
    UsageSnapshot? LastShown = null);

public interface IUsageCacheStore
{
    UsageCacheState? Read(string key);

    void Write(string key, UsageCacheState state);
}

/// <summary>
/// Persists the cache as JSON.
///
/// Without this, every restart starts with an empty cache and goes straight to the network.
/// During development that meant several requests in quick succession and a rate limit that
/// outlived the session causing it — and a user updating the plugin hits the same thing.
///
/// The location is deliberately outside the plugin folder, which is deleted and recreated on
/// install.
/// </summary>
public sealed class FileUsageCacheStore(string? directory = null) : IUsageCacheStore
{
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = false };

    private readonly string _directory = directory ?? DefaultDirectory();

    public static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeDeck",
            "usage-cache");

    public UsageCacheState? Read(string key)
    {
        try
        {
            var path = PathFor(key);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UsageCacheState>(File.ReadAllText(path), Format)
                : null;
        }
        catch
        {
            // A cache that cannot be read is the same as no cache.
            return null;
        }
    }

    public void Write(string key, UsageCacheState state)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(PathFor(key), JsonSerializer.Serialize(state, Format));
        }
        catch
        {
            // Losing the cache costs one request after a restart, which is not worth failing
            // a render over.
        }
    }

    /// <summary>The key is a filesystem path, so it is hashed rather than escaped.</summary>
    private string PathFor(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToLowerInvariant()));
        return Path.Combine(_directory, Convert.ToHexString(hash)[..16] + ".json");
    }
}
