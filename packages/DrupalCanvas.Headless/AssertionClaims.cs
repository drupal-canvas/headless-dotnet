using System.Buffers.Text;
using System.Text.Json;

namespace DrupalCanvas.Headless;

public static class AssertionClaims
{
    /// <summary>
    /// Decodes the claim set of a JWT assertion without verifying the signature.
    ///
    /// Only safe to call on an assertion Drupal's token endpoint has just
    /// accepted: acceptance IS the verification (signature against Drupal's
    /// key, expiry, single-use jti — all checked server-side by the jwt-bearer
    /// grant). A tampered assertion never gets a token, so its claims are never
    /// read. The trust binding is exact string identity — decode the same
    /// string that was posted, nothing else.
    ///
    /// The one exception is documented pre-checks that can only refuse (the
    /// renewal identity pin, the components endpoint's origin gate).
    /// </summary>
    public static JsonElement? Decode(string assertion)
    {
        var parts = assertion.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = Base64Url.DecodeFromChars(parts[1]);
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>A string claim's value, or null when absent or not a string.</summary>
    public static string? GetString(JsonElement? claims, string name)
        => claims is { } element
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
