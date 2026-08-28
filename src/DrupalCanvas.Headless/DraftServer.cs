using System.Text.Json;

namespace DrupalCanvas.Headless;

/// <summary>
/// The result of redeeming an assertion at Drupal's token endpoint: the
/// established draft session, or the error response to answer with.
/// </summary>
public sealed record RedemptionResult
{
    public DraftData? DraftData { get; init; }

    public FlowResponse? Error { get; init; }

    public bool Ok => Error is null;
}

/// <summary>
/// The framework-agnostic draft server: the activation, renewal, and exit
/// flows, and the data accessors app code needs. A C# port of the JavaScript
/// SDK's <c>createDraftServer()</c>; the design records live in the Drupal
/// Canvas repository (docs/adr/0014…0016 and
/// modules/canvas_headless/docs/headless-preview-auth.md).
///
/// One instance serves one request's adapter — all state lives in the
/// request's cookies, reached through the adapter.
/// </summary>
public sealed class DraftServer
{
    private readonly IDraftServerAdapter _adapter;
    private readonly HttpClient _httpClient;
    private readonly Func<DraftConfig> _getConfig;

    public DraftServer(IDraftServerAdapter adapter, HttpClient httpClient, Func<DraftConfig>? config = null)
    {
        _adapter = adapter;
        _httpClient = httpClient;
        // Resolved lazily on every call, never at construction, so building an
        // app without the environment set does not throw at startup.
        _getConfig = config ?? (() => DraftConfig.Resolve());
    }

    public DraftConfig GetConfig() => _getConfig();

    /// <summary>
    /// The draft session for the current request, or null when draft mode is
    /// off or the data cookie is missing/corrupt.
    /// </summary>
    public async Task<DraftData?> GetDraftDataAsync()
    {
        if (!await _adapter.IsDraftFlagEnabledAsync().ConfigureAwait(false))
        {
            return null;
        }
        return DraftData.Parse(
            await _adapter.GetCookieAsync(CanvasConstants.DraftDataCookieName).ConfigureAwait(false));
    }

    /// <summary>
    /// A site-relative path: exactly one leading slash. Rejects
    /// protocol-relative forms (<c>//host</c>) and backslash tricks, mirroring
    /// the check Drupal's renewal endpoints apply before minting. Assertions
    /// are Drupal-signed, so a malformed path should never arrive — this is
    /// the app-side backstop for the same invariant, since the path ends up in
    /// a redirect.
    /// </summary>
    private static bool IsSiteRelativePath(string path)
        => path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\');

    /// <summary>
    /// Exchanges a preview assertion for a draft session (RFC 7523 jwt-bearer
    /// grant at Drupal's standard token endpoint).
    ///
    /// The session's entry path and resource version policy are read from the
    /// assertion's own claims, which is safe exactly because the token
    /// endpoint accepted this exact string: a tampered assertion never gets a
    /// token, so its claims are never used.
    ///
    /// Every exchange registers a fresh PKCE challenge with Drupal and stores
    /// the matching verifier in the session; a renewal exchange must present
    /// the previous verifier or Drupal refuses it (see <see cref="Pkce"/>).
    /// </summary>
    public static async Task<RedemptionResult> RedeemAssertionAsync(
        HttpClient httpClient,
        string assertion,
        DraftConfig config,
        string? previousVerifier = null,
        CancellationToken cancellationToken = default)
    {
        var nextVerifier = Pkce.GenerateCodeVerifier();
        var exchange = await TokenExchange.ExchangeAssertionAsync(
            httpClient,
            assertion,
            config,
            new AssertionExchangePkce(Pkce.ComputeCodeChallenge(nextVerifier), previousVerifier),
            cancellationToken).ConfigureAwait(false);

        if (!exchange.Ok)
        {
            return new RedemptionResult
            {
                Error = FlowResponse.Text(
                    exchange.Kind == ExchangeFailureKind.Network ? 502 : exchange.Status!.Value,
                    exchange.Message),
            };
        }

        // Drupal accepted this exact assertion string, so its claims are trusted.
        var claims = AssertionClaims.Decode(assertion);
        var path = AssertionClaims.GetString(claims, "path");
        var resourceVersion = AssertionClaims.GetString(claims, "resourceVersion");
        var sub = AssertionClaims.GetString(claims, "sub");
        var renewUrl = AssertionClaims.GetString(claims, "renewUrl");

        DraftPreviewContext? previewContext = null;
        if (claims is { } claimsElement
            && claimsElement.TryGetProperty("previewContext", out var rawContext)
            && rawContext.ValueKind == JsonValueKind.Object)
        {
            var viewMode = AssertionClaims.GetString(rawContext, "viewMode");
            var pageVariant = AssertionClaims.GetString(rawContext, "pageVariant");
            var viewModeInvalid = rawContext.TryGetProperty("viewMode", out var viewModeValue)
                && viewModeValue.ValueKind != JsonValueKind.String;
            var pageVariantInvalid = rawContext.TryGetProperty("pageVariant", out var pageVariantValue)
                && pageVariantValue.ValueKind != JsonValueKind.String;
            if (!viewModeInvalid && !pageVariantInvalid)
            {
                previewContext = new DraftPreviewContext { ViewMode = viewMode, PageVariant = pageVariant };
            }
        }

        var renewUrlValid = renewUrl is not null
            && (renewUrl.StartsWith("http://", StringComparison.Ordinal)
                || renewUrl.StartsWith("https://", StringComparison.Ordinal));
        if (path is null
            || !IsSiteRelativePath(path)
            || string.IsNullOrEmpty(resourceVersion)
            || string.IsNullOrEmpty(sub)
            || !renewUrlValid)
        {
            return new RedemptionResult
            {
                Error = FlowResponse.Text(422, "The preview assertion is missing session claims."),
            };
        }

        return new RedemptionResult
        {
            DraftData = new DraftData
            {
                Path = path,
                ResourceVersion = resourceVersion!,
                PreviewContext = previewContext,
                Sub = sub!,
                RenewUrl = renewUrl!,
                AccessToken = exchange.AccessToken!,
                TokenType = exchange.TokenType!,
                TokenExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + exchange.ExpiresIn * 1000,
                CodeVerifier = nextVerifier,
            },
        };
    }

    /// <summary>
    /// Enables the framework draft flag and stores the session in the draft
    /// cookies. The framework's own flag cookie (when it has one) is re-set
    /// with the cross-site attribute set — see <see cref="DraftCookie"/> for
    /// why the defaults would silently break inside the embedding iframe.
    /// </summary>
    private async Task StoreDraftSessionAsync(DraftData draftData)
    {
        await _adapter.EnableDraftFlagAsync().ConfigureAwait(false);

        if (_adapter.DraftFlagCookieName is { } flagCookieName)
        {
            var flagValue = await _adapter.GetCookieAsync(flagCookieName).ConfigureAwait(false);
            if (flagValue is not null)
            {
                await _adapter.SetCookieAsync(DraftCookie.Build(flagCookieName, flagValue)).ConfigureAwait(false);
            }
        }

        await _adapter.SetCookieAsync(
            DraftCookie.Build(CanvasConstants.DraftDataCookieName, draftData.Serialize())).ConfigureAwait(false);
    }

    /// <summary>
    /// Body of the draft-mode activation route (GET, <c>?assertion=</c> query).
    /// Redeems the assertion, stores the session, and redirects to the signed
    /// entry path; failure responses pass Drupal's status through.
    /// </summary>
    public async Task<FlowResponse> EnableDraftModeAsync(
        string? assertion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(assertion))
        {
            return FlowResponse.Text(422, "Missing preview assertion.");
        }

        var result = await RedeemAssertionAsync(_httpClient, assertion, _getConfig(), null, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Ok)
        {
            // A dead assertion can arrive on top of a live session: assertions
            // are single-use, so restoring a closed tab or navigating back to
            // the activation entry URL re-submits one that was already
            // redeemed. The session itself (cookies) is unaffected — continue
            // into it instead of stranding the user on an error page.
            var existingSession = await GetDraftDataAsync().ConfigureAwait(false);
            if (existingSession is not null && !existingSession.IsExpired())
            {
                return _adapter.Redirect(existingSession.Path);
            }

            return result.Error!;
        }

        await StoreDraftSessionAsync(result.DraftData!).ConfigureAwait(false);

        // The path was signed into the assertion Drupal accepted, and is
        // additionally constrained to a site-relative path (no scheme, host,
        // or protocol-relative form) in RedeemAssertionAsync().
        return _adapter.Redirect(result.DraftData!.Path);
    }

    /// <summary>
    /// Body of the renewal route (POST, JSON <c>{assertion}</c>). Continuation
    /// only: 400 without an existing session, 409 when the assertion names a
    /// different editor; on success answers <c>{tokenExpiresAt}</c> as JSON.
    ///
    /// The exchange and cookie handling are exactly the activation path — same
    /// single-use jti, same claim checks on Drupal's side — but the response
    /// is JSON instead of a redirect, so the client can refresh its data
    /// without a document reload. The renewal exchange is PKCE-bound to the
    /// app server: Drupal refuses to redeem a renewal assertion without the
    /// code_verifier registered at the previous redemption, and that verifier
    /// lives in the httpOnly session cookie only the app server reads. See the
    /// JavaScript SDK's flows.ts for the full security narrative (CSRF
    /// analysis included); this port preserves its behavior exactly.
    /// </summary>
    public async Task<FlowResponse> RenewDraftSessionAsync(
        string? bodyJson,
        CancellationToken cancellationToken = default)
    {
        string? assertion = null;
        if (bodyJson is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(bodyJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("assertion", out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    assertion = value.GetString();
                }
            }
            catch (JsonException)
            {
                // An unreadable body is a missing assertion.
            }
        }
        if (string.IsNullOrEmpty(assertion))
        {
            return FlowResponse.Text(422, "Missing preview assertion.");
        }

        // Continuation only: no session, nothing to renew — starting a session
        // is the preview URL's job, and refusing here keeps this endpoint from
        // doubling as a second activation surface.
        var existingSession = await GetDraftDataAsync().ConfigureAwait(false);
        if (existingSession is null)
        {
            return FlowResponse.Text(
                400, "No draft session to renew. Open a preview from Drupal to start one.");
        }

        // Identity pre-check on the *unverified* claims — safe, because it can
        // only refuse: an assertion forged to pass this check still has to
        // pass Drupal's signature verification to mint anything. Checking
        // before the exchange keeps a mismatched (still valid, single-use)
        // assertion unconsumed and avoids minting a token nobody will use.
        var claimedSub = AssertionClaims.GetString(AssertionClaims.Decode(assertion), "sub");
        if (claimedSub != existingSession.Sub)
        {
            return FlowResponse.Text(
                409,
                "The assertion names a different editor than this draft session. "
                + "Re-open the preview from Drupal to start a new session.");
        }

        var result = await RedeemAssertionAsync(
            _httpClient, assertion, _getConfig(), existingSession.CodeVerifier, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Ok)
        {
            return result.Error!;
        }

        await StoreDraftSessionAsync(result.DraftData!).ConfigureAwait(false);

        return FlowResponse.Json(
            200,
            JsonSerializer.Serialize(new Dictionary<string, long>
            {
                ["tokenExpiresAt"] = result.DraftData!.TokenExpiresAt,
            }));
    }

    /// <summary>
    /// Body of the draft-mode exit route (POST — exiting changes state, and a
    /// GET endpoint reached by links would be eligible for prefetching):
    /// disables the flag, overwrites both cookies expired, and redirects to
    /// the homepage with a 303 so the browser follows with a GET.
    /// </summary>
    public async Task<FlowResponse> DisableDraftModeAsync()
    {
        await _adapter.DisableDraftFlagAsync().ConfigureAwait(false);

        // Overwrite the cookies with expired equivalents carrying the original
        // attributes; see DraftCookie.BuildCleared() for why plain framework
        // deletions leave the CHIPS cookies alive.
        foreach (var name in new[] { _adapter.DraftFlagCookieName, CanvasConstants.DraftDataCookieName })
        {
            if (name is not null)
            {
                await _adapter.SetCookieAsync(DraftCookie.BuildCleared(name)).ConfigureAwait(false);
            }
        }

        // Invoked by POST, so the redirect is a 303 See Other: the browser
        // follows it with a GET, instead of a 307 replaying the POST against
        // the homepage.
        return FlowResponse.Redirect("/", 303);
    }

    /// <summary>
    /// Fetches a page by its Drupal path (see <see cref="ContentApi"/>),
    /// carrying the live draft session's bearer token when there is one.
    /// </summary>
    public async Task<PageResult?> FetchPageAsync(string path, CancellationToken cancellationToken = default)
    {
        var draftData = await GetDraftDataAsync().ConfigureAwait(false);
        return await ContentApi.FetchPageAsync(
            _httpClient, path, _getConfig(), draftData, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches one component preview through the current draft session without
    /// changing that session's entry path.
    /// </summary>
    public async Task<PageResult?> FetchComponentPreviewAsync(
        string componentId,
        CancellationToken cancellationToken = default)
    {
        var draftData = await GetDraftDataAsync().ConfigureAwait(false);
        if (draftData is null || componentId.Length == 0)
        {
            return null;
        }
        return await ContentApi.FetchPageAsync(
            _httpClient, draftData.Path, _getConfig(), draftData, componentId, cancellationToken)
            .ConfigureAwait(false);
    }
}
