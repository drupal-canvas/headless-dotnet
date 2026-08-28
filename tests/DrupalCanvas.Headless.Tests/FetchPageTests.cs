namespace DrupalCanvas.Headless.Tests;

/// <summary>
/// Port of the <c>fetchPage</c> block of the JavaScript SDK's
/// <c>server/flows.test.ts</c>.
/// </summary>
public class FetchPageTests
{
    private const string PageJson =
        """
        {
          "content": { "element": "canvas-page" },
          "head": { "title": "Example page" },
          "route": {
            "name": "entity.canvas_page.canonical",
            "requestUri": "/example",
            "params": { "canvas_page": "1" },
            "managedByCanvas": true,
            "entity": {
              "entityType": "canvas_page",
              "bundle": "canvas_page",
              "id": "1",
              "uuid": "page-uuid",
              "langcode": "en"
            }
          }
        }
        """;

    private static string RequestedUrl(FakeHttpHandler http, int index = 0)
        => http.Requests[index].Request.RequestUri!.ToString();

    [Fact]
    public async Task Keeps_a_public_component_tree_marker_free()
    {
        var (server, _, http) = TestData.MakeServer();
        http.EnqueueJson(200, PageJson);

        var result = await server.FetchPageAsync("/example");

        var page = Assert.IsType<Page>(result);
        Assert.False(page.Content!.CanvasDraftMode);
        Assert.Equal("canvas-page", page.Content.Element);
        Assert.Equal("Example page", page.Head.Title);
        Assert.True(page.Route.ManagedByCanvas);
        Assert.Equal("page-uuid", page.Route.Entity!.Uuid);
        Assert.Equal(
            "https://drupal.example/canvas/content-api?requestUri=%2Fexample",
            RequestedUrl(http));
        var request = http.Requests[0].Request;
        Assert.Equal("application/json", request.Headers.Accept.ToString());
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Returns_public_responses_without_Canvas_content_unchanged()
    {
        var (server, _, http) = TestData.MakeServer();
        http.EnqueueJson(200, PageJson.Replace("""{ "element": "canvas-page" }""", "null"));

        var result = await server.FetchPageAsync("/example");
        var page = Assert.IsType<Page>(result);
        Assert.Null(page.Content);
    }

    [Fact]
    public async Task Returns_a_configured_redirect_without_draft_annotations()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(200,
            """{"redirect":{"external":false,"url":"/new-location","statusCode":301}}""");
        adapter.SeedSession(TestData.LiveDraftData());

        var result = await server.FetchPageAsync("/old-location");

        var redirect = Assert.IsType<PageRedirect>(result);
        Assert.False(redirect.Redirect.External);
        Assert.Equal("/new-location", redirect.Redirect.Url);
        Assert.Equal(301, redirect.Redirect.StatusCode);
    }

    [Fact]
    public async Task Preserves_Drupal_base_paths_in_the_endpoint_URL()
    {
        var (server, _, http) = TestData.MakeServer(
            new DraftConfig { BaseUrl = "https://drupal.example/cms" });
        http.EnqueueJson(200, PageJson);

        await server.FetchPageAsync("/example");

        Assert.Equal(
            "https://drupal.example/cms/canvas/content-api?requestUri=%2Fexample",
            RequestedUrl(http));
    }

    [Fact]
    public async Task Marks_a_draft_component_tree_as_editor_renderable()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(200, PageJson);
        adapter.SeedSession(TestData.LiveDraftData(previewContext: new DraftPreviewContext
        {
            ViewMode = "teaser",
            PageVariant = "alternate",
        }));

        var result = await server.FetchPageAsync("/example");

        var page = Assert.IsType<Page>(result);
        Assert.True(page.Content!.CanvasDraftMode);
        Assert.Equal("canvas-page", page.Content.Element);
        Assert.Equal(
            "https://drupal.example/canvas/content-api?requestUri=%2Fexample&viewMode=teaser&pageVariant=alternate",
            RequestedUrl(http));
        Assert.Equal("Bearer old-token", http.Requests[0].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Fetches_a_component_through_the_existing_entity_draft_session()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(200, PageJson);
        adapter.SeedSession(TestData.LiveDraftData(
            path: "/example?language=fr",
            previewContext: new DraftPreviewContext { PageVariant = "alternate" }));

        await server.FetchComponentPreviewAsync("js.example");

        // The component preview pins the session's own entry path, and the
        // pageVariant is deliberately omitted for isolated component renders.
        Assert.Equal(
            "https://drupal.example/canvas/content-api?requestUri=%2Fexample%3Flanguage%3Dfr&componentId=js.example",
            RequestedUrl(http));
        var draftData = await server.GetDraftDataAsync();
        Assert.Equal("/example?language=fr", draftData!.Path);
    }

    [Fact]
    public async Task Keeps_an_expired_draft_session_anonymous_and_marker_free()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(200, PageJson);
        adapter.SeedSession(TestData.LiveDraftData(tokenExpiresAt: TestData.NowMs() - 1));

        var result = await server.FetchPageAsync("/example");

        var page = Assert.IsType<Page>(result);
        Assert.False(page.Content!.CanvasDraftMode);
        Assert.Null(http.Requests[0].Request.Headers.Authorization);
    }

    [Fact]
    public async Task Makes_empty_draft_content_editor_renderable()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(200, PageJson.Replace("""{ "element": "canvas-page" }""", "null"));
        adapter.SeedSession(TestData.LiveDraftData());

        var result = await server.FetchPageAsync("/example");

        var page = Assert.IsType<Page>(result);
        Assert.Equal("renderless-container", page.Content!.Element);
        Assert.True(page.Content.CanvasDraftMode);
    }

    [Fact]
    public async Task Keeps_unmanaged_draft_content_empty()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(200, PageJson
            .Replace("""{ "element": "canvas-page" }""", "null")
            .Replace("\"managedByCanvas\": true", "\"managedByCanvas\": false"));
        adapter.SeedSession(TestData.LiveDraftData());

        var result = await server.FetchPageAsync("/example");

        var page = Assert.IsType<Page>(result);
        Assert.Null(page.Content);
        Assert.False(page.Route.ManagedByCanvas);
    }

    [Fact]
    public async Task Returns_null_for_routes_the_current_access_level_cannot_see()
    {
        var (server, _, http) = TestData.MakeServer();
        http.EnqueueJson(404, """{"message":"not found"}""");
        Assert.Null(await server.FetchPageAsync("/missing"));
    }
}
