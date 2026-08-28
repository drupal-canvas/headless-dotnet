namespace DrupalCanvas.Headless;

/// <summary>
/// The framework surface the draft server flows need — the whole of it.
///
/// A framework binding implements this interface plus route mounting;
/// everything else (assertion redemption, cookie contents, claim validation,
/// identity pinning) lives in the framework-agnostic flows.
/// </summary>
public interface IDraftServerAdapter
{
    /// <summary>Reads a request cookie value; null when absent.</summary>
    ValueTask<string?> GetCookieAsync(string name);

    /// <summary>Sets a response cookie with the given attributes.</summary>
    ValueTask SetCookieAsync(DraftCookie cookie);

    /// <summary>Whether the framework's draft/preview flag is on for this request.</summary>
    ValueTask<bool> IsDraftFlagEnabledAsync();

    /// <summary>Turns the framework's draft/preview flag on.</summary>
    ValueTask EnableDraftFlagAsync();

    /// <summary>Turns the framework's draft/preview flag off.</summary>
    ValueTask DisableDraftFlagAsync();

    /// <summary>
    /// Name of the framework's own draft-flag cookie when it sets one that
    /// must be re-set with cross-site (CHIPS) attributes to survive inside the
    /// embedding iframe. Null when the framework has no such cookie.
    /// </summary>
    string? DraftFlagCookieName => null;

    /// <summary>Framework redirect for the draft-mode activation flow.</summary>
    FlowResponse Redirect(string path);
}
