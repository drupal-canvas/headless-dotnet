//#region src/constants.ts
/**
* The cookie this SDK uses to carry the draft session (entry path, resource
* version policy, and the user-bound access token) between requests.
*/
const DRAFT_DATA_COOKIE_NAME = "canvas_headless_draft_data";
/**
* The registered grant type URI for JWT bearer assertions (RFC 7523 §2.1).
* The Canvas Headless module implements this grant: it exchanges a
* Drupal-signed preview assertion for an access token bound to the editor
* the assertion names.
*/
const JWT_BEARER_GRANT_TYPE = "urn:ietf:params:oauth:grant-type:jwt-bearer";
/**
* OAuth client id of the consumer the Canvas Headless module provisions at
* install. Fixed on the Drupal side — not site configuration — so the SDK
* carries it as a constant. A public client: there is no client secret
* anywhere in the app; the signed preview assertion is the credential
* (RFC 7523).
*/
const CANVAS_HEADLESS_CLIENT_ID = "canvas_headless";
/** Query parameter selecting a one-component library preview. */
const CANVAS_COMPONENT_PREVIEW_QUERY = "componentId";
/** App route reserved for the isolated one-component preview document. */
const CANVAS_COMPONENT_PREVIEW_PATH = "/api/canvas/component-preview";
/**
* The host ↔ app draft-preview protocol message types.
*
* The embedded app cannot renew its own session — its requests to Drupal are
* cross-site in the ancestor chain, so the editor's SameSite=Lax session
* cookie never accompanies them. The embedding host page (the Canvas editor)
* *does* hold that session, so renewal is a relayed conversation over
* postMessage. These string values are the contract between the two sides:
* the app side is implemented by the draft session state machine in this
* package's `client` entry, the host side by @drupal-canvas/headless-host
* (which re-exports these constants).
*/
/** Host → app: establish the current iframe document's protocol session. */
const HEADLESS_STATUS_REQUEST_MESSAGE = "canvas-headless:status-request";
/** App → host: draft session state, sent on load and on every change. */
const HEADLESS_STATUS_MESSAGE = "canvas-headless:status";
/** App → host: mint a fresh assertion (sent before the token expires). */
const HEADLESS_RENEW_REQUEST_MESSAGE = "canvas-headless:renew-request";
/** Host → app: a freshly minted assertion, to redeem in place. */
const HEADLESS_ASSERTION_MESSAGE = "canvas-headless:assertion";
/** Host → app: refresh content after Canvas persisted a new auto-save. */
const HEADLESS_REFRESH_MESSAGE = "canvas-headless:refresh";
/** App → host: confirms that a numbered refresh command was received. */
const HEADLESS_REFRESH_ACK_MESSAGE = "canvas-headless:refresh-ack";
/** Host → app: complete the trusted geometry-channel handshake. */
const HEADLESS_GEOMETRY_REQUEST_MESSAGE = "canvas-headless:geometry-request";
/** App → host: one unchanged shared-library geometry snapshot. */
const HEADLESS_GEOMETRY_MESSAGE = "canvas-headless:geometry";
/**
* App → host: current rendered content height, in CSS pixels. Sent on load
* and on every ResizeObserver-detected change.
*/
const HEADLESS_HEIGHT_MESSAGE = "canvas-headless:height";
/** App → host: temporarily resize the iframe for viewport-height probing. */
const HEADLESS_HEIGHT_PROBE_MESSAGE = "canvas-headless:height-probe";
/** Host → app: the requested probe height has been applied. */
const HEADLESS_HEIGHT_PROBE_READY_MESSAGE = "canvas-headless:height-probe-ready";
/** Host → app: the base height of the selected preview viewport. */
const HEADLESS_VIEWPORT_HEIGHT_MESSAGE = "canvas-headless:viewport-height";
//#endregion
//#region src/draft-data.ts
/**
* Returns the exact editor origin carried by the redeemed assertion's
* signed renewal URL. Only HTTP(S) URLs without credentials are accepted.
*/
function getDraftEditorOrigin(draftData) {
	if (!draftData) return null;
	try {
		const renewUrl = new URL(draftData.renewUrl);
		if (renewUrl.protocol !== "http:" && renewUrl.protocol !== "https:" || renewUrl.username !== "" || renewUrl.password !== "") return null;
		return renewUrl.origin;
	} catch {
		return null;
	}
}
/**
* How much earlier than `tokenExpiresAt` a session counts as expired, so
* nothing acts on a token that will be dead by the time a request reaches
* Drupal. The client-side state machine applies the same slack, so the
* client flips to "expired" at the same moment the server would.
*/
const EXPIRY_SLACK_MS = 5e3;
/**
* Parses and validates a serialized draft-data cookie value. Returns null
* for missing, malformed, or incomplete data — an unreadable session is
* treated as no session.
*/
function parseDraftData(value) {
	if (!value) return null;
	try {
		const data = JSON.parse(value);
		if (typeof data.path !== "string" || typeof data.resourceVersion !== "string" || data.previewContext !== void 0 && (typeof data.previewContext !== "object" || data.previewContext === null || data.previewContext.viewMode !== void 0 && typeof data.previewContext.viewMode !== "string") || typeof data.sub !== "string" || typeof data.renewUrl !== "string" || typeof data.accessToken !== "string" || typeof data.tokenType !== "string" || typeof data.tokenExpiresAt !== "number" || typeof data.codeVerifier !== "string") return null;
		return data;
	} catch {
		return null;
	}
}
/**
* Serializes a draft session for cookie storage; parseDraftData() reverses
* it.
*/
function serializeDraftData(draftData) {
	return JSON.stringify(draftData);
}
/**
* Whether the draft session's access token has expired.
*
* An expired session is surfaced, never silently downgraded: pages fall
* back to what anonymous visitors can see while the draft indicator
* explains that the preview session ended.
*/
function isDraftSessionExpired(draftData, now = Date.now()) {
	return now >= draftData.tokenExpiresAt - EXPIRY_SLACK_MS;
}
//#endregion
export { JWT_BEARER_GRANT_TYPE as S, HEADLESS_REFRESH_MESSAGE as _, serializeDraftData as a, HEADLESS_STATUS_REQUEST_MESSAGE as b, CANVAS_HEADLESS_CLIENT_ID as c, HEADLESS_GEOMETRY_MESSAGE as d, HEADLESS_GEOMETRY_REQUEST_MESSAGE as f, HEADLESS_REFRESH_ACK_MESSAGE as g, HEADLESS_HEIGHT_PROBE_READY_MESSAGE as h, parseDraftData as i, DRAFT_DATA_COOKIE_NAME as l, HEADLESS_HEIGHT_PROBE_MESSAGE as m, getDraftEditorOrigin as n, CANVAS_COMPONENT_PREVIEW_PATH as o, HEADLESS_HEIGHT_MESSAGE as p, isDraftSessionExpired as r, CANVAS_COMPONENT_PREVIEW_QUERY as s, EXPIRY_SLACK_MS as t, HEADLESS_ASSERTION_MESSAGE as u, HEADLESS_RENEW_REQUEST_MESSAGE as v, HEADLESS_VIEWPORT_HEIGHT_MESSAGE as x, HEADLESS_STATUS_MESSAGE as y };
