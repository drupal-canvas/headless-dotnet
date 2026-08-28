using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace DrupalCanvas.Headless;

/// <summary>
/// PKCE pair binding assertion redemption to the app server (RFC 7636 shapes).
///
/// Renewal assertions reach the app relayed through the embedded page's script
/// context (host → postMessage → client → renew endpoint), so a script
/// injected into the app could intercept one. Drupal's grant therefore refuses
/// to redeem a renewal assertion unless the request also proves possession of
/// the running session: a <c>code_verifier</c> hashing to the
/// <c>code_challenge</c> the app server registered at the previous redemption.
/// The verifier never leaves the server — it lives in the httpOnly draft data
/// cookie — so an intercepted assertion is worthless on its own.
///
/// Every redemption registers a fresh challenge for the next one; the verifier
/// is stored alongside the session and rotated with it.
/// </summary>
public static class Pkce
{
    /// <summary>Generates a fresh, high-entropy code verifier.</summary>
    public static string GenerateCodeVerifier()
    {
        // 32 random bytes → 43 base64url characters, RFC 7636's minimum length.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>Computes the S256 code challenge for a verifier.</summary>
    public static string ComputeCodeChallenge(string verifier)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return Base64Url.EncodeToString(digest);
    }
}
