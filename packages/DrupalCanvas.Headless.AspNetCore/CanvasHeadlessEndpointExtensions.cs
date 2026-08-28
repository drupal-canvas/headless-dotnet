using System.Text;
using DrupalCanvas.Headless;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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

        // Cast to Delegate so minimal APIs treat the handlers as route
        // handlers (writing the IResult) rather than RequestDelegates.
        endpoints.MapGet("/api/canvas/components", (Delegate)ComponentMetadataEndpoint.HandleGetAsync);
        endpoints.MapMethods("/api/canvas/components", ["OPTIONS"],
            (Delegate)ComponentMetadataEndpoint.HandleOptionsAsync);

        return endpoints;
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
