using System.Text.Json;

namespace ClaudeDeck.Core.Usage;

public enum FetchOutcome
{
    Ok,
    Unauthorized,
    RateLimited,
    Failed,
}

public sealed record FetchResult(
    FetchOutcome Outcome,
    JsonDocument? Body = null,
    string? Message = null,
    TimeSpan? RetryAfter = null)
{
    public static FetchResult Ok(JsonDocument body) => new(FetchOutcome.Ok, body);
}

/// <summary>
/// The two calls the usage feature makes. Separated from the provider so the pipeline can be
/// tested without a network or a real token.
/// </summary>
public interface IUsageApi
{
    Task<FetchResult> FetchUsageAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>Exchanges a refresh token for an access token, or null if it was rejected.</summary>
    Task<string?> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken);
}
