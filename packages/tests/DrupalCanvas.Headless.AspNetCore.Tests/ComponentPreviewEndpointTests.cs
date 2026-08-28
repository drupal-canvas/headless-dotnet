using System.Net;
using DrupalCanvas.Headless.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DrupalCanvas.Headless.AspNetCore.Tests;

public class ComponentPreviewEndpointTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private readonly FakeHttpHandler _drupal = new();

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseEnvironment("Development")
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddDrupalCanvasHeadless(options =>
                    {
                        options.BaseUrl = "https://drupal.example";
                        options.ComponentPreviewStylesheets.Add("/css/app.css");
                    });
                    services.AddDrupalCanvasComponents(components =>
                        components.Add<TestCard>("card"));
                    services.AddHttpClient(CanvasHeadlessServiceCollectionExtensions.HttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => _drupal);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapDrupalCanvasHeadless());
                }))
            .StartAsync();
        _client = _host.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Requested-With", "test");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private const string PreviewPageJson =
        """
        {
          "content": {
            "element": "js-card",
            "canvasDraftMode": true,
            "props": { "canvasUuid": "preview-uuid", "heading": "Thumbnail card" }
          },
          "head": { "title": "Preview" },
          "route": {
            "name": "entity.canvas_page.canonical",
            "requestUri": "/page/1",
            "params": {},
            "managedByCanvas": true,
            "entity": null
          }
        }
        """;

    private static string SessionCookieHeader()
        => $"{HttpContextDraftAdapter.DraftFlagCookie}=1; "
            + $"{CanvasConstants.DraftDataCookieName}="
            + Uri.EscapeDataString(TestData.LiveDraftData().Serialize());

    private async Task<HttpResponseMessage> GetPreviewAsync(string query, bool withSession)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"{CanvasConstants.ComponentPreviewPath}{query}");
        if (withSession)
        {
            request.Headers.Add("Cookie", SessionCookieHeader());
        }
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Redirects_home_without_a_draft_session()
    {
        var response = await GetPreviewAsync("?componentId=js.card", withSession: false);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Redirects_home_without_a_component_id()
    {
        var response = await GetPreviewAsync(string.Empty, withSession: true);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Redirects_home_when_the_preview_does_not_resolve()
    {
        _drupal.EnqueueJson(404, """{"message":"nope"}""");
        var response = await GetPreviewAsync("?componentId=js.card", withSession: true);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Renders_the_isolated_preview_document()
    {
        _drupal.EnqueueJson(200, PreviewPageJson);

        var response = await GetPreviewAsync("?componentId=js.card", withSession: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("data-canvas-component-preview-document", html);
        // The component rendered through the registry, editor-marked.
        Assert.Contains("<h2>Thumbnail card</h2>", html);
        Assert.Contains("<!-- canvas-start-preview-uuid -->", html);
        // The app stylesheet and the draft session element are present.
        Assert.Contains("href=\"/css/app.css\"", html);
        Assert.Contains("<canvas-draft-session", html);

        // The fetch pinned the session's entry path and carried the token.
        var requested = _drupal.Requests[0].Request;
        Assert.Contains("componentId=js.card", requested.RequestUri!.Query);
        Assert.Contains("requestUri=%2Fnode%2F9", requested.RequestUri!.Query);
        Assert.NotNull(requested.Headers.Authorization);
    }
}
