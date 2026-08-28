namespace DrupalCanvas.Headless.Tests;

public class CspTests
{
    [Fact]
    public void Frame_ancestors_is_self_only_without_a_draft_session()
        => Assert.Equal("'self'", Csp.ResolveFrameAncestors(null));

    [Fact]
    public void Frame_ancestors_admits_the_exact_editor_origin_of_a_session()
        => Assert.Equal(
            "'self' https://drupal.example",
            Csp.ResolveFrameAncestors(TestData.LiveDraftData()));

    [Fact]
    public void An_invalid_renew_url_keeps_the_policy_self_only()
    {
        var draftData = TestData.LiveDraftData() with { RenewUrl = "javascript:alert(1)" };
        Assert.Equal("'self'", Csp.ResolveFrameAncestors(draftData));
    }

    [Fact]
    public void Merge_appends_a_policy_when_none_defines_frame_ancestors()
        => Assert.Equal(
            ["default-src 'self'", "frame-ancestors 'self'"],
            Csp.MergeFrameAncestors(["default-src 'self'"], "'self'"));

    [Fact]
    public void Merge_keeps_an_application_owned_frame_ancestors_authoritative()
        => Assert.Equal(
            ["frame-ancestors https://app.example"],
            Csp.MergeFrameAncestors(["frame-ancestors https://app.example"], "'self'"));

    [Fact]
    public void Merge_splits_comma_separated_policy_lists()
        => Assert.Equal(
            ["default-src 'self'", "img-src *", "frame-ancestors 'self'"],
            Csp.MergeFrameAncestors(["default-src 'self', img-src *"], "'self'"));

    [Fact]
    public void Merge_detects_frame_ancestors_in_any_policy_of_a_list()
        => Assert.Equal(
            ["default-src 'self'", "frame-ancestors https://a.example"],
            Csp.MergeFrameAncestors(
                ["default-src 'self', frame-ancestors https://a.example"], "'self'"));

    [Fact]
    public void Merge_starts_from_nothing()
        => Assert.Equal(
            ["frame-ancestors 'self' https://drupal.example"],
            Csp.MergeFrameAncestors(null, "'self' https://drupal.example"));

    [Fact]
    public void A_directive_merely_prefixed_with_frame_ancestors_does_not_count()
        => Assert.Equal(
            ["frame-ancestors-x y", "frame-ancestors 'self'"],
            Csp.MergeFrameAncestors(["frame-ancestors-x y"], "'self'"));
}
