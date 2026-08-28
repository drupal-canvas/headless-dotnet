# Drupal Canvas headless template for .NET

An ASP.NET Core / Blazor starter for decoupled [Drupal
Canvas](https://git.drupalcode.org/project/canvas) frontends — the .NET
counterpart of the JavaScript starters in canvas-headless-templates. The
Canvas Headless module lets the Drupal Canvas editor embed this app, so
editors preview their work — draft content included — rendered by the app
itself, with the app's components registered in Canvas.

This repository **is** the template: the app lives at the root, and the SDK
packages powering it live under [`packages/`](#the-sdk-packages).

## Getting started

```bash
cp .env.example .env   # point CANVAS_SITE_URL at your Drupal site
dotnet run
```

The app serves on http://localhost:5210: any Drupal path (e.g. `/node/1`)
resolves through the Canvas content API and renders server-side. Register
the URL as a headless frontend in the Canvas editor's *Headless frontends*
screen and the editor embeds the app for drag-and-drop editing, syncing the
component library from `Components/canvas/`.

Requires the .NET 10 SDK. Node.js is only needed when changing styles:
`npm install && npm run build:css` regenerates `wwwroot/css/app.css`
(Tailwind CSS 4; the generated file is committed).

## What's included

- **Catch-all Drupal page rendering** (`Program.cs`): every request resolves
  through Drupal, with redirects, document head, and 404s handled.
- **The component library** (`Components/canvas/`): the same selected
  components as the JavaScript templates, one directory per component with
  its framework-neutral `component.yml` metadata and a Blazor `.razor`
  implementation. Naming note: where a component carries a prop named
  exactly like itself (`heading`, `image`, `text`, `video`), the class is
  `*Component` and registered explicitly in `Program.cs`, because a C#
  property cannot share its class's name.
- **Draft preview** end to end: activation, PKCE-bound renewal, exit, CHIPS
  cookies for the cross-site editor iframe, editor geometry markers, and the
  `DraftBanner` session chrome.
- **The component metadata endpoint** Canvas syncs the library from,
  protected by proof-by-redemption.

## The SDK packages

`packages/` holds the C# port of the `@drupal-canvas/headless` server core
and its ASP.NET Core binding — the layer the template builds on. They are
project-referenced for now and will move to NuGet references once published
(and then likely to their own repository):

- **`DrupalCanvas.Headless`** — the framework-agnostic protocol core: draft
  session flows (RFC 7523 assertion redemption at Drupal's token endpoint),
  draft-aware content fetching, rendered-page contracts, render helpers and
  the editor comment-marker format, CSP merging, and `component.yml`
  discovery.
- **`DrupalCanvas.Headless.AspNetCore`** — endpoints
  (`MapDrupalCanvasHeadless()`), DI (`AddDrupalCanvasHeadless()`,
  `AddDrupalCanvasComponents()`), frame-ancestors middleware, `.env`
  configuration, the Blazor `CanvasComponentTree` renderer, and the npm
  package's browser client served as static assets (pinned at
  `@drupal-canvas/headless@0.5.0`; see `scripts/sync-client-assets.sh`).

### Conformance testing

`packages/tests/DrupalCanvas.Headless.Tests` ports the JavaScript SDK's
`flows.test.ts` scenario-for-scenario; the AspNetCore suite covers the
renderer's marker output and the endpoints end-to-end, including the CHIPS
`Partitioned` cookie attributes. **Keep the ported suite in step with the JS
one** — it is the contract stopping the two protocol implementations from
drifting.

```bash
dotnet test packages/DrupalCanvas.Headless.slnx
```

## Status and roadmap

Verified live against a Drupal Canvas Headless site inside the Canvas
editor: component sync, draft activation from a Drupal-minted assertion,
CHIPS cookies in the cross-site editor iframe, marker-driven geometry,
prop editing with auto-save refresh of working copies, publishing with
marker-free anonymous output, and the exit flow deleting partitioned
cookies in a real browser.

Not yet done:

- The isolated one-component preview document
  (`/api/canvas/component-preview`, for editor thumbnails) — the core method
  `DraftServer.FetchComponentPreviewAsync` exists; the route and minimal
  HTML document do not.
- In-place data refresh on Canvas auto-save (the client glue reloads the
  document; Blazor enhanced navigation could do better).
- Live verification of in-place renewal (the token must approach expiry;
  the flow is covered by the ported unit and endpoint tests).
- NuGet packaging/publishing for the SDK packages, and CI.
