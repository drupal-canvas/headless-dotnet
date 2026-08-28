using System.Net;
using DrupalCanvas.Headless.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DrupalCanvas.Headless.AspNetCore.Tests;

/// <summary>
/// End-to-end tests of the mounted endpoints and middleware over TestServer,
/// with Drupal faked at the HttpClient layer. The Set-Cookie assertions are
/// the load-bearing ones: the CHIPS (Partitioned) attribute rides through
/// CookieOptions.Extensions, which nothing else verifies.
/// </summary>
public class EndpointTests : IAsyncLifetime
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
                        options.BaseUrl = "https://drupal.example");
                    services.AddHttpClient(CanvasHeadlessServiceCollectionExtensions.HttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => _drupal);
                    services.AddSingleton<ICanvasComponentMetadataProvider>(
                        new StaticMetadataProvider());
                })
                .Configure(app =>
                {
                    app.UseDrupalCanvasFrameAncestors();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapDrupalCanvasHeadless();
                        endpoints.MapGet("/page", () => Results.Text("page"));
                    });
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private sealed class StaticMetadataProvider : ICanvasComponentMetadataProvider
    {
        public ValueTask<ComponentMetadataPayload> GetPayloadAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ComponentMetadataPayload
            {
                Components = [],
                Warnings = [],
            });
    }

    private static string SessionCookieHeader()
        => $"{HttpContextDraftAdapter.DraftFlagCookie}=1; "
            + $"{CanvasConstants.DraftDataCookieName}="
            + Uri.EscapeDataString(TestData.LiveDraftData().Serialize());

    [Fact]
    public async Task Activation_stores_partitioned_cookies_and_redirects_to_the_signed_path()
    {
        _drupal.EnqueueJson(200, TestData.TokenResponseJson);
        var assertion = TestData.BuildAssertion(TestData.ValidClaims);

        var response = await _client.GetAsync(
            $"/api/draft?assertion={Uri.EscapeDataString(assertion)}");

        Assert.Equal((HttpStatusCode)307, response.StatusCode);
        Assert.Equal("/node/1", response.Headers.Location!.ToString());

        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var dataCookie = Assert.Single(
            setCookies, value => value.StartsWith(CanvasConstants.DraftDataCookieName));
        Assert.Contains("path=/", dataCookie);
        Assert.Contains("samesite=none", dataCookie);
        Assert.Contains("secure", dataCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", dataCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Partitioned", dataCookie);
        var flagCookie = Assert.Single(
            setCookies, value => value.StartsWith(HttpContextDraftAdapter.DraftFlagCookie));
        Assert.Contains("Partitioned", flagCookie);
    }

    [Fact]
    public async Task Activation_without_an_assertion_answers_422()
    {
        var response = await _client.GetAsync("/api/draft");
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    [Fact]
    public async Task Renewal_refuses_without_a_session_and_renews_with_one()
    {
        var assertion = TestData.BuildAssertion(TestData.ValidClaims);
        var body = new StringContent(
            $"{{\"assertion\":\"{assertion}\"}}",
            System.Text.Encoding.UTF8,
            "application/json");
        var refused = await _client.PostAsync("/api/draft/renew", body);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        _drupal.EnqueueJson(200, TestData.TokenResponseJson);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/draft/renew")
        {
            Content = new StringContent(
                $"{{\"assertion\":\"{assertion}\"}}",
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Cookie", SessionCookieHeader());
        var renewed = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.Contains("tokenExpiresAt", await renewed.Content.ReadAsStringAsync());
        Assert.Equal("stored-verifier", _drupal.FormBody()["code_verifier"]);
    }

    [Fact]
    public async Task Exit_expires_the_cookies_with_their_partition_attributes()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/disable-draft");
        request.Headers.Add("Cookie", SessionCookieHeader());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());
        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var dataCookie = Assert.Single(
            setCookies, value => value.StartsWith(CanvasConstants.DraftDataCookieName));
        Assert.Contains("expires=Thu, 01 Jan 1970", dataCookie);
        Assert.Contains("Partitioned", dataCookie);
    }

    [Fact]
    public async Task Responses_carry_a_self_only_frame_ancestors_policy_by_default()
    {
        var response = await _client.GetAsync("/page");
        Assert.Equal(
            "frame-ancestors 'self'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task A_draft_session_admits_the_editor_origin_into_frame_ancestors()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/page");
        request.Headers.Add("Cookie", SessionCookieHeader());
        var response = await _client.SendAsync(request);
        Assert.Equal(
            "frame-ancestors 'self' https://drupal.example",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task The_components_endpoint_requires_a_bearer_assertion()
    {
        var response = await _client.GetAsync("/api/canvas/components");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("missing_assertion", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_components_endpoint_verifies_by_redemption_and_answers_the_payload()
    {
        _drupal.EnqueueJson(200, TestData.TokenResponseJson);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/canvas/components");
        request.Headers.Add(
            "Authorization", $"Bearer {TestData.BuildAssertion(TestData.ValidClaims)}");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"version\":1", body);
        Assert.Contains("\"components\":[]", body);
    }

    [Fact]
    public async Task The_components_endpoint_refuses_a_mismatched_origin_before_redeeming()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/canvas/components");
        request.Headers.Add(
            "Authorization", $"Bearer {TestData.BuildAssertion(TestData.ValidClaims)}");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // The single-use assertion was not spent at the token endpoint.
        Assert.Empty(_drupal.Requests);
    }

    [Fact]
    public async Task The_components_endpoint_scopes_cors_to_the_editor_origin()
    {
        _drupal.EnqueueJson(200, TestData.TokenResponseJson);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/canvas/components");
        request.Headers.Add(
            "Authorization", $"Bearer {TestData.BuildAssertion(TestData.ValidClaims)}");
        request.Headers.Add("Origin", "https://drupal.example");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "https://drupal.example",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task The_components_endpoint_answers_cors_preflights()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/canvas/components");
        request.Headers.Add("Origin", "https://drupal.example");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "GET, OPTIONS",
            response.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }
}
