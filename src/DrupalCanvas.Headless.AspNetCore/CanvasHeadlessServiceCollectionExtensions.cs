using DrupalCanvas.Headless;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DrupalCanvas.Headless.AspNetCore;

public sealed class CanvasHeadlessOptions
{
    /// <summary>
    /// Base URL of the Drupal site. Defaults to the CANVAS_SITE_URL
    /// environment variable when unset.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The project root canvas.config.json and the component directory are
    /// resolved from. Defaults to the host's content root.
    /// </summary>
    public string? ProjectRoot { get; set; }
}

public static class CanvasHeadlessServiceCollectionExtensions
{
    public const string HttpClientName = "DrupalCanvas.Headless";

    /// <summary>
    /// Registers the Canvas Headless draft server and its supporting services.
    /// The draft server is request-scoped: all draft state lives in the
    /// request's cookies, reached through the HttpContext adapter.
    /// </summary>
    public static IServiceCollection AddDrupalCanvasHeadless(
        this IServiceCollection services,
        Action<CanvasHeadlessOptions>? configure = null)
    {
        services.AddOptions<CanvasHeadlessOptions>().Configure(options => configure?.Invoke(options));
        services.AddHttpContextAccessor();
        services.AddHttpClient(HttpClientName);

        services.AddScoped<DraftServer>(provider =>
        {
            var httpContext = provider.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?? throw new InvalidOperationException(
                    "The Canvas draft server is request-scoped and needs an active HttpContext.");
            var options = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<CanvasHeadlessOptions>>().Value;
            var httpClient = provider
                .GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new DraftServer(
                new HttpContextDraftAdapter(httpContext),
                httpClient,
                () => DraftConfig.Resolve(options.BaseUrl));
        });

        services.AddSingleton<ICanvasComponentMetadataProvider, ContentRootComponentMetadataProvider>();
        return services;
    }
}
