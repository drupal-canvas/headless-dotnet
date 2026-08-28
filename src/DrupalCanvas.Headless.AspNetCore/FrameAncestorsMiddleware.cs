using DrupalCanvas.Headless;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DrupalCanvas.Headless.AspNetCore;

public static class FrameAncestorsMiddlewareExtensions
{
    /// <summary>
    /// Merges the <c>frame-ancestors</c> directive into every response's
    /// Content-Security-Policy, restricting who may embed the app. Merged,
    /// not set: a policy the app already sends (default-src, script-src, ...)
    /// is preserved, and an application-owned frame-ancestors directive
    /// remains authoritative. Otherwise, responses are 'self'-only by
    /// default, and a draft session also admits the exact editor origin from
    /// its signed renewal URL.
    /// </summary>
    public static IApplicationBuilder UseDrupalCanvasFrameAncestors(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var server = context.RequestServices.GetService(typeof(DraftServer)) as DraftServer;
            var draftData = server is null ? null : await server.GetDraftDataAsync();
            context.Response.OnStarting(() =>
            {
                var existing = context.Response.Headers.ContentSecurityPolicy;
                var merged = Csp.MergeFrameAncestors(
                    existing.Select(value => value),
                    Csp.ResolveFrameAncestors(draftData));
                // Joined with ', ': the standard serialization of a policy
                // list in one header field.
                context.Response.Headers.ContentSecurityPolicy = string.Join(", ", merged);
                return Task.CompletedTask;
            });
            await next();
        });
}
