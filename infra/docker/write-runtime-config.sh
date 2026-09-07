#!/bin/sh
# =============================================================================
# Klara Home - runtime configuration for the two browser bundles
# =============================================================================
# Both Angular apps are built once and run everywhere: what differs between a
# laptop and production is a config.json served beside the bundle, which the
# browser fetches BEFORE the app bootstraps (docs/05-frontend-architecture.md
# §5). In a container that file is written from the KH_* environment variables
# at start-up - this script - and in development it is the checked-in
# apps/<app>/public/config.json.
#
# The names, the defaults and the feature-flag encoding are deliberately the
# same ones libs/util/src/lib/runtime-config.server.ts reads, because the
# storefront ALSO renders on Node, where there is no file to fetch. If the two
# disagree, a page renders against one API on the server and another in the
# browser, and the only symptom is a hydration mismatch.
#
#   Usage: write-runtime-config /path/to/config.json
# =============================================================================
set -eu

target="${1:?usage: write-runtime-config <path>}"

# Escapes the two characters that cannot appear raw inside a JSON string.
#
# One substitution rather than two: a bracket expression matches either character
# and `&` puts it back after the backslash, so there is no order to get wrong -
# escaping the backslashes first and the quotes second doubles the backslash that
# the second rule has just added, which is a bug that only shows up in a value
# nobody puts in a config file until the day they do.
json_string() {
    printf '%s' "$1" | sed 's/[\\"]/\\&/g'
}

# No trailing slash: every generated client method carries its own /api/v1/...
# prefix, so a base URL that ends in one produces a double slash on every call.
api_base_url="$(printf '%s' "${KH_API_BASE_URL:-https://api.klarahome.localhost}" | sed -e 's:/*$::')"

# Feature flags arrive as KH_FEATURE_<key>, where `__` is a dot and `_` is a
# hyphen: KH_FEATURE_search__external_engine is "search.external-engine". The
# substitution parks the dot separator on an `@` while the hyphens are applied,
# and puts it back afterwards - which is what makes the rule reverse cleanly.
# `@` is safe as the placeholder precisely because it is not a character an
# environment variable name may contain, so it cannot already be in the key.
features=''
for name in $(env | sed -n 's/^\(KH_FEATURE_[A-Za-z0-9_]*\)=.*/\1/p'); do
    key="$(printf '%s' "${name#KH_FEATURE_}" | sed -e 's/__/@/g' -e 's/_/-/g' -e 's/@/./g')"
    value="$(eval "printf '%s' \"\${$name}\"")"
    [ "$value" = 'true' ] && value='true' || value='false'
    features="${features:+$features, }\"$(json_string "$key")\": $value"
done

cat > "$target" <<JSON
{
  "apiBaseUrl": "$(json_string "$api_base_url")",
  "tenantCode": "$(json_string "${KH_TENANT_CODE:-klarahome}")",
  "locale": "$(json_string "${KH_LOCALE:-en-IN}")",
  "timeZone": "$(json_string "${KH_TIME_ZONE:-Asia/Kolkata}")",
  "environment": "$(json_string "${KH_ENVIRONMENT:-production}")",
  "features": { $features },
  "analytics": {
    "provider": "$(json_string "${KH_ANALYTICS_PROVIDER:-}")",
    "measurementId": "$(json_string "${KH_ANALYTICS_MEASUREMENT_ID:-}")"
  },
  "imageBaseUrl": "$(json_string "${KH_IMAGE_BASE_URL:-}")"
}
JSON
