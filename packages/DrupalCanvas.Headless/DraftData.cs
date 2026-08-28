using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrupalCanvas.Headless;

/// <summary>Signed rendering context for an editor preview.</summary>
public sealed record DraftPreviewContext
{
    [JsonPropertyName("viewMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ViewMode { get; init; }

    [JsonPropertyName("pageVariant")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PageVariant { get; init; }
}

/// <summary>
/// The draft session, established by exchanging a signed preview assertion at
/// Drupal's token endpoint. It describes a session, not a previewed entity.
/// The JSON wire shape (stored in the draft data cookie) matches the
/// JavaScript SDK's <c>DraftData</c> exactly.
/// </summary>
public sealed record DraftData
{
    /// <summary>The session's entry point, from the assertion's claims.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>
    /// Session-wide revision policy the draft client applies to every fetch
    /// (JSON:API <c>resourceVersion</c> values, e.g. <c>rel:working-copy</c>).
    /// </summary>
    [JsonPropertyName("resourceVersion")]
    public required string ResourceVersion { get; init; }

    [JsonPropertyName("previewContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DraftPreviewContext? PreviewContext { get; init; }

    /// <summary>
    /// The Drupal user id of the editor the session is bound to, from the
    /// assertion's <c>sub</c> claim. Renewal is continuation, not activation:
    /// a renewal naming a different editor is refused.
    /// </summary>
    [JsonPropertyName("sub")]
    public required string Sub { get; init; }

    /// <summary>
    /// The absolute URL of Drupal's standalone renewal route, as seen by the
    /// editor's browser — a signed claim, minted from the request Drupal
    /// received.
    /// </summary>
    [JsonPropertyName("renewUrl")]
    public required string RenewUrl { get; init; }

    [JsonPropertyName("accessToken")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("tokenType")]
    public required string TokenType { get; init; }

    /// <summary>Unix epoch milliseconds after which the access token is invalid.</summary>
    [JsonPropertyName("tokenExpiresAt")]
    public required long TokenExpiresAt { get; init; }

    /// <summary>
    /// The PKCE verifier proving the next renewal comes from the app server.
    /// Lives in the httpOnly cookie, out of any script's reach; rotated on
    /// every redemption.
    /// </summary>
    [JsonPropertyName("codeVerifier")]
    public required string CodeVerifier { get; init; }

    /// <summary>
    /// How much earlier than <see cref="TokenExpiresAt"/> a session counts as
    /// expired, so nothing acts on a token that will be dead by the time a
    /// request reaches Drupal. Matches the client-side state machine's slack.
    /// </summary>
    public const long ExpirySlackMs = 5_000;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Parses and validates a serialized draft-data cookie value. Returns null
    /// for missing, malformed, or incomplete data — an unreadable session is
    /// treated as no session.
    /// </summary>
    public static DraftData? Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!TryGetString(root, "path", out var path)
                || !TryGetString(root, "resourceVersion", out var resourceVersion)
                || !TryGetString(root, "sub", out var sub)
                || !TryGetString(root, "renewUrl", out var renewUrl)
                || !TryGetString(root, "accessToken", out var accessToken)
                || !TryGetString(root, "tokenType", out var tokenType)
                || !TryGetString(root, "codeVerifier", out var codeVerifier))
            {
                return null;
            }

            if (!root.TryGetProperty("tokenExpiresAt", out var expiresAt)
                || expiresAt.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            DraftPreviewContext? previewContext = null;
            if (root.TryGetProperty("previewContext", out var context))
            {
                if (context.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
                string? viewMode = null;
                string? pageVariant = null;
                if (context.TryGetProperty("viewMode", out var viewModeValue))
                {
                    if (viewModeValue.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }
                    viewMode = viewModeValue.GetString();
                }
                if (context.TryGetProperty("pageVariant", out var pageVariantValue))
                {
                    if (pageVariantValue.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }
                    pageVariant = pageVariantValue.GetString();
                }
                previewContext = new DraftPreviewContext { ViewMode = viewMode, PageVariant = pageVariant };
            }

            return new DraftData
            {
                Path = path,
                ResourceVersion = resourceVersion,
                PreviewContext = previewContext,
                Sub = sub,
                RenewUrl = renewUrl,
                AccessToken = accessToken,
                TokenType = tokenType,
                TokenExpiresAt = (long)expiresAt.GetDouble(),
                CodeVerifier = codeVerifier,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Serializes the session for cookie storage; <see cref="Parse"/> reverses it.</summary>
    public string Serialize() => JsonSerializer.Serialize(this, SerializeOptions);

    /// <summary>
    /// Whether the session's access token has expired. An expired session is
    /// surfaced, never silently downgraded: pages fall back to what anonymous
    /// visitors can see while the draft indicator explains that the preview
    /// session ended.
    /// </summary>
    public bool IsExpired(long? nowUnixMs = null)
    {
        var now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return now >= TokenExpiresAt - ExpirySlackMs;
    }

    /// <summary>
    /// The exact editor origin carried by the redeemed assertion's signed
    /// renewal URL. Only HTTP(S) URLs without credentials are accepted.
    /// </summary>
    public static string? GetDraftEditorOrigin(string? renewUrl)
    {
        if (renewUrl is null || !Uri.TryCreate(renewUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }
        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }
        return uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = property.GetString()!;
        return true;
    }
}
