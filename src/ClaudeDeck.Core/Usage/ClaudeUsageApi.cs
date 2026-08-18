using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeDeck.Core.Usage;

/// <summary>
/// Reads the endpoint the Claude Code client itself uses for <c>/usage</c>.
///
/// Read-only by design. There is no token refresh here: see <see cref="ClaudeUsageProvider"/>
/// for why touching the OAuth token endpoint is not worth the risk.
///
/// The endpoint is unofficial and can change with any release, which is why every failure is
/// reported rather than thrown, and the caller degrades to "no data" instead of breaking.
/// </summary>
public sealed class ClaudeUsageApi : IUsageApi, IDisposable
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";

    private readonly HttpClient _http;

    public ClaudeUsageApi(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<FetchResult> FetchUsageAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            return new FetchResult(FetchOutcome.Failed, Message: Describe(ex));
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    return new FetchResult(FetchOutcome.Unauthorized, Message: "Access token was rejected.");
                case HttpStatusCode.TooManyRequests:
                    return new FetchResult(
                        FetchOutcome.RateLimited,
                        Message: "Usage endpoint asked us to back off.",
                        RetryAfter: response.Headers.RetryAfter?.Delta);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new FetchResult(FetchOutcome.Failed, Message: $"Usage request failed ({(int)response.StatusCode}).");
            }

            try
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return FetchResult.Ok(JsonDocument.Parse(content));
            }
            catch (Exception ex)
            {
                return new FetchResult(FetchOutcome.Failed, Message: Describe(ex));
            }
        }
    }

    /// <summary>Exception text only. Never the request, which carries the token.</summary>
    private static string Describe(Exception ex) =>
        ex is TaskCanceledException ? "Usage request timed out." : "Usage request failed.";

    public void Dispose() => _http.Dispose();
}
