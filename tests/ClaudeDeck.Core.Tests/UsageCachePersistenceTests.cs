using ClaudeDeck.Core.Usage;

namespace ClaudeDeck.Core.Tests;

/// <summary>
/// A restart must not look like a first run. Every reinstall used to start with an empty
/// cache and go straight to the network, which is how a handful of restarts earned a rate
/// limit that outlived them.
/// </summary>
public class UsageCachePersistenceTests
{
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task A_restart_inside_the_interval_serves_the_saved_value_without_asking()
    {
        var clock = new TestClock();
        var store = new MemoryStore();

        var first = Build(clock, store, out var firstInner);
        firstInner.Results.Add(Ok(24));
        await first.GetUsageAsync();

        clock.Advance(TimeSpan.FromSeconds(30));

        var second = Build(clock, store, out var secondInner);
        var snapshot = await second.GetUsageAsync();

        Assert.Equal(0, secondInner.Calls);
        Assert.Equal(24, snapshot.Session!.Percent);
        Assert.False(snapshot.Stale);
    }

    [Fact]
    public async Task A_restart_after_the_interval_refreshes()
    {
        var clock = new TestClock();
        var store = new MemoryStore();

        var first = Build(clock, store, out var firstInner);
        firstInner.Results.Add(Ok(24));
        await first.GetUsageAsync();

        clock.Advance(Refresh + TimeSpan.FromSeconds(1));

        var second = Build(clock, store, out var secondInner);
        secondInner.Results.Add(Ok(31));
        var snapshot = await second.GetUsageAsync();

        Assert.Equal(1, secondInner.Calls);
        Assert.Equal(31, snapshot.Session!.Percent);
    }

    [Fact]
    public async Task A_wait_the_server_asked_for_survives_the_restart()
    {
        var clock = new TestClock();
        var store = new MemoryStore();

        var first = Build(clock, store, out var firstInner);
        firstInner.Results.Add(Failure(UsageStatus.RateLimited, "slow down"));
        await first.GetUsageAsync();

        // Restarting must not shorten the penalty, which is exactly what an in-memory cache
        // did every time the plugin was reinstalled.
        clock.Advance(TimeSpan.FromMinutes(1));

        var second = Build(clock, store, out var secondInner);
        await second.GetUsageAsync();

        Assert.Equal(0, secondInner.Calls);
    }

    [Fact]
    public async Task Restored_values_past_their_interval_are_shown_as_stale()
    {
        var clock = new TestClock();
        var store = new MemoryStore();

        var first = Build(clock, store, out var firstInner);
        firstInner.Results.Add(Ok(24));
        await first.GetUsageAsync();

        clock.Advance(TimeSpan.FromMinutes(10));

        var second = Build(clock, store, out var secondInner);
        secondInner.Results.Add(Failure(UsageStatus.Unavailable, "network"));
        var snapshot = await second.GetUsageAsync();

        Assert.Equal(24, snapshot.Session!.Percent);
        Assert.True(snapshot.Stale);
    }

    [Fact]
    public async Task An_unreadable_cache_is_the_same_as_no_cache()
    {
        var clock = new TestClock();
        var store = new BrokenStore();

        var provider = Build(clock, store, out var inner);
        inner.Results.Add(Ok(24));
        var snapshot = await provider.GetUsageAsync();

        Assert.Equal(1, inner.Calls);
        Assert.Equal(24, snapshot.Session!.Percent);
    }

    private static CachedUsageProvider Build(TestClock clock, IUsageCacheStore store, out FakeProvider inner)
    {
        inner = new FakeProvider();
        return new CachedUsageProvider(inner, Refresh, () => clock.Now, store: store, cacheKey: "account");
    }

    private static UsageSnapshot Ok(int percent) => new(
        UsageStatus.Ok,
        [new UsageWindow(UsageSnapshot.SessionGroup, "session", percent, "normal", null, true)],
        DateTimeOffset.UnixEpoch);

    private static UsageSnapshot Failure(UsageStatus status, string message) =>
        UsageSnapshot.Failure(status, message, DateTimeOffset.UnixEpoch);

    private sealed class MemoryStore : IUsageCacheStore
    {
        private readonly Dictionary<string, UsageCacheState> _states = new(StringComparer.Ordinal);

        public UsageCacheState? Read(string key) => _states.GetValueOrDefault(key);

        public void Write(string key, UsageCacheState state) => _states[key] = state;
    }

    private sealed class BrokenStore : IUsageCacheStore
    {
        public UsageCacheState? Read(string key) => null;

        public void Write(string key, UsageCacheState state)
        {
        }
    }

    private sealed class FakeProvider : IUsageProvider
    {
        public List<UsageSnapshot> Results { get; } = [];

        public int Calls { get; private set; }

        public Task<UsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default)
        {
            var index = Calls++;
            return Task.FromResult(index < Results.Count ? Results[index] : Results[^1]);
        }
    }

    private sealed class TestClock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan amount) => Now += amount;
    }
}
