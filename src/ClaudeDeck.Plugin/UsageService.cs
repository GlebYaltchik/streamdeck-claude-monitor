using ClaudeDeck.Core.Usage;

namespace ClaudeDeck.Plugin;

/// <summary>
/// Owns one cached provider per credentials file, so several keys reading the same account
/// share a single request rather than each polling the endpoint.
/// </summary>
internal sealed class UsageService : IDisposable
{
    private readonly Dictionary<string, CachedUsageProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ClaudeUsageApi _api = new();
    private readonly Lock _gate = new();

    public Task<UsageSnapshot> GetAsync(string? configuredPath, CancellationToken cancellationToken = default)
    {
        return For(configuredPath).GetUsageAsync(cancellationToken);
    }

    public void Invalidate(string? configuredPath)
    {
        For(configuredPath).Invalidate();
    }

    private CachedUsageProvider For(string? configuredPath)
    {
        // Resolved once per call rather than cached: a distribution can start after the
        // plugin does, and the key should start working when it does.
        var path = CredentialsLocator.Locate(configuredPath) ?? FileCredentialsStore.DefaultPath();

        lock (_gate)
        {
            if (_providers.TryGetValue(path, out var existing))
            {
                return existing;
            }

            var provider = new CachedUsageProvider(new ClaudeUsageProvider(new FileCredentialsStore(path), _api));
            _providers[path] = provider;
            return provider;
        }
    }

    public void Dispose() => _api.Dispose();
}
