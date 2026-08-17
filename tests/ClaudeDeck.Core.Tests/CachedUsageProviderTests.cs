using ClaudeDeck.Core.Usage;

namespace ClaudeDeck.Core.Tests;

public class CachedUsageProviderTests
{
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Repeated_reads_inside_the_interval_hit_the_endpoint_once()
    {
        var (clock, inner, cache) = Build();
        inner.Results.Add(Ok(24));

        await cache.GetUsageAsync();
        await cache.GetUsageAsync();
        clock.Advance(Refresh - TimeSpan.FromSeconds(1));
        var snapshot = await cache.GetUsageAsync();

        Assert.Equal(1, inner.Calls);
        Assert.Equal(24, snapshot.Session!.Percent);
    }

    [Fact]
    public async Task The_endpoint_is_read_again_once_the_interval_passes()
    {
        var (clock, inner, cache) = Build();
        inner.Results.Add(Ok(24));
        inner.Results.Add(Ok(31));

        await cache.GetUsageAsync();
        clock.Advance(Refresh);
        var snapshot = await cache.GetUsageAsync();

        Assert.Equal(2, inner.Calls);
        Assert.Equal(31, snapshot.Session!.Percent);
    }

    [Fact]
    public async Task Forcing_a_refresh_skips_the_cooling_period()
    {
        var (clock, inner, cache) = Build();
        inner.Results.Add(Ok(24));
        inner.Results.Add(Ok(25));

        await cache.GetUsageAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        cache.Invalidate();
        await cache.GetUsageAsync();

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Holding_the_refresh_button_does_not_become_a_burst_of_requests()
    {
        var (clock, inner, cache) = Build();
        inner.Results.Add(Ok(24));

        await cache.GetUsageAsync();

        // A refresh button gets pressed repeatedly, and that is how a client earns a rate
        // limit rather than fresher data.
        for (var i = 0; i < 10; i++)
        {
            cache.Invalidate();
            await cache.GetUsageAsync();
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task A_refresh_asked_for_later_still_reaches_the_endpoint()
    {
        var (clock, inner, cache) = Build();
        inner.Results.Add(Ok(24));
        inner.Results.Add(Ok(25));

        await cache.GetUsageAsync();
        cache.Invalidate();
        await cache.GetUsageAsync();
        Assert.Equal(1, inner.Calls);

        clock.Advance(TimeSpan.FromMinutes(1));
        cache.Invalidate();
        var snapshot = await cache.GetUsageAsync();

        Assert.Equal(2, inner.Calls);
        Assert.Equal(25, snapshot.Session!.Percent);
    }

    [Fact]
    public async Task A_transient_failure_keeps_showing_the_last_good_value_as_stale()
    {
        var (clock, inner, cache) = Build();
        inner.Results.Add(Ok(24));
        inner.Results.Add(Failure(UsageStatus.Unavailable, "network"));

        await cache.GetUsageAsync();
        clock.Advance(Refresh);
        var snapshot = await cache.GetUsageAsync();

        Assert.Equal(UsageStatus.Ok, snapshot.Status);
        Assert.Equal(24, snapshot.Session!.Percent);
        Assert.True(snapshot.Stale);
        Assert.Equal("network", snapshot.Message);
    }

    [Fact]
    public async Task An_auth_failure_replaces_the_value_rather_than_hiding_behind_it()
    {
        var (clock, inner, cache) = Build();
        inner.Results.Add(Ok(24));
        inner.Results.Add(Failure(UsageStatus.AuthRequired, "log in"));

        await cache.GetUsageAsync();
        clock.Advance(Refresh);
        var snapshot = await cache.GetUsageAsync();

        // Showing a stale percentage would hide the one thing the user has to act on.
        Assert.Equal(UsageStatus.AuthRequired, snapshot.Status);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public async Task Failures_back_off_instead_of_retrying_every_call()
    {
        var (clock, inner, cache) = Build();
        inner.Results.Add(Failure(UsageStatus.Unavailable, "network"));

        await cache.GetUsageAsync();
        await cache.GetUsageAsync();
        clock.Advance(TimeSpan.FromSeconds(29));
        await cache.GetUsageAsync();

        Assert.Equal(1, inner.Calls);

        clock.Advance(TimeSpan.FromSeconds(2));
        inner.Results.Add(Failure(UsageStatus.Unavailable, "network"));
        await cache.GetUsageAsync();

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task The_wait_grows_with_each_consecutive_failure()
    {
        var (clock, inner, cache) = Build();
        for (var i = 0; i < 3; i++)
        {
            inner.Results.Add(Failure(UsageStatus.Unavailable, "network"));
        }

        await cache.GetUsageAsync();
        clock.Advance(TimeSpan.FromSeconds(30));
        await cache.GetUsageAsync();
        Assert.Equal(2, inner.Calls);

        // The second failure must wait longer than the first did.
        clock.Advance(TimeSpan.FromSeconds(31));
        await cache.GetUsageAsync();
        Assert.Equal(2, inner.Calls);

        clock.Advance(TimeSpan.FromSeconds(30));
        await cache.GetUsageAsync();
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task The_servers_retry_hint_wins_when_it_asks_for_longer()
    {
        var (clock, inner, cache) = Build();
        inner.Results.Add(Failure(UsageStatus.RateLimited, "slow down") with { RetryAfter = TimeSpan.FromMinutes(5) });

        await cache.GetUsageAsync();
        clock.Advance(TimeSpan.FromMinutes(4));
        await cache.GetUsageAsync();

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task A_rate_limit_without_a_hint_waits_far_longer_than_a_network_failure()
    {
        var (clock, inner, cache) = Build();
        inner.Results.Add(Failure(UsageStatus.RateLimited, "slow down"));

        await cache.GetUsageAsync();

        // Measured against the real endpoint: the generic 30-second backoff walked straight
        // into a second rate limit. Being told to back off is not a flaky connection.
        clock.Advance(TimeSpan.FromMinutes(4));
        await cache.GetUsageAsync();
        Assert.Equal(1, inner.Calls);

        clock.Advance(TimeSpan.FromMinutes(2));
        await cache.GetUsageAsync();
        Assert.Equal(2, inner.Calls);
    }

    private static (TestClock, FakeProvider, CachedUsageProvider) Build()
    {
        var clock = new TestClock();
        var inner = new FakeProvider();
        return (clock, inner, new CachedUsageProvider(inner, Refresh, () => clock.Now));
    }

    private static UsageSnapshot Ok(int percent) => new(
        UsageStatus.Ok,
        [new UsageWindow(UsageSnapshot.SessionGroup, "session", percent, "normal", null, true)],
        DateTimeOffset.UnixEpoch);

    private static UsageSnapshot Failure(UsageStatus status, string message) =>
        UsageSnapshot.Failure(status, message, DateTimeOffset.UnixEpoch);

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
