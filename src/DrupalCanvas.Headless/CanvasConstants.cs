namespace DrupalCanvas.Headless;

/// <summary>
/// Protocol constants shared with the JavaScript SDK (@drupal-canvas/headless).
/// The values are a cross-implementation contract; every one of them must stay
/// byte-identical with the npm package's <c>constants.ts</c>.
/// </summary>
public static class CanvasConstants
{
    /// <summary>
    /// The cookie carrying the draft session (entry path, resource version
    /// policy, and the user-bound access token) between requests.
    /// </summary>
    public const string DraftDataCookieName = "canvas_headless_draft_data";

    /// <summary>
    /// The registered grant type URI for JWT bearer assertions (RFC 7523 §2.1).
    /// The Canvas Headless module implements this grant: it exchanges a
    /// Drupal-signed preview assertion for an access token bound to the editor
    /// the assertion names.
    /// </summary>
    public const string JwtBearerGrantType = "urn:ietf:params:oauth:grant-type:jwt-bearer";

    /// <summary>
    /// OAuth client id of the consumer the Canvas Headless module provisions at
    /// install. Fixed on the Drupal side — not site configuration. A public
    /// client: there is no client secret anywhere in the app; the signed
    /// preview assertion is the credential (RFC 7523).
    /// </summary>
    public const string CanvasHeadlessClientId = "canvas_headless";

    /// <summary>Query parameter selecting a one-component library preview.</summary>
    public const string ComponentPreviewQuery = "componentId";

    /// <summary>App route reserved for the isolated one-component preview document.</summary>
    public const string ComponentPreviewPath = "/api/canvas/component-preview";

    /// <summary>The CSS class for an empty Canvas slot placeholder.</summary>
    public const string EmptySlotPlaceholderClass = "canvas--slot-empty-placeholder";

    /// <summary>The CSS class for an empty Canvas region placeholder.</summary>
    public const string EmptyRegionPlaceholderClass = "canvas--region-empty-placeholder";

    /// <summary>
    /// The payload format version of the component metadata endpoint. The
    /// Drupal-side reader hard-fails on an unknown version instead of
    /// mis-parsing — a cross-repo, cross-deploy contract shared with the
    /// JavaScript SDK's COMPONENT_METADATA_PAYLOAD_VERSION.
    /// </summary>
    public const int ComponentMetadataPayloadVersion = 1;
}
