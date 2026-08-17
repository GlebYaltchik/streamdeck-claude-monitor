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
    Func<DateTimeOffset>? clock = null) : IUsageProvider
{
    private static readonly TimeSpan DefaultRefresh = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MinimumBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(15);

    private readonly TimeSpan _refreshInterval = refreshInterval ?? DefaultRefresh;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private UsageSnapshot? _lastGood;
    private UsageSnapshot? _lastResult;
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
    private TimeSpan _backoff = MinimumBackoff;

    public async Task<UsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_clock() < _nextAttempt && _lastResult is not null)
            {
                return _lastResult;
            }

            var snapshot = await inner.GetUsageAsync(cancellationToken);
            Record(snapshot);
            return _lastResult!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops the cooling period so the next call goes to the endpoint.</summary>
    public void Invalidate()
    {
        _nextAttempt = DateTimeOffset.MinValue;
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
            return;
        }

        _nextAttempt = now + NextWait(snapshot);
        _backoff = Double(_backoff);

        // Auth failures are about the credentials, not the connection: showing an old
        // percentage would hide the one thing the user has to act on.
        _lastResult = snapshot.Status != UsageStatus.AuthRequired && _lastGood is not null
            ? _lastGood with { Stale = true, Message = snapshot.Message }
            : snapshot;
    }

    /// <summary>The server's own hint wins whenever it asks for longer than we would wait.</summary>
    private TimeSpan NextWait(UsageSnapshot snapshot) =>
        snapshot.RetryAfter is { } hinted && hinted > _backoff ? hinted : _backoff;

    private static TimeSpan Double(TimeSpan value)
    {
        var doubled = value * 2;
        return doubled > MaximumBackoff ? MaximumBackoff : doubled;
    }
}
