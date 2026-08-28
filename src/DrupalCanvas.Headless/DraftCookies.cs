namespace DrupalCanvas.Headless;

/// <summary>
/// A draft session cookie, carrying the full attribute set explicitly so the
/// framework adapter can emit it verbatim.
///
/// Cookies default to SameSite=Lax, which browsers do not send in cross-site
/// iframe requests (the Drupal previewer) — draft state would silently stay
/// off inside the iframe, so the cookies are set cross-site
/// (SameSite=None; Secure). httpOnly and path are stated explicitly rather
/// than inherited: the token-carrying cookies must not depend on other
/// attributes happening to ride along. Partitioned (CHIPS) opts into the
/// per-top-level-site cookie jar, which is what lets browsers with
/// third-party-cookie restrictions accept these cookies inside the iframe.
/// Requires a secure (HTTPS) origin.
/// </summary>
public sealed record DraftCookie
{
    public required string Name { get; init; }

    public required string Value { get; init; }

    public bool HttpOnly { get; init; } = true;

    public string Path { get; init; } = "/";

    /// <summary>Always SameSite=None; see the type remarks.</summary>
    public string SameSite { get; init; } = "None";

    public bool Secure { get; init; } = true;

    public bool Partitioned { get; init; } = true;

    public DateTimeOffset? Expires { get; init; }

    /// <summary>Builds a draft session cookie carrying the full cross-site attribute set.</summary>
    public static DraftCookie Build(string name, string value) => new() { Name = name, Value = value };

    /// <summary>
    /// Builds the deletion counterpart of a draft session cookie.
    ///
    /// A deletion is a Set-Cookie with an expiry in the past — and the browser
    /// only applies it to a cookie whose identity matches, which for CHIPS
    /// cookies includes the partition. Framework-level deletions that omit
    /// Partitioned target an unpartitioned cookie that does not exist, and the
    /// real one survives — draft mode would be impossible to exit. Setting the
    /// cookie to an empty value, already expired (epoch), with the exact
    /// attributes <see cref="Build"/> used produces deletions that match the
    /// cookies actually stored. curl-based tests cannot catch a regression
    /// here: curl's cookie jar has no partitioning, so attribute-less
    /// deletions work there. Verify exits in a browser.
    /// </summary>
    public static DraftCookie BuildCleared(string name)
        => new() { Name = name, Value = string.Empty, Expires = DateTimeOffset.UnixEpoch };
}
