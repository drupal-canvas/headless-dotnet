using DrupalCanvas.Headless;
using Microsoft.AspNetCore.Http;

namespace DrupalCanvas.Headless.AspNetCore;

/// <summary>
/// The ASP.NET Core implementation of the draft server adapter, bound to one
/// request's <see cref="HttpContext"/>.
///
/// ASP.NET Core has no framework draft mode, so the flag is the SDK's own
/// cookie, set with the same cross-site (CHIPS) attributes as the session data
/// cookie — the same choice the Astro adapter makes. There is no rendering
/// behavior behind it: with every page rendered on demand, the flag only
/// records that a draft session was activated and not yet exited.
/// </summary>
public sealed class HttpContextDraftAdapter(HttpContext httpContext) : IDraftServerAdapter
{
    public const string DraftFlagCookie = "canvas_headless_draft_mode";

    public ValueTask<string?> GetCookieAsync(string name)
        => ValueTask.FromResult<string?>(
            httpContext.Request.Cookies.TryGetValue(name, out var value) ? value : null);

    public ValueTask SetCookieAsync(DraftCookie cookie)
    {
        var options = new CookieOptions
        {
            HttpOnly = cookie.HttpOnly,
            Path = cookie.Path,
            SameSite = SameSiteMode.None,
            Secure = cookie.Secure,
            Expires = cookie.Expires,
        };
        if (cookie.Partitioned)
        {
            // CHIPS. Response.Cookies has no first-class Partitioned flag, but
            // attribute extensions pass through verbatim. Deletions carry it
            // too: a CHIPS cookie only matches a deletion stating its
            // partition (see DraftCookie.BuildCleared).
            options.Extensions.Add("Partitioned");
        }
        httpContext.Response.Cookies.Append(cookie.Name, cookie.Value, options);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> IsDraftFlagEnabledAsync()
        => ValueTask.FromResult(
            httpContext.Request.Cookies.TryGetValue(DraftFlagCookie, out var value) && value == "1");

    public ValueTask EnableDraftFlagAsync()
        => SetCookieAsync(DraftCookie.Build(DraftFlagCookie, "1"));

    public ValueTask DisableDraftFlagAsync()
        => SetCookieAsync(DraftCookie.BuildCleared(DraftFlagCookie));

    // The flag cookie above already carries the cross-site attributes, so no
    // re-set pass is needed (that hook exists for frameworks whose own flag
    // cookie ships with same-site defaults).
    public string? DraftFlagCookieName => null;

    public FlowResponse Redirect(string path) => FlowResponse.Redirect(path, 307);
}
