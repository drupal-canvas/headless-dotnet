using System.Text.RegularExpressions;
using DrupalCanvas.Headless;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DrupalCanvas.Headless.AspNetCore;

/// <summary>
/// The component metadata endpoint: GET answers the codebase's component
/// registry to the Drupal Canvas instance, and OPTIONS answers the browser's
/// CORS preflight. A port of the JavaScript SDK's
/// <c>createComponentMetadataHandler()</c>.
///
/// Protection is proof-by-redemption: the caller presents a Drupal-minted
/// preview assertion as a Bearer token, and the endpoint verifies it by
/// redeeming it at Drupal's own token endpoint — only the embedding Drupal
/// can mint one, assertions are single-use, and the minted access token is
/// discarded. Drupal coordinates the request in the editor's browser so local
/// frontends remain reachable. The authenticated response is CORS-readable
/// only by the editor origin carried in the accepted assertion's signed
/// renewUrl claim; no separate origin configuration is needed.
/// </summary>
internal static partial class ComponentMetadataEndpoint
{
    [GeneratedRegex(@"^Bearer\s+(\S+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    private static void ApplyCorsHeaders(HttpResponse response, string? origin)
    {
        // no-store on every response: without it an intermediary could cache
        // an authenticated 200 body without its Authorization header and
        // serve it to a caller that never presented an assertion.
        response.Headers.CacheControl = "no-store";
        if (origin is null)
        {
            return;
        }
        response.Headers.AccessControlAllowOrigin = origin;
        response.Headers.AccessControlAllowMethods = "GET, OPTIONS";
        response.Headers.AccessControlAllowHeaders = "Authorization";
        response.Headers.AccessControlMaxAge = "3600";
        response.Headers.Vary = "Origin";
    }

    private static IResult Error(int status, string error, string message)
        => Results.Json(new { error, message }, statusCode: status);

    public static async Task<IResult> HandleGetAsync(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.FirstOrDefault();
        ApplyCorsHeaders(context.Response, origin);

        var options = context.RequestServices
            .GetRequiredService<IOptions<CanvasHeadlessOptions>>().Value;
        DraftConfig config;
        try
        {
            config = DraftConfig.Resolve(options.BaseUrl);
        }
        catch (InvalidOperationException e)
        {
            return Error(500, "configuration_error", e.Message);
        }

        var authorization = context.Request.Headers.Authorization.FirstOrDefault() ?? string.Empty;
        var assertion = BearerToken().Match(authorization) is { Success: true } match
            ? match.Groups[1].Value
            : null;
        if (assertion is null)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return Error(401, "missing_assertion",
                "Provide a Drupal preview assertion as a Bearer token. "
                + "Assertions are single-use; mint a fresh one per request.");
        }

        // Decode only to reject a mismatched browser origin before spending
        // the single-use assertion. Redemption below remains the authorization
        // and turns the same assertion's signed claim into trusted input.
        if (origin is not null)
        {
            var renewUrl = AssertionClaims.GetString(AssertionClaims.Decode(assertion), "renewUrl");
            if (DraftData.GetDraftEditorOrigin(renewUrl) != origin)
            {
                return Error(403, "origin_not_allowed",
                    "The request origin does not match the editor origin in the preview assertion.");
            }
        }

        var httpClient = context.RequestServices
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(CanvasHeadlessServiceCollectionExtensions.HttpClientName);
        var verification = await AssertionVerification.VerifyByRedemptionAsync(
            httpClient, assertion, config, context.RequestAborted);
        if (!verification.Ok)
        {
            return Error(
                verification.Status,
                verification.Status == 502 ? "drupal_unreachable" : "invalid_assertion",
                verification.Message);
        }

        ComponentMetadataPayload payload;
        try
        {
            payload = await context.RequestServices
                .GetRequiredService<ICanvasComponentMetadataProvider>()
                .GetPayloadAsync(context.RequestAborted);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            return Error(500, "discovery_failed", e.Message);
        }

        return Results.Text(payload.Serialize(), "application/json");
    }

    public static Task<IResult> HandleOptionsAsync(HttpContext context)
    {
        ApplyCorsHeaders(context.Response, context.Request.Headers.Origin.FirstOrDefault());
        return Task.FromResult(Results.StatusCode(204));
    }
}
