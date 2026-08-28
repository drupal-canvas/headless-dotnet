/**
 * Client-side wiring of the draft session, the ASP.NET Core counterpart of
 * the Astro adapter's DraftSession script: defines the
 * <canvas-draft-session> custom element from @drupal-canvas/headless/client
 * (which owns the renewal protocol with the embedding Canvas editor, expiry
 * timing, and content-height reporting) and refreshes the page when Canvas
 * auto-saves. Blazor static SSR has no in-place data refresh primitive, so
 * the refresh handler reloads the document.
 */
import {
  defineDraftSessionElement,
  DRAFT_SESSION_REFRESH_EVENT,
} from './client/index.js';

defineDraftSessionElement();

document.addEventListener(DRAFT_SESSION_REFRESH_EVENT, (event) => {
  event.preventDefault();
  window.location.reload();
});
