namespace DrupalCanvas.Headless;

/// <summary>The outcome of verifying an assertion by redemption.</summary>
/// <param name="Ok">Whether the assertion verified.</param>
/// <param name="Status">401, 403, or 502 when it did not.</param>
public sealed record AssertionVerificationResult(bool Ok, int Status = 0, string Message = "");

public static class AssertionVerification
{
    /// <summary>
    /// Verifies that a request comes from the embedding Drupal Canvas instance,
    /// by proof-by-redemption: the assertion is presented at Drupal's own token
    /// endpoint, and acceptance there proves it was minted by that Drupal, for
    /// a user holding the preview permission — signature, 60 s expiry, and
    /// single-use jti are all enforced on Drupal's side. The app needs no key
    /// material and no shared secret; the assertion is the credential.
    ///
    /// The verification is stateless and the minted access token is a
    /// byproduct: discarded here, never returned, logged, or stored.
    /// Assertions are single-use, so every call must present a freshly minted
    /// one — a replay fails the exchange and verifies as 401.
    /// </summary>
    public static async Task<AssertionVerificationResult> VerifyByRedemptionAsync(
        HttpClient httpClient,
        string assertion,
        DraftConfig config,
        CancellationToken cancellationToken = default)
    {
        var exchange = await TokenExchange
            .ExchangeAssertionAsync(httpClient, assertion, config, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (exchange.Ok)
        {
            return new AssertionVerificationResult(true);
        }

        if (exchange.Kind == ExchangeFailureKind.Network)
        {
            return new AssertionVerificationResult(false, 502, "Could not reach Drupal to verify the assertion.");
        }

        // OAuth refusals (invalid_grant, invalid_request — upstream 400/401)
        // mean the credential did not verify: 401 for the caller. An upstream
        // 403 passes through; anything else is an upstream contract violation,
        // not a caller error. The message strings come from Drupal itself, so
        // forwarding them to a caller that must hold Drupal editing rights
        // leaks nothing.
        return exchange.Status switch
        {
            400 or 401 => new AssertionVerificationResult(false, 401, exchange.Message),
            403 => new AssertionVerificationResult(false, 403, exchange.Message),
            _ => new AssertionVerificationResult(
                false, 502, "Unexpected response from Drupal while verifying the assertion."),
        };
    }
}
