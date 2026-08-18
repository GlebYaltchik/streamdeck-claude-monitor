using System.Text.Json;
using ClaudeDeck.Core.Usage;

namespace ClaudeDeck.Core.Tests;

public class ClaudeUsageProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private const string Usable = """
        { "limits": [ { "kind": "session", "group": "session", "percent": 24, "severity": "normal" } ] }
        """;

    [Fact]
    public async Task A_file_without_a_token_asks_for_a_login()
    {
        var snapshot = await Provider(CredentialsResult.NoToken("no access token"), new FakeApi()).GetUsageAsync();

        Assert.Equal(UsageStatus.AuthRequired, snapshot.Status);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public async Task An_unreachable_credentials_file_is_an_outage_not_a_login_problem()
    {
        var api = new FakeApi();

        var snapshot = await Provider(CredentialsResult.Unreachable("share asleep"), api).GetUsageAsync();

        // Measured the hard way: a WSL share sleeping overnight made every key demand a
        // login the user could not act on, and discarded a good reading to do it.
        Assert.Equal(UsageStatus.Unavailable, snapshot.Status);
        Assert.Equal("share asleep", snapshot.Message);
        Assert.Empty(api.TokensUsed);
    }

    [Fact]
    public async Task The_stored_token_is_used_exactly_as_found()
    {
        var api = new FakeApi { Responses = { FetchResult.Ok(JsonDocument.Parse(Usable)) } };

        var snapshot = await Provider(Stored(), api).GetUsageAsync();

        Assert.Equal(UsageStatus.Ok, snapshot.Status);
        Assert.Equal(24, snapshot.Session!.Percent);
        Assert.Equal(["stored-access"], api.TokensUsed);
    }

    [Fact]
    public async Task A_rejected_token_is_reported_rather_than_renewed()
    {
        var api = new FakeApi { Responses = { new FetchResult(FetchOutcome.Unauthorized) } };

        var snapshot = await Provider(Stored(), api).GetUsageAsync();

        // Renewing would rotate the refresh token server-side and invalidate the one the
        // client holds. Logging the user out of Claude Code to draw a percentage is not a
        // trade this plugin gets to make.
        Assert.Equal(UsageStatus.AuthRequired, snapshot.Status);
        Assert.Single(api.TokensUsed);
    }

    [Fact]
    public async Task Rate_limiting_is_reported_as_itself_and_keeps_the_hint()
    {
        var api = new FakeApi
        {
            Responses =
            {
                new FetchResult(FetchOutcome.RateLimited, Message: "slow down", RetryAfter: TimeSpan.FromMinutes(2)),
            },
        };

        var snapshot = await Provider(Stored(), api).GetUsageAsync();

        Assert.Equal(UsageStatus.RateLimited, snapshot.Status);
        Assert.Equal("slow down", snapshot.Message);
        Assert.Equal(TimeSpan.FromMinutes(2), snapshot.RetryAfter);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable_rather_than_an_exception()
    {
        var api = new FakeApi { Responses = { new FetchResult(FetchOutcome.Failed, Message: "timed out") } };

        var snapshot = await Provider(Stored(), api).GetUsageAsync();

        Assert.Equal(UsageStatus.Unavailable, snapshot.Status);
    }

    private static ClaudeUsageProvider Provider(CredentialsResult credentials, IUsageApi api) =>
        new(new FakeCredentials(credentials), api, () => Now);

    private static CredentialsResult Stored() =>
        CredentialsResult.Ok(new ClaudeCredentials("stored-access"));

    private sealed class FakeCredentials(CredentialsResult result) : ICredentialsStore
    {
        public CredentialsResult Read() => result;
    }

    private sealed class FakeApi : IUsageApi
    {
        private int _next;

        public List<FetchResult> Responses { get; } = [];

        public List<string> TokensUsed { get; } = [];

        public Task<FetchResult> FetchUsageAsync(string accessToken, CancellationToken cancellationToken)
        {
            TokensUsed.Add(accessToken);
            return Task.FromResult(_next < Responses.Count
                ? Responses[_next++]
                : new FetchResult(FetchOutcome.Failed, Message: "no response queued"));
        }
    }
}
