using System.Net.Http.Headers;

namespace DrupalCanvas.Headless;

/// <summary>
/// The client for the Canvas Headless module's rendered-content endpoint:
/// resolve a Drupal request URI and get the routed content back as structured
/// data. Drupal Canvas Headless exposes it at
/// <c>/canvas/content-api?requestUri={requestUri}</c>. The endpoint path
/// remains an implementation detail confined to this file so the SDK's public
/// surface describes what the caller gets rather than how Drupal serves it.
/// </summary>
public static class ContentApi
{
    /// <summary>
    /// Fetches a page by its Drupal request URI (e.g. <c>/node/4?view=full</c>).
    ///
    /// With a draft session the request carries the session's user-bound
    /// bearer token, so content the initiating editor may see (e.g.
    /// unpublished entities) renders; without one — or once the session token
    /// has expired — the request is anonymous and resolves only what anonymous
    /// visitors may see. Returns null for anything the current access level
    /// cannot see (403/404).
    ///
    /// The endpoint renders through Drupal's routing, so the default revision
    /// is served; it has no notion of JSON:API's resourceVersion.
    /// </summary>
    public static async Task<PageResult?> FetchPageAsync(
        HttpClient httpClient,
        string requestUri,
        DraftConfig config,
        DraftData? draftData = null,
        string? componentPreviewId = null,
        CancellationToken cancellationToken = default)
    {
        var liveDraft = draftData is not null && !draftData.IsExpired();

        var query = new List<KeyValuePair<string, string>>
        {
            new("requestUri", requestUri),
        };
        if (!string.IsNullOrEmpty(componentPreviewId))
        {
            query.Add(new(CanvasConstants.ComponentPreviewQuery, componentPreviewId));
        }
        if (liveDraft && draftData!.PreviewContext?.ViewMode is { } viewMode)
        {
            query.Add(new("viewMode", viewMode));
        }
        if (string.IsNullOrEmpty(componentPreviewId)
            && liveDraft
            && draftData!.PreviewContext?.PageVariant is { } pageVariant)
        {
            query.Add(new("pageVariant", pageVariant));
        }

        var queryString = string.Join('&', query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var url = $"{config.BaseUrl.TrimEnd('/')}/canvas/content-api?{queryString}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        if (liveDraft)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(draftData!.TokenType, draftData.AccessToken);
        }
        // Expired session: stay anonymous; the draft indicator surfaces it.

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = PageResult.Parse(body);
        if (result is Page page && liveDraft && page.Route.ManagedByCanvas)
        {
            // Editor-renderable draft content: mark the root, creating a
            // transparent one when Canvas managed the route but returned no
            // content, so the renderer still emits measurable region geometry.
            var content = page.Content ?? new CanvasComponentTreeElement { Element = "renderless-container" };
            return page with { Content = content with { CanvasDraftMode = true } };
        }
        return result;
    }
}
