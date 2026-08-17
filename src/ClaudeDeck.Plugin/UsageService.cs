using ClaudeDeck.Core.Usage;

namespace ClaudeDeck.Plugin;

/// <summary>
/// Owns one cached provider per credentials file, so several controls reading the same
/// account share a single request rather than each polling the endpoint.
/// </summary>
internal sealed class UsageService : IDisposable
{
    /// <summary>
    /// How long a resolved credentials path is trusted. Resolving means touching the
    /// filesystem, including WSL shares over 9p, and controls redraw far more often than
    /// distributions come and go.
    /// </summary>
    private static readonly TimeSpan PathLifetime = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, CachedUsageProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResolvedPath> _paths = new(StringComparer.Ordinal);
    private readonly ClaudeUsageApi _api = new();
    private readonly FileUsageCacheStore _cache = new();
    private readonly Lock _gate = new();

    public Task<UsageSnapshot> GetAsync(string? configuredPath, CancellationToken cancellationToken = default)
    {
        return For(configuredPath).GetUsageAsync(cancellationToken);
    }

    private CachedUsageProvider For(string? configuredPath)
    {
        var path = Resolve(configuredPath);

        lock (_gate)
        {
            if (_providers.TryGetValue(path, out var existing))
            {
                return existing;
            }

            PluginLog.Write($"usage provider for {path}");
            var provider = new CachedUsageProvider(
                new ClaudeUsageProvider(new FileCredentialsStore(path), _api),
                log: PluginLog.Write,
                store: _cache,
                cacheKey: path);

            _providers[path] = provider;
            return provider;
        }
    }

    private string Resolve(string? configuredPath)
    {
        var key = configuredPath ?? "";
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (_paths.TryGetValue(key, out var cached) && now - cached.At < PathLifetime)
            {
                return cached.Path;
            }
        }

        // Deliberately outside the lock: locating can touch a WSL share, which can be slow.
        var located = CredentialsLocator.Locate(configuredPath) ?? FileCredentialsStore.DefaultPath();

        lock (_gate)
        {
            _paths[key] = new ResolvedPath(located, now);
        }

        return located;
    }

    public void Dispose() => _api.Dispose();

    private sealed record ResolvedPath(string Path, DateTimeOffset At);
}
