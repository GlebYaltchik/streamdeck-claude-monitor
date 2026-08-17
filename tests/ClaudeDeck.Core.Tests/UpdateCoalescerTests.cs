using ClaudeDeck.Core;

namespace ClaudeDeck.Core.Tests;

public class UpdateCoalescerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task A_burst_of_submissions_is_limited_to_the_interval()
    {
        var clock = new TestClock();
        var sent = new List<string>();
        var coalescer = Create(clock, sent);

        // One dial spin measured 116 events in a few seconds. Simulate that shape: an event
        // every 10 ms for a second.
        for (var i = 0; i < 100; i++)
        {
            coalescer.Submit("dial", $"value {i}");
            await coalescer.FlushDueAsync();
            clock.Advance(TimeSpan.FromMilliseconds(10));
        }

        Assert.InRange(sent.Count, 1, 5);
    }

    [Fact]
    public async Task The_latest_value_always_wins()
    {
        var clock = new TestClock();
        var sent = new List<string>();
        var coalescer = Create(clock, sent);

        coalescer.Submit("key", "stale");
        coalescer.Submit("key", "fresh");
        await coalescer.FlushDueAsync();

        Assert.Equal(["fresh"], sent);
    }

    [Fact]
    public async Task Nothing_is_lost_when_submissions_stop()
    {
        var clock = new TestClock();
        var sent = new List<string>();
        var coalescer = Create(clock, sent);

        coalescer.Submit("key", "first");
        await coalescer.FlushDueAsync();

        coalescer.Submit("key", "second");
        await coalescer.FlushDueAsync();
        Assert.Equal(["first"], sent);

        clock.Advance(Interval);
        await coalescer.FlushDueAsync();

        Assert.Equal(["first", "second"], sent);
    }

    [Fact]
    public async Task Controls_are_limited_independently()
    {
        var clock = new TestClock();
        var sent = new List<string>();
        var coalescer = Create(clock, sent);

        coalescer.Submit("one", "a");
        coalescer.Submit("two", "b");
        await coalescer.FlushDueAsync();

        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public async Task Forgetting_a_control_drops_its_pending_update()
    {
        var clock = new TestClock();
        var sent = new List<string>();
        var coalescer = Create(clock, sent);

        coalescer.Submit("key", "queued");
        coalescer.Forget("key");
        await coalescer.FlushDueAsync();

        Assert.Empty(sent);
    }

    private static UpdateCoalescer<string> Create(TestClock clock, List<string> sent)
    {
        return new UpdateCoalescer<string>(
            Interval,
            (_, value) =>
            {
                sent.Add(value);
                return Task.CompletedTask;
            },
            () => clock.Now);
    }

    private sealed class TestClock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan amount) => Now += amount;
    }
}
