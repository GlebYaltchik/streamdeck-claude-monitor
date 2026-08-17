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
    public async Task Missing_credentials_ask_for_a_login()
    {
        var snapshot = await Provider(credentials: null, new FakeApi()).GetUsageAsync();

        Assert.Equal(UsageStatus.AuthRequired, snapshot.Status);
        Assert.Empty(snapshot.Windows);
    }

    [Fact]
    public async Task A_valid_token_is_used_as_is()
    {
        var api = new FakeApi { Responses = { FetchResult.Ok(Parse(Usable)) } };

        var snapshot = await Provider(Fresh(), api).GetUsageAsync();

        Assert.Equal(UsageStatus.Ok, snapshot.Status);
        Assert.Equal(24, snapshot.Session!.Percent);
        Assert.Equal(0, api.RefreshCalls);
        Assert.Equal(["stored-access"], api.TokensUsed);
    }

    [Fact]
    public async Task An_expired_token_is_refreshed_before_the_request()
    {
        var api = new FakeApi { RefreshedToken = "renewed", Responses = { FetchResult.Ok(Parse(Usable)) } };

        var snapshot = await Provider(Expired(), api).GetUsageAsync();

        Assert.Equal(UsageStatus.Ok, snapshot.Status);
        Assert.Equal(1, api.RefreshCalls);
        Assert.Equal(["renewed"], api.TokensUsed);
    }

    [Fact]
    public async Task A_rejected_token_is_refreshed_and_retried_once()
    {
        var api = new FakeApi
        {
            RefreshedToken = "renewed",
            Responses =
            {
                new FetchResult(FetchOutcome.Unauthorized),
                FetchResult.Ok(Parse(Usable)),
            },
        };

        var snapshot = await Provider(Fresh(), api).GetUsageAsync();

        Assert.Equal(UsageStatus.Ok, snapshot.Status);
        Assert.Equal(1, api.RefreshCalls);
        Assert.Equal(["stored-access", "renewed"], api.TokensUsed);
    }

    [Fact]
    public async Task Rejection_after_the_retry_gives_up_rather_than_looping()
    {
        var api = new FakeApi
        {
            RefreshedToken = "renewed",
            Responses =
            {
                new FetchResult(FetchOutcome.Unauthorized),
                new FetchResult(FetchOutcome.Unauthorized),
            },
        };

        var snapshot = await Provider(Fresh(), api).GetUsageAsync();

        Assert.Equal(UsageStatus.AuthRequired, snapshot.Status);
        Assert.Equal(1, api.RefreshCalls);
    }

    [Fact]
    public async Task A_credential_without_a_refresh_token_cannot_recover()
    {
        var api = new FakeApi { Responses = { new FetchResult(FetchOutcome.Unauthorized) } };
        var credentials = new ClaudeCredentials("stored-access", RefreshToken: null, ExpiresAt: null);

        var snapshot = await Provider(credentials, api).GetUsageAsync();

        Assert.Equal(UsageStatus.AuthRequired, snapshot.Status);
        Assert.Equal(0, api.RefreshCalls);
    }

    [Fact]
    public async Task Rate_limiting_is_reported_as_itself()
    {
        var api = new FakeApi { Responses = { new FetchResult(FetchOutcome.RateLimited, Message: "slow down") } };

        var snapshot = await Provider(Fresh(), api).GetUsageAsync();

        Assert.Equal(UsageStatus.RateLimited, snapshot.Status);
        Assert.Equal("slow down", snapshot.Message);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable_rather_than_an_exception()
    {
        var api = new FakeApi { Responses = { new FetchResult(FetchOutcome.Failed, Message: "timed out") } };

        var snapshot = await Provider(Fresh(), api).GetUsageAsync();

        Assert.Equal(UsageStatus.Unavailable, snapshot.Status);
    }

    private static ClaudeUsageProvider Provider(ClaudeCredentials? credentials, IUsageApi api) =>
        new(new FakeCredentials(credentials), api, () => Now);

    private static ClaudeCredentials Fresh() =>
        new("stored-access", "stored-refresh", Now.AddHours(1));

    private static ClaudeCredentials Expired() =>
        new("stored-access", "stored-refresh", Now.AddSeconds(-1));

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    private sealed class FakeCredentials(ClaudeCredentials? credentials) : ICredentialsStore
    {
        public ClaudeCredentials? Read() => credentials;
    }

    private sealed class FakeApi : IUsageApi
    {
        private int _next;

        public List<FetchResult> Responses { get; } = [];

        public string? RefreshedToken { get; init; }

        public int RefreshCalls { get; private set; }

        public List<string> TokensUsed { get; } = [];

        public Task<FetchResult> FetchUsageAsync(string accessToken, CancellationToken cancellationToken)
        {
            TokensUsed.Add(accessToken);
            return Task.FromResult(_next < Responses.Count
                ? Responses[_next++]
                : new FetchResult(FetchOutcome.Failed, Message: "no response queued"));
        }

        public Task<string?> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            RefreshCalls++;
            return Task.FromResult(RefreshedToken);
        }
    }
}
