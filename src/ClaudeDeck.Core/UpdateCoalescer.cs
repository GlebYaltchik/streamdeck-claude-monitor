namespace ClaudeDeck.Core;

/// <summary>
/// Rate limits updates per key while never losing the latest value.
///
/// Measured need: one spin of a Stream Deck dial produced 116 events in a few seconds, and
/// answering each one saturates the socket. Submissions that arrive faster than the interval
/// overwrite each other, so a key always converges on its current state without flooding.
/// </summary>
public sealed class UpdateCoalescer<T>
{
    private readonly TimeSpan _minimumInterval;
    private readonly Func<string, T, Task> _send;
    private readonly Func<DateTimeOffset> _clock;

    private readonly Dictionary<string, T> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastSent = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public UpdateCoalescer(TimeSpan minimumInterval, Func<string, T, Task> send, Func<DateTimeOffset>? clock = null)
    {
        _minimumInterval = minimumInterval;
        _send = send;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public void Submit(string key, T value)
    {
        lock (_gate)
        {
            _pending[key] = value;
        }
    }

    /// <summary>
    /// Sends every pending update whose key is no longer within its cooling period.
    /// </summary>
    public async Task FlushDueAsync()
    {
        List<KeyValuePair<string, T>> due;
        var now = _clock();

        lock (_gate)
        {
            due = _pending
                .Where(entry => !_lastSent.TryGetValue(entry.Key, out var sent) || now - sent >= _minimumInterval)
                .ToList();

            foreach (var entry in due)
            {
                _pending.Remove(entry.Key);
                _lastSent[entry.Key] = now;
            }
        }

        foreach (var entry in due)
        {
            await _send(entry.Key, entry.Value);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var tick = TimeSpan.FromMilliseconds(Math.Max(10, _minimumInterval.TotalMilliseconds / 5));

        while (!cancellationToken.IsCancellationRequested)
        {
            await FlushDueAsync();

            try
            {
                await Task.Delay(tick, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Forget(string key)
    {
        lock (_gate)
        {
            _pending.Remove(key);
            _lastSent.Remove(key);
        }
    }
}
