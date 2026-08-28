using System.Text.RegularExpressions;

namespace DrupalCanvas.Headless;

/// <summary>
/// The <c>frame-ancestors</c> policy the framework binding sends, and its
/// merge into Content-Security-Policy header values the application may
/// already have set. Middleware must never replace an existing policy
/// wholesale: directives such as default-src and script-src belong to the
/// app, and discarding them would silently weaken its security posture.
/// </summary>
public static partial class Csp
{
    /// <summary>
    /// The frame-ancestors source list: <c>'self'</c> always, plus the exact
    /// editor origin from a draft session's signed renewal URL. Without a
    /// draft session, or when its URL is invalid, the policy remains
    /// 'self'-only. (<c>'none'</c> cannot be combined with other sources, so
    /// it is not used as the fallback.)
    /// </summary>
    public static string ResolveFrameAncestors(DraftData? draftData)
    {
        var editorOrigin = DraftData.GetDraftEditorOrigin(draftData?.RenewUrl);
        return editorOrigin is null ? "'self'" : $"'self' {editorOrigin}";
    }

    /// <summary>Whether any policy already defines its own frame-ancestors directive.</summary>
    public static bool HasFrameAncestors(IEnumerable<string?>? policies)
        => (policies ?? []).Any(value => (value ?? string.Empty)
            .Split(',')
            .Any(policy => policy
                .Split(';')
                .Any(part => FrameAncestorsDirective().IsMatch(part.Trim()))));

    [GeneratedRegex(@"^frame-ancestors(\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex FrameAncestorsDirective();

    /// <summary>
    /// Merges a frame-ancestors directive into existing
    /// Content-Security-Policy header values, preserving every other directive
    /// of every policy.
    ///
    /// CSP headers may repeat: multiple header fields, or one field carrying a
    /// comma-separated policy list, all mean several policies, each enforced
    /// independently. An application-owned frame-ancestors directive therefore
    /// remains authoritative: when one is present, the existing policies are
    /// returned unchanged. When none is present, the SDK appends its directive
    /// as one more policy. Commas cannot appear inside directive values, so
    /// splitting on them is safe.
    ///
    /// Returns the policy list; single-header-line consumers join it with
    /// <c>", "</c> (the standard serialization of repeated fields).
    /// </summary>
    public static IReadOnlyList<string> MergeFrameAncestors(
        IEnumerable<string?>? existingPolicies,
        string frameAncestors)
    {
        var policies = (existingPolicies ?? [])
            .SelectMany(value => (value ?? string.Empty).Split(','))
            .Select(policy => policy.Trim())
            .Where(policy => policy.Length > 0)
            .ToList();
        if (HasFrameAncestors(policies))
        {
            return policies;
        }
        policies.Add($"frame-ancestors {frameAncestors}");
        return policies;
    }
}
