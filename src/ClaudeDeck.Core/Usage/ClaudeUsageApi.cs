using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClaudeDeck.Core.Usage;

/// <summary>
/// Talks to the endpoint the Claude Code client itself uses for <c>/usage</c>.
///
/// This endpoint is unofficial and can change with any release, which is why every failure
/// is reported rather than thrown, and why the caller degrades to "no data" instead of
/// breaking.
/// </summary>
public sealed class ClaudeUsageApi : IUsageApi, IDisposable
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private const string TokenEndpoint = "https://platform.claude.com/v1/oauth/token";

    /// <summary>
    /// The public client identifier embedded in the distributed Claude Code binary. It
    /// identifies the client, not the user, and is not a secret.
    /// </summary>
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

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
                    return new FetchResult(FetchOutcome.Unauthorized, Message: "Access token was rejected.");
                case HttpStatusCode.Forbidden:
                    return new FetchResult(FetchOutcome.Unauthorized, Message: "Usage request was forbidden.");
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

    public async Task<string?> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                TokenEndpoint,
                new { grant_type = "refresh_token", refresh_token = refreshToken, client_id = ClientId },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("access_token", out var token) &&
                   token.ValueKind == JsonValueKind.String
                ? token.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Exception text only. Never the request, which carries the token.</summary>
    private static string Describe(Exception ex) =>
        ex is TaskCanceledException ? "Usage request timed out." : "Usage request failed.";

    public void Dispose() => _http.Dispose();
}
