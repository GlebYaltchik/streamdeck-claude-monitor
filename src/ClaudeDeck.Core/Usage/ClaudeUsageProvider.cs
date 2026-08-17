namespace ClaudeDeck.Core.Usage;

public interface IUsageProvider
{
    /// <summary>Always resolves to a snapshot. Failures are a status, never an exception.</summary>
    Task<UsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Credentials, refresh when stale, fetch, one refresh-and-retry on rejection, normalize.
/// </summary>
public sealed class ClaudeUsageProvider(
    ICredentialsStore credentials,
    IUsageApi api,
    Func<DateTimeOffset>? clock = null) : IUsageProvider
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<UsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock();

        if (credentials.Read() is not { } stored)
        {
            return UsageSnapshot.Failure(
                UsageStatus.AuthRequired,
                "No Claude credentials found. Log in with Claude Code.",
                now);
        }

        var accessToken = stored.AccessToken;
        if (stored.IsExpired(now))
        {
            if (await Refresh(stored, cancellationToken) is not { } refreshed)
            {
                return Expired(now);
            }

            accessToken = refreshed;
        }

        var result = await api.FetchUsageAsync(accessToken, cancellationToken);

        if (result.Outcome == FetchOutcome.Unauthorized)
        {
            // The stored expiry can be wrong or absent, so a rejection is worth one refresh.
            if (await Refresh(stored, cancellationToken) is not { } refreshed)
            {
                return Expired(now);
            }

            result = await api.FetchUsageAsync(refreshed, cancellationToken);
        }

        return Interpret(result, now);
    }

    private async Task<string?> Refresh(ClaudeCredentials stored, CancellationToken cancellationToken) =>
        stored.RefreshToken is { } token ? await api.RefreshAccessTokenAsync(token, cancellationToken) : null;

    private static UsageSnapshot Expired(DateTimeOffset now) =>
        UsageSnapshot.Failure(UsageStatus.AuthRequired, "Claude session expired. Log in with Claude Code.", now);

    private static UsageSnapshot Interpret(FetchResult result, DateTimeOffset now)
    {
        switch (result.Outcome)
        {
            case FetchOutcome.Ok when result.Body is not null:
                using (result.Body)
                {
                    return UsageNormalizer.Normalize(result.Body.RootElement, now);
                }

            case FetchOutcome.Unauthorized:
                return Expired(now);

            case FetchOutcome.RateLimited:
                return UsageSnapshot.Failure(UsageStatus.RateLimited, result.Message ?? "Rate limited.", now)
                    with { RetryAfter = result.RetryAfter };

            default:
                return UsageSnapshot.Failure(UsageStatus.Unavailable, result.Message ?? "Usage unavailable.", now);
        }
    }
}
