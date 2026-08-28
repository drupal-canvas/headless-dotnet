using System.Text.Json;

namespace DrupalCanvas.Headless;

/// <summary>Where an assertion exchange failed.</summary>
public enum ExchangeFailureKind
{
    /// <summary>Drupal was unreachable.</summary>
    Network,

    /// <summary>Drupal refused the exchange.</summary>
    Upstream,
}

/// <summary>
/// The raw outcome of presenting an assertion at Drupal's token endpoint.
/// Framework-free by design: the draft flows dress failures as HTTP responses,
/// the assertion verifier maps them to status codes — both from this one
/// result shape.
/// </summary>
public sealed record AssertionExchangeResult
{
    public required bool Ok { get; init; }

    public string? TokenType { get; init; }

    public string? AccessToken { get; init; }

    /// <summary>Token lifetime in seconds, as reported by the token endpoint.</summary>
    public long ExpiresIn { get; init; }

    public ExchangeFailureKind Kind { get; init; }

    /// <summary>The upstream HTTP status; null for network failures.</summary>
    public int? Status { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <summary>Optional PKCE parameters of one assertion exchange.</summary>
/// <param name="CodeChallenge">
/// The S256 challenge to register for the session's next renewal exchange;
/// Drupal stores it against the session. Optional on Drupal's side — an
/// exchange that registers none simply cannot renew in place.
/// </param>
/// <param name="CodeVerifier">
/// The verifier matching the challenge registered at the previous redemption.
/// Required by Drupal for renewal assertions, which transit the embedded
/// page's script context; activation assertions carry no proof requirement.
/// </param>
public sealed record AssertionExchangePkce(string? CodeChallenge = null, string? CodeVerifier = null);

public static class TokenExchange
{
    /// <summary>
    /// Exchanges a preview assertion at Drupal's standard token endpoint (RFC
    /// 7523 jwt-bearer grant). Drupal verifies the signature, expiry, and
    /// single-use jti, and answers with an access token bound to the editor who
    /// initiated the preview. No client secret is involved — the consumer is a
    /// public client and the assertion itself is the credential.
    /// </summary>
    public static async Task<AssertionExchangeResult> ExchangeAssertionAsync(
        HttpClient httpClient,
        string assertion,
        DraftConfig config,
        AssertionExchangePkce? pkce = null,
        CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", CanvasConstants.JwtBearerGrantType),
            new("assertion", assertion),
            new("client_id", CanvasConstants.CanvasHeadlessClientId),
        };
        if (pkce?.CodeChallenge is { } challenge)
        {
            form.Add(new("code_challenge", challenge));
            form.Add(new("code_challenge_method", "S256"));
        }
        if (pkce?.CodeVerifier is { } verifier)
        {
            form.Add(new("code_verifier", verifier));
        }

        HttpResponseMessage response;
        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl}/oauth/token")
            {
                Content = new FormUrlEncodedContent(form),
            };
            request.Headers.Accept.ParseAdd("application/json");
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new AssertionExchangeResult
            {
                Ok = false,
                Kind = ExchangeFailureKind.Network,
                Message = "Could not reach Drupal to redeem the preview assertion.",
            };
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var message = string.Empty;
                try
                {
                    using var document = JsonDocument.Parse(body);
                    var parts = new[] { "error_description", "hint" }
                        .Select(name => document.RootElement.TryGetProperty(name, out var value)
                            && value.ValueKind == JsonValueKind.String
                            ? value.GetString()
                            : null)
                        .Where(part => !string.IsNullOrEmpty(part));
                    message = string.Join(' ', parts);
                }
                catch (JsonException)
                {
                    // A non-JSON refusal falls through to the generic message.
                }

                return new AssertionExchangeResult
                {
                    Ok = false,
                    Kind = ExchangeFailureKind.Upstream,
                    Status = (int)response.StatusCode,
                    Message = message.Length > 0 ? message : "Invalid preview assertion.",
                };
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                return new AssertionExchangeResult
                {
                    Ok = true,
                    TokenType = root.GetProperty("token_type").GetString(),
                    AccessToken = root.GetProperty("access_token").GetString(),
                    ExpiresIn = (long)root.GetProperty("expires_in").GetDouble(),
                };
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return new AssertionExchangeResult
                {
                    Ok = false,
                    Kind = ExchangeFailureKind.Upstream,
                    Status = (int)response.StatusCode,
                    Message = "Unexpected token response from Drupal.",
                };
            }
        }
    }
}
