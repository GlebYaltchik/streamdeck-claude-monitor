namespace ClaudeDeck.Core.Usage;

/// <summary>
/// Caches usage and backs off after failures.
///
/// Two jobs. The endpoint is polled far more often than it changes, so a short cache keeps
/// the traffic sane. And when it fails, hammering it makes things worse — a 429 is answered
/// by honouring the server's own retry hint, everything else by doubling the wait.
///
/// A transient failure does not blank the key: the last good snapshot is returned marked
/// stale, carrying the reason.
/// </summary>
public sealed class CachedUsageProvider(
    IUsageProvider inner,
    TimeSpan? refreshInterval = null,
    Func<DateTimeOffset>? clock = null,
    Action<string>? log = null,
    IUsageCacheStore? store = null,
    string? cacheKey = null) : IUsageProvider
{
    private static readonly TimeSpan DefaultRefresh = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MinimumBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The floor between two forced refreshes. Measured against the real endpoint: a second
    /// request 21 seconds after the first was already answered with a rate limit, so this is
    /// deliberately far longer than it feels like it needs to be.
    /// </summary>
    private static readonly TimeSpan MinimumForcedInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Fallback wait after a rate limit that carried no <c>retry-after</c>. The endpoint was
    /// observed to send the header, so this is the safety net rather than the usual path:
    /// being told to back off is a different failure from a dropped connection, and the
    /// generic backoff starting at 30 seconds is not enough on its own.
    /// </summary>
    private static readonly TimeSpan RateLimitedWait = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _refreshInterval = refreshInterval ?? DefaultRefresh;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private UsageSnapshot? _lastGood;
    private UsageSnapshot? _lastResult;
    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
    private TimeSpan _backoff = MinimumBackoff;
    private bool _restored;

    public async Task<UsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Restore();

            if (_clock() < _nextAttempt && _lastResult is not null)
            {
                return _lastResult;
            }

            _lastFetch = _clock();
            var snapshot = await inner.GetUsageAsync(cancellationToken);
            log?.Invoke($"usage fetched: {snapshot.Status}{(snapshot.Message is null ? "" : $" ({snapshot.Message})")}");

            Record(snapshot);
            return _lastResult!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Asks for the cooling period to be dropped so the next call reaches the endpoint.
    /// Ignored when the last fetch was too recent, so holding the refresh button does not
    /// turn into a burst of requests.
    /// </summary>
    public void Invalidate()
    {
        Restore();

        if (_clock() - _lastFetch < MinimumForcedInterval)
        {
            log?.Invoke("refresh ignored, last fetch was too recent");
            return;
        }

        _nextAttempt = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Picks up where the last run left off. Without this a restart is indistinguishable
    /// from a first run, so the cooling period is lost and the endpoint is hit immediately —
    /// which is how a few reinstalls in a row earn a rate limit that outlives them.
    /// </summary>
    private void Restore()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;

        if (store is null || cacheKey is null || store.Read(cacheKey) is not { } saved)
        {
            return;
        }

        _lastGood = saved.LastGood;
        _lastFetch = saved.LastFetch;
        _nextAttempt = saved.NextAttempt;
        _backoff = saved.Backoff < MinimumBackoff ? MinimumBackoff : saved.Backoff;

        // Restored numbers are as old as the file. Showing them as current would be a lie,
        // so anything past its refresh interval is marked for what it is.
        var shown = saved.LastShown ?? saved.LastGood;
        _lastResult = shown is { Status: UsageStatus.Ok } && _clock() - saved.LastFetch > _refreshInterval
            ? shown with { Stale = true }
            : shown;

        var wait = _nextAttempt - _clock();
        log?.Invoke($"usage cache restored, next attempt in {Math.Max(0, wait.TotalSeconds):F0}s");
    }

    private void Save()
    {
        if (store is not null && cacheKey is not null)
        {
            store.Write(cacheKey, new UsageCacheState(_lastGood, _lastFetch, _nextAttempt, _backoff, _lastResult));
        }
    }

    private void Record(UsageSnapshot snapshot)
    {
        var now = _clock();

        if (snapshot.Status == UsageStatus.Ok)
        {
            _lastGood = snapshot;
            _lastResult = snapshot;
            _backoff = MinimumBackoff;
            _nextAttempt = now + _refreshInterval;
            Save();
            return;
        }

        var wait = NextWait(snapshot);
        _nextAttempt = now + wait;
        _backoff = Double(_backoff);
        log?.Invoke($"usage unavailable, next attempt in {wait.TotalSeconds:F0}s");

        // Auth failures are about the credentials, not the connection: showing an old
        // percentage would hide the one thing the user has to act on.
        _lastResult = snapshot.Status != UsageStatus.AuthRequired && _lastGood is not null
            ? _lastGood with { Stale = true, Message = snapshot.Message }
            : snapshot;

        // The backoff is worth persisting too: a restart must not shorten a wait the server
        // asked for.
        Save();
    }

    /// <summary>
    /// The longer of what the server asked for and what our own backoff has escalated to.
    /// Honouring the hint alone is not enough: repeated limits kept arriving after waiting
    /// exactly as long as we were told, so consecutive failures still push the wait up.
    /// </summary>
    private TimeSpan NextWait(UsageSnapshot snapshot)
    {
        var floor = snapshot.Status == UsageStatus.RateLimited ? RateLimitedWait : _backoff;
        var wait = snapshot.RetryAfter ?? floor;
        return wait > _backoff ? wait : _backoff;
    }

    private static TimeSpan Double(TimeSpan value)
    {
        var doubled = value * 2;
        return doubled > MaximumBackoff ? MaximumBackoff : doubled;
    }
}
