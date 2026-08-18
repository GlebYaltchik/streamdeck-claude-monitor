namespace ClaudeDeck.Core.Usage;

public interface IUsageProvider
{
    /// <summary>Always resolves to a snapshot. Failures are a status, never an exception.</summary>
    Task<UsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the credentials Claude Code wrote and asks the endpoint for the current usage.
///
/// It deliberately never refreshes the token, even when the stored expiry says it is stale.
/// Refreshing rotates the refresh token server-side, which would invalidate the one the
/// client is holding — a plugin that shows a percentage has no business logging the user out
/// of the tool it reports on. The client refreshes during normal use and rewrites the file;
/// we pick the new token up on the next read.
///
/// The stored expiry is not consulted either. It was observed to be zeroed while the file
/// still existed, so the server is the only trustworthy judge of whether a token works.
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
        var read = credentials.Read();

        if (read.Outcome == CredentialsOutcome.Unreachable)
        {
            // Not an auth problem: we could not get to the file. Reporting it as one would
            // ask the user to fix something that is not broken, and would throw away a
            // perfectly good last reading.
            return UsageSnapshot.Failure(
                UsageStatus.Unavailable,
                read.Error ?? "Credentials file unreadable.",
                now);
        }

        if (read.Credentials is not { } stored)
        {
            return UsageSnapshot.Failure(
                UsageStatus.AuthRequired,
                read.Error ?? "No Claude credentials found. Log in with Claude Code.",
                now);
        }

        return Interpret(await api.FetchUsageAsync(stored.AccessToken, cancellationToken), now);
    }

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
                return UsageSnapshot.Failure(
                    UsageStatus.AuthRequired,
                    "Claude session expired. Claude Code will renew it when you use it.",
                    now);

            case FetchOutcome.RateLimited:
                return UsageSnapshot.Failure(UsageStatus.RateLimited, result.Message ?? "Rate limited.", now)
                    with { RetryAfter = result.RetryAfter };

            default:
                return UsageSnapshot.Failure(UsageStatus.Unavailable, result.Message ?? "Usage unavailable.", now);
        }
    }
}
