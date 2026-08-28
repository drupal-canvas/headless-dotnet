namespace DrupalCanvas.Headless;

/// <summary>Configuration of the Drupal side of the integration.</summary>
public sealed record DraftConfig
{
    /// <summary>
    /// Base URL of the Drupal site, without a trailing slash. Only the app's
    /// server uses it; anything the editor's browser must reach on Drupal (the
    /// standalone renew link) arrives as a signed assertion claim instead, so
    /// multi-origin dev topologies need no second URL here.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Resolves the draft configuration, letting an explicit value win over
    /// the CANVAS_SITE_URL environment variable. The OAuth client id is not
    /// configuration at all: the Canvas Headless module provisions its
    /// consumer under a fixed id (<see cref="CanvasConstants.CanvasHeadlessClientId"/>).
    /// </summary>
    public static DraftConfig Resolve(string? baseUrl = null)
    {
        var resolved = baseUrl ?? Environment.GetEnvironmentVariable("CANVAS_SITE_URL");
        if (string.IsNullOrEmpty(resolved))
        {
            throw new InvalidOperationException("CANVAS_SITE_URL must be set.");
        }
        return new DraftConfig { BaseUrl = resolved.TrimEnd('/') };
    }
}
