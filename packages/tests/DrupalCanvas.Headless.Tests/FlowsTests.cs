using System.Text.Json;

namespace DrupalCanvas.Headless.Tests;

/// <summary>
/// Conformance port of the JavaScript SDK's <c>server/flows.test.ts</c>. The
/// scenarios — inputs, status codes, and side effects — must stay in step with
/// that suite: it is the executable contract keeping the two implementations
/// of the draft-preview protocol from drifting apart.
/// </summary>
public class RedeemAssertionTests
{
    [Fact]
    public async Task Builds_the_draft_session_from_the_token_response_and_the_claims()
    {
        var http = new FakeHttpHandler();
        http.EnqueueJson(200, TestData.TokenResponseJson);
        var before = TestData.NowMs();

        var result = await DraftServer.RedeemAssertionAsync(
            http.CreateClient(), TestData.BuildAssertion(TestData.ValidClaims), TestData.Config);

        Assert.True(result.Ok);
        var draftData = result.DraftData!;
        Assert.Equal("/node/1", draftData.Path);
        Assert.Equal("rel:working-copy", draftData.ResourceVersion);
        Assert.Equal("teaser", draftData.PreviewContext?.ViewMode);
        Assert.Equal("alternate", draftData.PreviewContext?.PageVariant);
        Assert.Equal("42", draftData.Sub);
        Assert.Equal("https://drupal.example/canvas-headless/renew", draftData.RenewUrl);
        Assert.Equal("access-token-value", draftData.AccessToken);
        Assert.Equal("Bearer", draftData.TokenType);
        Assert.InRange(draftData.TokenExpiresAt, before + 900_000, TestData.NowMs() + 900_000);

        var (request, _) = http.Requests[0];
        Assert.Equal("https://drupal.example/oauth/token", request.RequestUri!.ToString());
        var body = http.FormBody();
        Assert.Equal("urn:ietf:params:oauth:grant-type:jwt-bearer", body["grant_type"]);
        Assert.Equal("canvas_headless", body["client_id"]);

        // Every exchange registers an S256 challenge for the next renewal, and
        // the stored verifier hashes to it.
        Assert.Equal("S256", body["code_challenge_method"]);
        Assert.Equal(Pkce.ComputeCodeChallenge(draftData.CodeVerifier), body["code_challenge"]);
        // An activation exchange carries no verifier: none was passed in.
        Assert.False(body.ContainsKey("code_verifier"));
    }

    [Fact]
    public async Task Presents_the_previous_verifier_when_one_is_passed()
    {
        var http = new FakeHttpHandler();
        http.EnqueueJson(200, TestData.TokenResponseJson);

        var result = await DraftServer.RedeemAssertionAsync(
            http.CreateClient(),
            TestData.BuildAssertion(TestData.ValidClaims),
            TestData.Config,
            "previous-verifier");

        Assert.True(result.Ok);
        Assert.Equal("previous-verifier", http.FormBody()["code_verifier"]);
        // The verifier rotates: the new session stores a fresh one.
        Assert.NotEqual("previous-verifier", result.DraftData!.CodeVerifier);
    }

    [Fact]
    public async Task Answers_502_when_Drupal_is_unreachable()
    {
        var http = new FakeHttpHandler { ThrowNetworkError = true };
        var result = await DraftServer.RedeemAssertionAsync(
            http.CreateClient(), TestData.BuildAssertion(TestData.ValidClaims), TestData.Config);
        Assert.False(result.Ok);
        Assert.Equal(502, result.Error!.Status);
    }

    [Fact]
    public async Task Passes_the_upstream_refusal_through_with_its_detail()
    {
        var http = new FakeHttpHandler();
        http.EnqueueJson(400,
            """
            {"error":"invalid_grant","error_description":"The assertion was already used.","hint":"Mint a fresh one."}
            """);
        var result = await DraftServer.RedeemAssertionAsync(
            http.CreateClient(), TestData.BuildAssertion(TestData.ValidClaims), TestData.Config);
        Assert.False(result.Ok);
        Assert.Equal(400, result.Error!.Status);
        Assert.Equal("The assertion was already used. Mint a fresh one.", result.Error.Body);
    }

    public static TheoryData<string, Dictionary<string, object?>> InvalidClaims => new()
    {
        { "a missing path", TestData.Claims(("path", TestData.Unset)) },
        { "a protocol-relative path", TestData.Claims(("path", "//evil.example")) },
        { "a backslash path", TestData.Claims(("path", "/node\\1")) },
        { "a relative path", TestData.Claims(("path", "node/1")) },
        { "a missing resourceVersion", TestData.Claims(("resourceVersion", TestData.Unset)) },
        { "an empty sub", TestData.Claims(("sub", "")) },
        { "a missing renewUrl", TestData.Claims(("renewUrl", TestData.Unset)) },
        { "a non-http renewUrl", TestData.Claims(("renewUrl", "javascript:alert(1)")) },
    };

    [Theory]
    [MemberData(nameof(InvalidClaims))]
    public async Task Answers_422_for_missing_or_malformed_session_claims(
        string label, Dictionary<string, object?> claims)
    {
        _ = label;
        var http = new FakeHttpHandler();
        http.EnqueueJson(200, TestData.TokenResponseJson);
        var result = await DraftServer.RedeemAssertionAsync(
            http.CreateClient(), TestData.BuildAssertion(claims), TestData.Config);
        Assert.False(result.Ok);
        Assert.Equal(422, result.Error!.Status);
    }
}

public class EnableDraftModeTests
{
    [Fact]
    public async Task Answers_422_without_an_assertion()
    {
        var (server, _, _) = TestData.MakeServer();
        var response = await server.EnableDraftModeAsync(null);
        Assert.Equal(422, response.Status);
    }

    [Fact]
    public async Task Stores_the_session_cross_site_and_redirects_to_the_signed_path()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(200, TestData.TokenResponseJson);

        var response = await server.EnableDraftModeAsync(
            TestData.BuildAssertion(TestData.ValidClaims));

        Assert.Equal(307, response.Status);
        Assert.Equal("/node/1", response.Location);
        Assert.True(adapter.Flag);

        // The framework flag cookie was re-set with the cross-site attributes.
        var flagCookie = adapter.Cookies[TestAdapter.FlagCookie];
        Assert.Equal("bypass-value", flagCookie.Value);
        Assert.Equal("None", flagCookie.SameSite);
        Assert.True(flagCookie.Secure);
        Assert.True(flagCookie.Partitioned);
        Assert.True(flagCookie.HttpOnly);
        Assert.Equal("/", flagCookie.Path);

        var dataCookie = adapter.Cookies[CanvasConstants.DraftDataCookieName];
        Assert.Equal("None", dataCookie.SameSite);
        Assert.True(dataCookie.Secure);
        Assert.True(dataCookie.Partitioned);
        using var stored = JsonDocument.Parse(dataCookie.Value);
        Assert.Equal("/node/1", stored.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Continues_into_a_live_session_when_the_assertion_is_dead()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(400, """{"error":"invalid_grant"}""");
        adapter.SeedSession(TestData.LiveDraftData(path: "/node/9"));

        var response = await server.EnableDraftModeAsync("dead");

        Assert.Equal(307, response.Status);
        Assert.Equal("/node/9", response.Location);
    }

    [Fact]
    public async Task Surfaces_the_redemption_failure_without_a_live_session()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(400, """{"error":"invalid_grant"}""");
        adapter.SeedSession(TestData.LiveDraftData(tokenExpiresAt: TestData.NowMs() - 1));

        var response = await server.EnableDraftModeAsync("dead");
        Assert.Equal(400, response.Status);
    }
}

public class RenewDraftSessionTests
{
    private static string RenewBody(object body) => JsonSerializer.Serialize(body);

    [Fact]
    public async Task Answers_422_without_an_assertion_in_the_body()
    {
        var (server, adapter, _) = TestData.MakeServer();
        adapter.SeedSession(TestData.LiveDraftData());
        var response = await server.RenewDraftSessionAsync(RenewBody(new { }));
        Assert.Equal(422, response.Status);
    }

    [Fact]
    public async Task Refuses_to_renew_without_an_existing_session()
    {
        var (server, _, _) = TestData.MakeServer();
        var response = await server.RenewDraftSessionAsync(
            RenewBody(new { assertion = TestData.BuildAssertion(TestData.ValidClaims) }));
        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task Refuses_an_assertion_naming_a_different_editor_unconsumed()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(200, TestData.TokenResponseJson);
        adapter.SeedSession(TestData.LiveDraftData(sub: "42"));

        var response = await server.RenewDraftSessionAsync(
            RenewBody(new { assertion = TestData.BuildAssertion(TestData.Claims(("sub", "7"))) }));

        Assert.Equal(409, response.Status);
        // The mismatched assertion was never presented at the token endpoint.
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Renews_the_session_and_answers_the_new_expiry_as_JSON()
    {
        var (server, adapter, http) = TestData.MakeServer();
        http.EnqueueJson(200, TestData.TokenResponseJson);
        adapter.SeedSession(TestData.LiveDraftData(sub: "42"));

        var response = await server.RenewDraftSessionAsync(
            RenewBody(new { assertion = TestData.BuildAssertion(TestData.ValidClaims) }));

        Assert.Equal(200, response.Status);
        using var body = JsonDocument.Parse(response.Body!);
        Assert.True(body.RootElement.GetProperty("tokenExpiresAt").GetInt64() > TestData.NowMs());

        var stored = DraftData.Parse(adapter.Cookies[CanvasConstants.DraftDataCookieName].Value)!;
        Assert.Equal("access-token-value", stored.AccessToken);

        // The renewal exchange spends the session's stored verifier at Drupal,
        // and the session continues with a rotated one.
        Assert.Equal("stored-verifier", http.FormBody()["code_verifier"]);
        Assert.NotEqual("stored-verifier", stored.CodeVerifier);
    }
}

public class DisableDraftModeTests
{
    [Fact]
    public async Task Overwrites_both_cookies_expired_with_matching_partition_attributes()
    {
        var (server, adapter, _) = TestData.MakeServer();
        adapter.SeedSession(TestData.LiveDraftData());
        adapter.Cookies[TestAdapter.FlagCookie] =
            DraftCookie.Build(TestAdapter.FlagCookie, "bypass-value");

        var response = await server.DisableDraftModeAsync();

        // A 303, not the adapter's redirect: the exit route is a POST, and the
        // browser must follow with a GET.
        Assert.Equal(303, response.Status);
        Assert.Equal("/", response.Location);
        Assert.False(adapter.Flag);
        foreach (var name in new[] { TestAdapter.FlagCookie, CanvasConstants.DraftDataCookieName })
        {
            var cookie = adapter.Cookies[name];
            Assert.Equal(string.Empty, cookie.Value);
            Assert.Equal(DateTimeOffset.UnixEpoch, cookie.Expires);
            Assert.Equal("None", cookie.SameSite);
            Assert.True(cookie.Secure);
            Assert.True(cookie.Partitioned);
        }
    }
}

public class GetDraftDataTests
{
    [Fact]
    public async Task Returns_null_while_the_draft_flag_is_off()
    {
        var (server, adapter, _) = TestData.MakeServer();
        adapter.Cookies[CanvasConstants.DraftDataCookieName] = DraftCookie.Build(
            CanvasConstants.DraftDataCookieName, TestData.LiveDraftData().Serialize());
        Assert.Null(await server.GetDraftDataAsync());
    }

    [Fact]
    public async Task Returns_the_parsed_session_while_the_flag_is_on()
    {
        var (server, adapter, _) = TestData.MakeServer();
        var draftData = TestData.LiveDraftData();
        adapter.SeedSession(draftData);
        Assert.Equal(draftData, await server.GetDraftDataAsync());
    }
}
