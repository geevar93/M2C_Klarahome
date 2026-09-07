# =============================================================================
# Klara Home - storefront image (SSR)
# =============================================================================
# Per docs/06-infrastructure-devops.md §3: node runtime, SSR server, non-root,
# no secrets in any layer, lockfile copied first so the install layer caches.
#
# Build context is the REPOSITORY ROOT, because .dockerignore is written against
# it and it is what every other image in this repository uses:
#   docker build -f infra/docker/storefront.Dockerfile -t klarahome/storefront:dev .
#
# The image is built ONCE and runs in every environment: what differs is the
# runtime configuration, which the entrypoint writes from KH_* variables at
# start-up (docs/05-frontend-architecture.md §5). There is no per-environment
# build and no baked-in environment file.
# =============================================================================

# --- Build -------------------------------------------------------------------
# 24-alpine, not the 22 the infrastructure doc names: the workspace is pinned to
# Node 24.20.0 (global.json's sibling - .github/workflows/ci.yml), and building
# an Angular 22 workspace on an older major is a difference nobody would find
# until a build failed in CI and passed on a laptop.
FROM node:26-alpine AS build
WORKDIR /src

# Lockfile first: editing a component must not invalidate the install layer.
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/frontend/ ./

# No daemon and no cache in a container - both write into a .nx directory that
# exists for exactly one build and is thrown away with the layer.
ENV NX_DAEMON=false \
    NX_REJECT_UNKNOWN_LOCAL_CACHE=0
RUN npx nx build storefront --configuration=production --skip-nx-cache

# --- Runtime -----------------------------------------------------------------
FROM node:26-alpine AS runtime

ARG VERSION=0.1.0
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="klarahome-storefront" \
      org.opencontainers.image.description="Klara Home storefront (Angular SSR)" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.source="https://github.com/klarahome/klarahome"

ENV NODE_ENV=production \
    PORT=4000

WORKDIR /app

# The server bundle is self-contained - Angular's application builder inlines
# express and every dependency, so there is no node_modules in this stage and
# nothing to install at runtime. Verified: the only bare import across the whole
# server output is `node:module`.
COPY --from=build /src/dist/apps/storefront/ ./

COPY infra/docker/write-runtime-config.sh /usr/local/bin/write-runtime-config
RUN chmod +x /usr/local/bin/write-runtime-config \
    && chown -R node:node /app

# The node image already defines a non-root `node` user (UID 1000).
USER node
EXPOSE 4000

# `/favicon.ico` rather than `/`: it is served by express.static and so proves
# the process is listening without server-rendering a page - which would call
# the API, on a fifteen-second timer, forever.
HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:4000/favicon.ico || exit 1

# The browser bundle reads /config.json before Angular bootstraps; the Node
# renderer reads the same values straight from the environment. Writing the file
# here is what keeps the two halves agreeing.
ENTRYPOINT ["/bin/sh", "-c", "write-runtime-config /app/browser/config.json && exec node server/server.mjs"]
