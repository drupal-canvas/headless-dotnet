namespace DrupalCanvas.Headless.Tests;

public class DraftDataTests
{
    [Fact]
    public void Serialize_and_parse_round_trip()
    {
        var draftData = TestData.LiveDraftData(previewContext: new DraftPreviewContext
        {
            ViewMode = "teaser",
        });
        Assert.Equal(draftData, DraftData.Parse(draftData.Serialize()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Parse_answers_null_for_missing_or_malformed_values(string? value)
        => Assert.Null(DraftData.Parse(value));

    [Fact]
    public void Parse_rejects_a_wrongly_typed_field()
    {
        var json = TestData.LiveDraftData().Serialize()
            .Replace("\"tokenType\":\"Bearer\"", "\"tokenType\":7");
        Assert.Null(DraftData.Parse(json));
    }

    [Fact]
    public void Parse_rejects_a_wrongly_typed_preview_context_member()
    {
        var draftData = TestData.LiveDraftData();
        var json = draftData.Serialize().Replace(
            "\"sub\":", "\"previewContext\":{\"viewMode\":1},\"sub\":");
        Assert.Null(DraftData.Parse(json));
    }

    [Fact]
    public void Parse_accepts_an_empty_preview_context()
    {
        var draftData = TestData.LiveDraftData();
        var json = draftData.Serialize().Replace(
            "\"sub\":", "\"previewContext\":{},\"sub\":");
        var parsed = DraftData.Parse(json);
        Assert.NotNull(parsed?.PreviewContext);
        Assert.Null(parsed!.PreviewContext!.ViewMode);
    }

    [Fact]
    public void A_session_expires_with_slack_before_the_token_does()
    {
        var draftData = TestData.LiveDraftData(tokenExpiresAt: 100_000);
        Assert.False(draftData.IsExpired(nowUnixMs: 94_999));
        Assert.True(draftData.IsExpired(nowUnixMs: 95_000));
    }

    [Theory]
    [InlineData("https://drupal.example/canvas-headless/renew", "https://drupal.example")]
    [InlineData("https://drupal.example:8443/renew", "https://drupal.example:8443")]
    [InlineData("http://localhost:3000/renew", "http://localhost:3000")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("https://user:pass@drupal.example/renew", null)]
    [InlineData("not a url", null)]
    [InlineData(null, null)]
    public void GetDraftEditorOrigin_accepts_only_credentialless_http_urls(
        string? renewUrl, string? expected)
        => Assert.Equal(expected, DraftData.GetDraftEditorOrigin(renewUrl));
}

public class PkceTests
{
    [Fact]
    public void Verifiers_meet_the_rfc7636_minimum_length_and_are_unique()
    {
        var verifier = Pkce.GenerateCodeVerifier();
        Assert.Equal(43, verifier.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
        Assert.NotEqual(verifier, Pkce.GenerateCodeVerifier());
    }

    [Fact]
    public void Challenge_matches_the_rfc7636_appendix_b_vector()
        => Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.ComputeCodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
}

public class AssertionClaimsTests
{
    [Fact]
    public void Decodes_the_claim_set_without_verification()
    {
        var assertion = TestData.BuildAssertion(TestData.ValidClaims);
        var claims = AssertionClaims.Decode(assertion);
        Assert.Equal("/node/1", AssertionClaims.GetString(claims, "path"));
        Assert.Equal("42", AssertionClaims.GetString(claims, "sub"));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("one.two")]
    [InlineData("a.!!!.c")]
    public void Answers_null_for_malformed_assertions(string assertion)
        => Assert.Null(AssertionClaims.Decode(assertion));

    [Fact]
    public void Answers_null_for_a_non_object_claim_set()
    {
        var assertion = "e30." + System.Buffers.Text.Base64Url.EncodeToString(
            System.Text.Encoding.UTF8.GetBytes("[1,2]")) + ".sig";
        Assert.Null(AssertionClaims.Decode(assertion));
    }
}
