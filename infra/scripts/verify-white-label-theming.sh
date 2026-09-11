#!/usr/bin/env bash
# =============================================================================
# Klara Home - white-label theming proof (Step 30 deliverable)
# =============================================================================
# Proves, against a running dev stack, that a second tenant can be re-themed end to end by
# changing configuration only - no code change, no rebuild of the storefront, API or admin image,
# PROVIDED the admin image already carries the per-tenant theme wiring this script's step 5 checks
# for (it does, once `docker compose build admin` has been run after that wiring landed).
#
# What it does:
#   1. Reads the current `branding` settings document (the "before" state).
#   2. Writes a demo tenant's branding over it directly in Postgres - a different hue family
#      (cool indigo/teal) from Klara Home's warm-earth ramp, plus a different store name, so the
#      difference is unmistakable rather than a shade of the same idea.
#   3. Restarts the API container only to drop its in-process settings cache (StoreSettingsService,
#      5-minute TTL) immediately rather than waiting for it to expire - not a rebuild, the image is
#      untouched.
#   4. Fetches `/store/config` and the storefront's server-rendered home page, and asserts the new
#      tenant's CSS custom properties are present on `<html style="...">` and the new store name is
#      in the rendered `<title>`.
#   5. Does the admin app's equivalent check. The admin is CSR-only (no server render), so there is
#      no HTML response to grep; `check-admin-theme-tokens.mjs` drives a real headless browser
#      against the admin's sign-in screen instead and asserts the tokens land on
#      `document.documentElement` after `provideAppInitializer` runs. This step is skipped, with a
#      clear note, if `$PROJECT`-admin is not running or `src/frontend`'s `playwright` browsers are
#      not installed - it is additive to the storefront proof, not a hard requirement of it.
#   6. Restores the original branding document and restarts the API again, leaving the stack as it
#      found it.
#
# Requires the dev stack up (infra/scripts/dev.sh up) and docker on PATH. Step 5 additionally
# requires `npx playwright install chromium-headless-shell` to have been run once against
# `src/frontend` (see that directory's devDependencies).
#
#   ./infra/scripts/verify-white-label-theming.sh
#
# See docs/steps/step-30-design-system-theming-and-visual-identity.md for what this proves and its
# caveats, and docs/10-design-system.md §6 for the mechanism itself.
# =============================================================================
set -euo pipefail

DEV_DOMAIN="${DEV_DOMAIN:-klarahome.localhost}"
PROJECT="${COMPOSE_PROJECT_NAME:-klarahome-dev}"
POSTGRES_CONTAINER="${PROJECT}-postgres"
API_CONTAINER="${PROJECT}-api"
ADMIN_CONTAINER="${PROJECT}-admin"
POSTGRES_USER="${POSTGRES_USER:-klarahome}"
POSTGRES_DB="${POSTGRES_DB:-klarahome}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-klarahome_dev_password}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FRONTEND_DIR="$SCRIPT_DIR/../../src/frontend"

log() { echo "[white-label-proof] $*"; }

psql_exec() {
  docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" "$POSTGRES_CONTAINER" \
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc "$1"
}

wait_for_api_healthy() {
  for _ in $(seq 1 30); do
    status=$(docker inspect -f '{{.State.Health.Status}}' "$API_CONTAINER" 2>/dev/null || echo "")
    [ "$status" = "healthy" ] && return 0
    sleep 2
  done
  echo "error: $API_CONTAINER did not become healthy" >&2
  return 1
}

BEFORE=$(psql_exec "select value from platform.store_settings where key='branding';")
if [ -z "$BEFORE" ]; then
  echo "error: no branding document found - has the dev stack been seeded?" >&2
  exit 1
fi
log "captured the current branding document as the restore point"

DEMO_BRANDING='{
  "logoRef": "", "logoDarkRef": "", "faviconRef": "",
  "primaryColor": "#3949ab", "accentColor": "#14b8a6",
  "storeName": "Nimbus Living",
  "tagline": "Modern living, delivered calm.",
  "themeTokens": {
    "--color-bg": "#eef2fb",
    "--color-surface": "#e2e9f9",
    "--color-surface-sunken": "#d3ddf5",
    "--color-border": "#b8c4e8",
    "--color-border-strong": "#5b6ecf",
    "--color-text": "#1a1f3d",
    "--color-text-muted": "#3949ab",
    "--color-text-subtle": "#4a5aa8",
    "--color-primary": "#3949ab",
    "--color-primary-hover": "#2c3785",
    "--color-primary-active": "#1a1f3d",
    "--color-accent": "#14b8a6",
    "--color-focus-ring": "#1a1f3d"
  }
}'
DEMO_BRANDING_JSON=$(echo "$DEMO_BRANDING" | tr -d '\n' | sed "s/'/''/g")

log "writing the demo tenant's branding (indigo/teal, 'Nimbus Living') - configuration only"
psql_exec "update platform.store_settings set value = '${DEMO_BRANDING_JSON}'::jsonb where key='branding';" >/dev/null

log "restarting the API container to drop its settings cache (not rebuilding the image)"
docker restart "$API_CONTAINER" >/dev/null
wait_for_api_healthy

restore() {
  log "restoring the original branding document"
  ESCAPED_BEFORE=$(echo "$BEFORE" | sed "s/'/''/g")
  psql_exec "update platform.store_settings set value = '${ESCAPED_BEFORE}'::jsonb where key='branding';" >/dev/null
  docker restart "$API_CONTAINER" >/dev/null
  wait_for_api_healthy
  log "restored; stack is back to its Klara Home branding"
}
trap restore EXIT

log "fetching /store/config"
CONFIG=$(curl -sk "https://api.${DEV_DOMAIN}/api/v1/store/config")
echo "$CONFIG" | grep -q '"storeName":"Nimbus Living"' \
  || { echo "FAIL: /store/config did not echo the demo tenant's store name" >&2; exit 1; }
echo "$CONFIG" | grep -q '"--color-primary":"#3949ab"' \
  || { echo "FAIL: /store/config did not echo the demo tenant's theme tokens" >&2; exit 1; }
log "PASS: /store/config carries the demo tenant's branding and theme tokens"

log "fetching the storefront's server-rendered home page"
HOME_HTML=$(curl -sk "https://${DEV_DOMAIN}/")
echo "$HOME_HTML" | grep -q '<title>Home | Nimbus Living</title>' \
  || { echo "FAIL: rendered <title> does not carry the demo tenant's store name" >&2; exit 1; }
echo "$HOME_HTML" | grep -oq '<html[^>]*style="[^"]*--color-primary: #3949ab' \
  || { echo "FAIL: rendered <html> does not carry the demo tenant's --color-primary" >&2; exit 1; }
echo "$HOME_HTML" | grep -oq '<html[^>]*style="[^"]*--color-accent: #14b8a6' \
  || { echo "FAIL: rendered <html> does not carry the demo tenant's --color-accent" >&2; exit 1; }
log "PASS: the server-rendered HTML carries the demo tenant's tokens on <html style=\"...\">"

if ! docker inspect "$ADMIN_CONTAINER" >/dev/null 2>&1; then
  log "SKIP: $ADMIN_CONTAINER is not running - skipping the admin theme-token check"
elif [ ! -d "$FRONTEND_DIR/node_modules/playwright" ]; then
  log "SKIP: src/frontend's playwright devDependency is not installed - skipping the admin check"
else
  log "checking the admin app (CSR-only: no server render, so this drives a real headless browser"
  log "  against the sign-in screen and asserts the tokens land on <html> after bootstrap)"
  if node "$FRONTEND_DIR/scripts/check-admin-theme-tokens.mjs" \
      "https://admin.${DEV_DOMAIN}/login" \
      '{"--color-primary":"#3949ab","--color-accent":"#14b8a6"}'; then
    log "PASS: the admin app's bootstrap applies the demo tenant's theme tokens too"
  else
    echo "FAIL: the admin app did not apply the demo tenant's theme tokens after bootstrap" >&2
    exit 1
  fi
fi

log "PASS: white-label proof complete - a second, visually distinct theme was applied end to end"
log "      with zero code changes and zero rebuild between the 'before' and 'after' checks in this"
log "      run (the API, storefront and admin images were not touched here; only"
log "      platform.store_settings changed). That requires the admin image to already carry the"
log "      per-tenant theme wiring (src/frontend/apps/admin/src/app/app.config.ts) - rebuild it once"
log "      (docker compose build admin) if this check reports the admin app has stale defaults."
