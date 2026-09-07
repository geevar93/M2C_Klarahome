# =============================================================================
# Klara Home - admin image (static SPA)
# =============================================================================
# Per docs/06-infrastructure-devops.md §3: nginx:alpine over the build output
# plus a hardened configuration. The admin app does not server-render - it is a
# back office behind a login, so there is nothing for a crawler to read and
# nothing to gain from rendering it twice.
#
# Build context is the REPOSITORY ROOT:
#   docker build -f infra/docker/admin.Dockerfile -t klarahome/admin:dev .
#
# One image, every environment: the entrypoint writes config.json from the KH_*
# variables at start-up (docs/05-frontend-architecture.md §5).
# =============================================================================

# --- Build -------------------------------------------------------------------
# Node 24 for the reason storefront.Dockerfile gives: the workspace is pinned to
# 24.20.0 and building it on another major is a difference nobody would find.
FROM node:26-alpine AS build
WORKDIR /src

COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/frontend/ ./

ENV NX_DAEMON=false \
    NX_REJECT_UNKNOWN_LOCAL_CACHE=0
RUN npx nx build admin --configuration=production --skip-nx-cache

# --- Runtime -----------------------------------------------------------------
FROM nginx:alpine AS runtime

ARG VERSION=0.1.0
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="klarahome-admin" \
      org.opencontainers.image.description="Klara Home admin back office (Angular)" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.source="https://github.com/klarahome/klarahome"

COPY --from=build /src/dist/apps/admin/browser/ /usr/share/nginx/html/
COPY infra/docker/admin.nginx.conf /etc/nginx/conf.d/default.conf
COPY infra/docker/write-runtime-config.sh /usr/local/bin/write-runtime-config
RUN chmod +x /usr/local/bin/write-runtime-config \
    && rm -f /usr/share/nginx/html/index.html.default

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/config.json || exit 1

ENTRYPOINT ["/bin/sh", "-c", "write-runtime-config /usr/share/nginx/html/config.json && exec nginx -g 'daemon off;'"]
