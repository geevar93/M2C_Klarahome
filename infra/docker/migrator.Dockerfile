# =============================================================================
# Klara Home - migrator image (Step 4)
# =============================================================================
# A one-shot job container. It applies every module's pending EF Core migrations,
# runs the idempotent seeders, and exits: 0 on success, 1 on failure. The deploy
# pipeline runs it to completion BEFORE starting the new API version, so a rolling
# restart never has two versions of the code disagreeing about the shape of a
# table (docs/03-database-design.md §7).
#
# It is a separate image from the API rather than the same image with a different
# entrypoint, because it needs different things: the migration assemblies of every
# module, and none of the ASP.NET Core surface. Keeping them apart also means the
# API image cannot be made to migrate by setting one environment variable.
#
# Build context is the REPOSITORY ROOT:
#   docker build -f infra/docker/migrator.Dockerfile -t klarahome/migrator:dev .
# =============================================================================

# --- Build -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH=amd64
WORKDIR /src

# Same SDK band and the same analyzer configuration a developer sees: the image
# build runs with warnings-as-errors and must apply identical rule severities.
COPY global.json .editorconfig ./

# Restore from project files only, so editing a .cs file does not invalidate it.
COPY src/backend/Directory.Build.props src/backend/Directory.Packages.props src/backend/
COPY src/backend/shared/KlaraHome.SharedKernel/KlaraHome.SharedKernel.csproj    src/backend/shared/KlaraHome.SharedKernel/
COPY src/backend/shared/KlaraHome.Contracts/KlaraHome.Contracts.csproj          src/backend/shared/KlaraHome.Contracts/
COPY src/backend/shared/KlaraHome.Infrastructure/KlaraHome.Infrastructure.csproj src/backend/shared/KlaraHome.Infrastructure/
# Every module's project file, put back into the directory it belongs to. A wildcard COPY
# flattens, so the loop restores the layout `dotnet restore` needs. It is written this way
# deliberately: a hand-kept list of COPY lines is a second declaration of which modules exist,
# and it silently fell eighteen modules behind the solution during the build sprint. A module
# added later needs no change here.
COPY src/backend/modules/*/*.csproj /tmp/modules/
RUN set -eu; \
    for project in /tmp/modules/*.csproj; do \
        name="$(basename "$project" .csproj)"; \
        mkdir -p "src/backend/modules/$name"; \
        mv "$project" "src/backend/modules/$name/"; \
    done; \
    rmdir /tmp/modules
COPY src/backend/host/KlaraHome.Migrator/KlaraHome.Migrator.csproj              src/backend/host/KlaraHome.Migrator/

RUN dotnet restore src/backend/host/KlaraHome.Migrator/KlaraHome.Migrator.csproj \
    --runtime linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 )

COPY src/backend/ src/backend/

# No ReadyToRun here, unlike the API. The migrator runs once for a few seconds and
# then exits; pre-compilation would trade image size and build time for a startup
# saving nothing measures.
RUN dotnet publish src/backend/host/KlaraHome.Migrator/KlaraHome.Migrator.csproj \
    --configuration $BUILD_CONFIGURATION \
    --runtime linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 ) \
    --self-contained false \
    --no-restore \
    -p:ContinuousIntegrationBuild=true \
    --output /app/publish

# --- Runtime -----------------------------------------------------------------
# The aspnet image, even though the migrator serves no HTTP: it references
# KlaraHome.Infrastructure for the module registry, options binding and logging,
# and that assembly carries a FrameworkReference to Microsoft.AspNetCore.App. The
# smaller `runtime` image fails at launch with "framework Microsoft.AspNetCore.App
# was not found". Splitting the persistence and hosting concerns out of
# Infrastructure so this image can shrink is in the Parking Lot, not smuggled in
# here.
#
# Known, harmless startup line on this base:
#   Cannot load library libgssapi_krb5.so.2
# Npgsql probes for Kerberos support on connect; a chiseled image has no krb5, so
# the probe fails and it proceeds with SCRAM, which is what the server offers and
# what we use. The `-extra` variant does not carry krb5 either, so accepting the
# line costs nothing and avoids a larger image for a message rather than a fix.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime

ARG VERSION=0.1.0
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="klarahome-migrator" \
      org.opencontainers.image.description="Klara Home database migration job" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.source="https://github.com/klarahome/klarahome"

ENV DOTNET_ENVIRONMENT=Production \
    APP_ROLE=migrator

WORKDIR /app
COPY --from=build /app/publish .

# The chiseled image already defines a non-root `app` user (UID 64198).
USER $APP_UID

# Deliberately no HEALTHCHECK: this container is meant to exit. A health check on
# a job that finishes reports unhealthy the moment it succeeds.
ENTRYPOINT ["/app/KlaraHome.Migrator"]
