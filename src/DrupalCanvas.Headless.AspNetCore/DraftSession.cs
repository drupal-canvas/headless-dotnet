using DrupalCanvas.Headless;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Http;

namespace DrupalCanvas.Headless.AspNetCore;

/// <summary>
/// The Blazor lifecycle around the draft session: gathers the session state
/// server-side and hands it to the <c>&lt;canvas-draft-session&gt;</c> custom
/// element from @drupal-canvas/headless/client (served from this package's
/// static assets), which owns the machine — expiry timing, the renewal
/// protocol with the embedding host, re-arming after a renewal, and content
/// height reporting. Renders nothing when draft mode is off.
///
/// The child content owns presentation: children marked
/// <c>data-draft-session-view="active"</c> show while the session is live and
/// the page is standalone (embedded, the host owns the session chrome);
/// <c>data-draft-session-view="expired"</c> children show once the session
/// has expired, embedded or not. A <c>data-draft-session-renew-link</c>
/// anchor gets its href pointed at Drupal's renew route for the current path.
/// A page that only needs the renewal protocol leaves the content empty.
/// </summary>
public sealed class DraftSession : ComponentBase
{
    private const string AssetBase = "_content/DrupalCanvas.Headless.AspNetCore";

    /// <summary>
    /// The app route that redeems a fresh assertion into the session; align
    /// with custom route mounting when the conventional path is not used.
    /// </summary>
    [Parameter]
    public string RenewEndpoint { get; set; } = "/api/draft/renew";

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Inject]
    private DraftServer Server { get; set; } = null!;

    [Inject]
    private IHttpContextAccessor HttpContextAccessor { get; set; } = null!;

    private bool _enabled;
    private DraftData? _draftData;

    protected override async Task OnParametersSetAsync()
    {
        var cookies = HttpContextAccessor.HttpContext?.Request.Cookies;
        _enabled = cookies is not null
            && cookies.TryGetValue(HttpContextDraftAdapter.DraftFlagCookie, out var flag)
            && flag == "1";
        _draftData = _enabled ? await Server.GetDraftDataAsync() : null;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!_enabled)
        {
            return;
        }

        var expired = _draftData is null || _draftData.IsExpired();

        builder.AddMarkupContent(0,
            $"<link rel=\"stylesheet\" href=\"{AssetBase}/preview.css\" />");
        // Until the element connects and applies the visibility rules, every
        // state view stays hidden — the server cannot know whether the page is
        // embedded, so first paint shows nothing. The [hidden] rule keeps the
        // element's toggling authoritative over display properties the app's
        // own styles set.
        builder.AddMarkupContent(1,
            "<style>canvas-draft-session:not(:defined) [data-draft-session-view],"
            + "canvas-draft-session [hidden]{display:none !important;}</style>");

        builder.OpenElement(2, "canvas-draft-session");
        if (_draftData is not null)
        {
            builder.AddAttribute(3, "token-expires-at",
                _draftData.TokenExpiresAt.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.AddAttribute(4, "renew-url", _draftData.RenewUrl);
            if (DraftData.GetDraftEditorOrigin(_draftData.RenewUrl) is { } editorOrigin)
            {
                builder.AddAttribute(5, "editor-origin", editorOrigin);
            }
        }
        if (expired)
        {
            builder.AddAttribute(6, "initial-expired", string.Empty);
        }
        builder.AddAttribute(7, "renew-endpoint", RenewEndpoint);
        builder.AddContent(8, ChildContent);
        builder.CloseElement();

        builder.AddMarkupContent(9,
            $"<script type=\"module\" src=\"{AssetBase}/draft-session-init.js\"></script>");
    }
}
