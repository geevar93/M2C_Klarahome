# =============================================================================
# Klara Home - worker image (Step 8)
# =============================================================================
# The background host: the transactional outbox dispatcher (Step 4) and the
# notification queue dispatcher (Step 8). Later steps add shipment polling and
# reconciliation to the same process.
#
# docs/01-architecture.md §4 describes the worker as "the same image, different
# entrypoint". It is a separate image here for the same reason the migrator is:
#   * it needs no ASP.NET Core surface and publishes no port;
#   * it must NOT be reachable, and a shared image with a role variable is one
#     misconfiguration away from serving traffic;
#   * a crash loop in the worker must not be able to take an API replica with it.
# The build stages are otherwise identical, so the layer cache is shared.
#
# Build context is the REPOSITORY ROOT:
#   docker build -f infra/docker/worker.Dockerfile -t klarahome/worker:dev .
# =============================================================================

# --- Build -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH=amd64
WORKDIR /src

# Same SDK band and the same analyzer configuration a developer sees.
COPY global.json .editorconfig ./

# Restore from project files only, so editing a .cs file does not invalidate it.
COPY src/backend/Directory.Build.props src/backend/Directory.Packages.props src/backend/
COPY src/backend/shared/KlaraHome.SharedKernel/KlaraHome.SharedKernel.csproj    src/backend/shared/KlaraHome.SharedKernel/
COPY src/backend/shared/KlaraHome.Contracts/KlaraHome.Contracts.csproj          src/backend/shared/KlaraHome.Contracts/
COPY src/backend/shared/KlaraHome.Infrastructure/KlaraHome.Infrastructure.csproj src/backend/shared/KlaraHome.Infrastructure/
COPY src/backend/modules/KlaraHome.Modules.Platform/KlaraHome.Modules.Platform.csproj src/backend/modules/KlaraHome.Modules.Platform/
COPY src/backend/modules/KlaraHome.Modules.Identity/KlaraHome.Modules.Identity.csproj src/backend/modules/KlaraHome.Modules.Identity/
COPY src/backend/modules/KlaraHome.Modules.Media/KlaraHome.Modules.Media.csproj       src/backend/modules/KlaraHome.Modules.Media/
COPY src/backend/modules/KlaraHome.Modules.Notifications/KlaraHome.Modules.Notifications.csproj src/backend/modules/KlaraHome.Modules.Notifications/
COPY src/backend/host/KlaraHome.Worker/KlaraHome.Worker.csproj                  src/backend/host/KlaraHome.Worker/

RUN dotnet restore src/backend/host/KlaraHome.Worker/KlaraHome.Worker.csproj \
    --runtime linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 )

COPY src/backend/ src/backend/

# Fonts for the PDF renderer (ADR-015). The worker renders documents too - an
# invoice is produced from a background job, not from a request - so it needs the
# same family as the API, and for the same reason: the chiselled runtime image has
# no fonts and no way to install one.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

# No ReadyToRun. The worker starts once and runs for weeks; pre-compilation would
# trade image size and build time for a startup saving nobody experiences.
RUN dotnet publish src/backend/host/KlaraHome.Worker/KlaraHome.Worker.csproj \
    --configuration $BUILD_CONFIGURATION \
    --runtime linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 ) \
    --self-contained false \
    --no-restore \
    -p:ContinuousIntegrationBuild=true \
    --output /app/publish

# --- Runtime -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime

ARG VERSION=0.1.0
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="klarahome-worker" \
      org.opencontainers.image.description="Klara Home background worker" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.source="https://github.com/klarahome/klarahome"

ENV DOTNET_ENVIRONMENT=Production \
    APP_ROLE=worker \
    DOTNET_gcServer=1

WORKDIR /app
COPY --from=build /app/publish .

# Documents:FontDirectories looks here first.
COPY --from=build /usr/share/fonts/truetype/dejavu/ /app/fonts/

# The chiseled image already defines a non-root `app` user (UID 64198).
USER $APP_UID

# No port, and therefore no HTTP health check. Whether the worker is doing its job
# is answered by the queues draining, which is a metric rather than a probe;
# Step 31 adds the alert on outbox and notification backlog age.
ENTRYPOINT ["/app/KlaraHome.Worker"]
