# Drupal Canvas headless template for .NET

> [!NOTE]
> This is an experimental project.

An experimental ASP.NET Core / Blazor starter for frontends built for
[Canvas Headless](https://git.drupalcode.org/project/canvas/-/tree/1.x/modules/canvas_headless).

The Canvas Headless module lets the Drupal Canvas editor embed this app, so
editors preview their work, rendered by the app itself, with the app's
components registered in Canvas.

## Getting started

```bash
cp .env.example .env   # point CANVAS_SITE_URL at your Drupal site
dotnet run
```

The app serves on http://localhost:5210: any Drupal path (e.g. `/page/1`)
resolves through the Canvas content API and renders server-side. Register the
URL as a headless frontend in the Canvas editor's _Headless frontends_ screen
and the editor embeds the app for drag-and-drop editing, syncing the component
library from `Components/canvas/`.

Requires the .NET 10 SDK. Node.js is only needed when changing styles:
`npm install && npm run build:css` regenerates `wwwroot/css/app.css` (Tailwind
CSS 4; the generated file is committed).

## What's included

- **Catch-all Drupal page rendering** (`Program.cs`): every request resolves
  through Drupal, with redirects, document head, and 404s handled.
- **The component library** (`Components/canvas/`): the same selected components
  as the
  [JavaScript templates](https://github.com/drupal-canvas/headless-templates),
  one directory per component with its framework-neutral `component.yml`
  metadata and a Blazor `.razor` implementation. Naming note: where a component
  carries a prop named exactly like itself (`heading`, `image`, `text`,
  `video`), the class is `*Component` and registered explicitly in `Program.cs`,
  because a C# property cannot share its class's name.
- **Draft preview** end to end: activation, PKCE-bound renewal, exit, CHIPS
  cookies for the cross-site editor iframe, editor geometry markers, and the
  `DraftBanner` session chrome.
- **The component metadata endpoint** Canvas syncs the library from, protected
  by proof-by-redemption.

## The packages

`packages/` holds the C# port of the
[`@drupal-canvas/headless` server core](https://git.drupalcode.org/project/canvas/-/tree/1.x/packages/headless/src/server)
and its ASP.NET Core binding — the integration layer the app builds on. They
live in this repository and are referenced as projects:

- **`DrupalCanvas.Headless`** — the framework-agnostic protocol core: draft
  session flows (RFC 7523 assertion redemption at Drupal's token endpoint),
  draft-aware content fetching, rendered-page contracts, render helpers and the
  editor comment-marker format, CSP merging, and `component.yml` discovery.
- **`DrupalCanvas.Headless.AspNetCore`** — endpoints
  (`MapDrupalCanvasHeadless()`), DI (`AddDrupalCanvasHeadless()`,
  `AddDrupalCanvasComponents()`), frame-ancestors middleware, `.env`
  configuration, the Blazor `CanvasComponentTree` renderer, and the npm
  package's browser client served as static assets (pinned at
  `@drupal-canvas/headless@0.5.0`; see `scripts/sync-client-assets.sh`).

### Conformance testing

`packages/tests/DrupalCanvas.Headless.Tests` ports the
[Canvas Headless JavaScript SDK's `flows.test.ts`](https://git.drupalcode.org/project/canvas/-/blob/1.x/packages/headless/src/server/flows.test.ts)
scenario-for-scenario; the AspNetCore suite covers the renderer's marker output
and the endpoints end-to-end, including the CHIPS `Partitioned` cookie
attributes. **Keep the ported suite in step with the JS one** — it is the
contract stopping the two protocol implementations from drifting.

```bash
dotnet test packages/DrupalCanvas.Headless.slnx
```

## Status

Not yet done:

- The isolated one-component preview document (`/api/canvas/component-preview`,
  for editor thumbnails) — the core method
  `DraftServer.FetchComponentPreviewAsync` exists; the route and minimal HTML
  document do not.
- In-place data refresh on Canvas auto-save (the client glue reloads the
  document; Blazor enhanced navigation could do better).
- Live verification of in-place renewal (the token must approach expiry; the
  flow is covered by the ported unit and endpoint tests).
