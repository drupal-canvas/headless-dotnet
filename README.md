# Drupal Canvas Headless SDK for .NET

A C# port of the server side of the [Drupal Canvas Headless
SDK](https://git.drupalcode.org/project/canvas) (`@drupal-canvas/headless`),
plus an ASP.NET Core / Blazor binding — the .NET counterpart of the
`headless-next`, `headless-astro`, `headless-nuxt`, and
`headless-tanstack-start` adapters.

The Canvas Headless module lets the Drupal Canvas editor embed your frontend
app, so editors preview their work — draft content included — rendered by the
app itself, with the app's components registered in Canvas. This repository
makes ASP.NET Core apps (Blazor static SSR) such a frontend.

## Packages

### `DrupalCanvas.Headless`

The framework-agnostic protocol core, a behavior-for-behavior port of the npm
package's server side:

- **Draft session flows** (`DraftServer`): activation (RFC 7523 jwt-bearer
  assertion redemption at Drupal's token endpoint), PKCE-bound in-place
  renewal with identity pinning, and exit. All state lives in partitioned
  (CHIPS) cross-site cookies, reached through an `IDraftServerAdapter`.
- **Content fetching** (`ContentApi`, `DraftServer.FetchPageAsync`): Drupal's
  rendered-content endpoint, carrying the session's user-bound bearer token
  while the draft session is live; expired sessions fall back to anonymous
  fetching, surfaced by the draft indicator rather than silently downgraded.
- **Rendered-page contracts** (`Page`, `PageRedirect`,
  `CanvasComponentTreeElement`, `CanvasSlot`) matching the wire JSON exactly.
- **Render helpers** (`CanvasRender`) and the **comment-marker format**
  (`CommentMarkers`) shared with `@drupal-canvas/preview-geometry`.
- **CSP helpers** (`Csp`): frame-ancestors resolution and non-destructive
  merging.
- **Component metadata** (`ComponentDiscovery`): `canvas.config.json` +
  `component.yml` discovery with JSON-typed YAML parsing, producing the
  payload the components endpoint serves (payload version 1).

### `DrupalCanvas.Headless.AspNetCore`

The ASP.NET Core binding:

- `AddDrupalCanvasHeadless()` / `MapDrupalCanvasHeadless()`: DI registration
  and the conventional routes (`GET /api/draft`, `POST /api/draft/renew`,
  `POST /api/disable-draft`, `GET|OPTIONS /api/canvas/components`).
- `UseDrupalCanvasFrameAncestors()`: CSP middleware.
- `CanvasComponentTree`: the Blazor renderer for Canvas component trees —
  static SSR, editor comment markers, empty slot/region placeholders, prop
  binding onto `[Parameter]` properties (JSON → CLR, `MarkupString` for HTML
  props), and wire slots bound to `RenderFragment` parameters.
- `AddDrupalCanvasComponents()`: the component registry (explicit or
  convention-based via `component.yml` machine names ↔ component class names).
- `DraftSession`: the Blazor component rendering `<canvas-draft-session>`,
  whose browser implementation ships in this package's static assets — the
  npm package's client entry served verbatim (see
  `scripts/sync-client-assets.sh`; pinned at `@drupal-canvas/headless@0.5.0`).

## Usage sketch

```csharp
builder.Configuration.AddDotEnvFile(); // reads .env; real env vars win
builder.Services.AddDrupalCanvasHeadless(options =>
    options.BaseUrl = builder.Configuration["CANVAS_SITE_URL"]);
builder.Services.AddDrupalCanvasComponents(components =>
    components.AddFromAssembly(typeof(Program).Assembly, builder.Environment.ContentRootPath));

var app = builder.Build();
app.UseDrupalCanvasFrameAncestors();
app.MapDrupalCanvasHeadless();
app.MapRazorComponents<App>();
```

In a catch-all page:

```razor
@code {
    var result = await Server.FetchPageAsync(path);
}
@if (result is Page page)
{
    <CanvasComponentTree Tree="page.Content" />
}
<DraftSession />
```

## Conformance testing

`tests/DrupalCanvas.Headless.Tests` ports the JavaScript SDK's
`flows.test.ts` scenario-for-scenario (plus draft-data, PKCE, CSP, render, and
discovery suites); `tests/DrupalCanvas.Headless.AspNetCore.Tests` covers the
Blazor renderer's marker output and the mounted endpoints end-to-end,
including the CHIPS `Partitioned` cookie attributes. **Keep the ported suite
in step with the JS one** — it is the contract stopping the two protocol
implementations from drifting.

```bash
dotnet test
```

## Status and roadmap

Done: protocol core with ported conformance suite; ASP.NET Core endpoints,
middleware, renderer, registry, client assets; the `app/CanvasSample`
app.

Verified live against a Drupal Canvas Headless site inside the Canvas
editor (via `app/CanvasSample`): component sync, draft activation from
a Drupal-minted assertion, CHIPS cookies in the cross-site editor iframe,
marker-driven geometry (region and slot drop targets, component selection
overlays), prop editing with auto-save refresh of working copies,
publishing with marker-free anonymous output, and the exit flow deleting
partitioned cookies in a real browser.

Not yet done:

- The `dotnet` template in
  [canvas-headless-templates](../canvas-headless-templates) (Blazor Web App,
  Nebula components as `.razor`, Tailwind, catch-all Drupal page route).
- The isolated one-component preview document
  (`/api/canvas/component-preview`, for editor thumbnails) — the core method
  `DraftServer.FetchComponentPreviewAsync` exists; the route and minimal HTML
  document do not.
- In-place data refresh on Canvas auto-save (the static-assets glue reloads
  the document; Blazor enhanced navigation could do better).
- Live verification of in-place renewal (the token must approach expiry;
  the flow is covered by the ported unit and endpoint tests).
- NuGet packaging/publishing metadata and CI.
