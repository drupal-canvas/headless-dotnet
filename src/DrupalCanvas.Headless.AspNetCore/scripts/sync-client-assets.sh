#!/usr/bin/env bash
# Syncs the browser-side assets of @drupal-canvas/headless into this
# package's wwwroot. The client entry (the <canvas-draft-session> element,
# renewal state machine, geometry bridge, and height reporting) is
# framework-agnostic browser ESM with relative-only imports, so the files are
# served verbatim as static web assets — no bundler involved.
#
# Usage: scripts/sync-client-assets.sh <path-to-@drupal-canvas/headless-dist>
# e.g.:  scripts/sync-client-assets.sh node_modules/@drupal-canvas/headless/dist
#
# After syncing, update the chunk file names referenced from
# wwwroot/client/index.js if the content hashes changed, and record the npm
# package version in PINNED_CLIENT_VERSION below.

PINNED_CLIENT_VERSION="0.5.0"

set -euo pipefail

DIST="${1:?path to @drupal-canvas/headless dist directory}"
HERE="$(cd "$(dirname "$0")/.." && pwd)"

mkdir -p "$HERE/wwwroot/client"
cp "$DIST/client/index.js" "$HERE/wwwroot/client/index.js"
cp "$DIST/preview.css" "$HERE/wwwroot/preview.css"
# The shared chunks client/index.js imports (../<chunk>.js); copy every
# top-level chunk so hashed names keep resolving.
find "$DIST" -maxdepth 1 -name "*-*.js" -exec cp {} "$HERE/wwwroot/" \;

echo "Synced client assets (pinned npm version: $PINNED_CLIENT_VERSION)."
