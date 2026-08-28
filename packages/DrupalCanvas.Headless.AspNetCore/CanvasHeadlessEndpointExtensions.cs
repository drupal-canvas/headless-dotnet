using System.Text;
using DrupalCanvas.Headless;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DrupalCanvas.Headless.AspNetCore;

public static class CanvasHeadlessEndpointExtensions
{
    /// <summary>
    /// Mounts the Canvas Headless routes at their conventional paths — the
    /// same paths the JavaScript adapters inject:
    /// <list type="bullet">
    /// <item><c>GET  /api/draft</c> — draft-mode activation</item>
    /// <item><c>POST /api/draft/renew</c> — in-place session renewal</item>
    /// <item><c>POST /api/disable-draft</c> — draft-mode exit</item>
    /// <item><c>GET|OPTIONS /api/canvas/components</c> — component metadata</item>
    /// <item><c>GET /api/canvas/component-preview</c> — the isolated
    /// one-component preview document (editor thumbnails)</item>
    /// </list>
    /// </summary>
    public static IEndpointRouteBuilder MapDrupalCanvasHeadless(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/draft", async (HttpContext context, DraftServer server) =>
        {
            var assertion = context.Request.Query["assertion"].FirstOrDefault();
            return ToResult(await server.EnableDraftModeAsync(assertion, context.RequestAborted));
        });

        endpoints.MapPost("/api/draft/renew", async (HttpContext context, DraftServer server) =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            return ToResult(await server.RenewDraftSessionAsync(body, context.RequestAborted));
        });

        endpoints.MapPost("/api/disable-draft", async (DraftServer server) =>
            ToResult(await server.DisableDraftModeAsync()));

        endpoints.MapGet(CanvasConstants.ComponentPreviewPath, HandleComponentPreviewAsync);

        // Cast to Delegate so minimal APIs treat the handlers as route
        // handlers (writing the IResult) rather than RequestDelegates.
        endpoints.MapGet("/api/canvas/components", (Delegate)ComponentMetadataEndpoint.HandleGetAsync);
        endpoints.MapMethods("/api/canvas/components", ["OPTIONS"],
            (Delegate)ComponentMetadataEndpoint.HandleOptionsAsync);

        return endpoints;
    }

    /// <summary>
    /// The isolated one-component preview the Canvas editor loads for
    /// library thumbnails. Draft-session-only, like the Astro adapter's
    /// ComponentPreviewPage: without a session, a component id, or a
    /// resolvable preview, the request redirects to the homepage instead of
    /// exposing an error surface.
    /// </summary>
    private static async Task<IResult> HandleComponentPreviewAsync(
        HttpContext context, DraftServer server)
    {
        var componentId = context.Request.Query[CanvasConstants.ComponentPreviewQuery].FirstOrDefault();
        var draftData = await server.GetDraftDataAsync();
        if (draftData is null || string.IsNullOrEmpty(componentId))
        {
            return Results.Redirect("/");
        }

        var result = await server.FetchComponentPreviewAsync(componentId, context.RequestAborted);
        if (result is not Page page)
        {
            return Results.Redirect("/");
        }

        var options = context.RequestServices
            .GetRequiredService<IOptions<CanvasHeadlessOptions>>().Value;
        await using var renderer = new HtmlRenderer(
            context.RequestServices,
            context.RequestServices.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ComponentPreviewDocument>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(ComponentPreviewDocument.Tree)] = page.Content,
                    [nameof(ComponentPreviewDocument.Stylesheets)] =
                        (IReadOnlyList<string>)[.. options.ComponentPreviewStylesheets],
                }));
            return output.ToHtmlString();
        });
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static IResult ToResult(FlowResponse response)
    {
        if (response.Location is not null)
        {
            return new RedirectFlowResult(response.Status, response.Location);
        }
        return Results.Text(
            response.Body ?? string.Empty,
            response.ContentType,
            statusCode: response.Status);
    }

    /// <summary>
    /// A redirect with the flow's exact status code; Results.Redirect() cannot
    /// express the exit flow's 303 See Other.
    /// </summary>
    private sealed class RedirectFlowResult(int status, string location) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = status;
            httpContext.Response.Headers.Location = location;
            return Task.CompletedTask;
        }
    }
}
