# =============================================================================
# Klara Home - API image (Step 3)
# =============================================================================
# Per docs/06-infrastructure-devops.md §3:
#   * multi-stage build, lockfile copied first so restore layers cache
#   * chiseled runtime: no shell, no package manager, minimal CVE surface
#   * non-root
#   * ReadyToRun, so a cold container serves its first request quickly
#   * no secrets in any layer
#
# Build context is the REPOSITORY ROOT, because the build needs global.json and
# the whole src/backend tree:
#   docker build -f infra/docker/api.Dockerfile -t klarahome/api:dev .
#
# The worker and migrator run from this same image with a different entrypoint
# (APP_ROLE); their compose services arrive with the work that needs them.
# =============================================================================

# --- Build -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH=amd64
WORKDIR /src

# Pin the SDK feature band exactly as the repository does, and bring the analyzer
# configuration with it: the image build runs with warnings-as-errors, so it must
# see the same rule severities a developer does.
COPY global.json .editorconfig ./

# Restore first, from project files only: editing a .cs file must not invalidate
# the restore layer.
COPY src/backend/Directory.Build.props src/backend/Directory.Packages.props src/backend/
COPY src/backend/shared/KlaraHome.SharedKernel/KlaraHome.SharedKernel.csproj    src/backend/shared/KlaraHome.SharedKernel/
COPY src/backend/shared/KlaraHome.Contracts/KlaraHome.Contracts.csproj          src/backend/shared/KlaraHome.Contracts/
COPY src/backend/shared/KlaraHome.Infrastructure/KlaraHome.Infrastructure.csproj src/backend/shared/KlaraHome.Infrastructure/
COPY src/backend/modules/KlaraHome.Modules.Platform/KlaraHome.Modules.Platform.csproj src/backend/modules/KlaraHome.Modules.Platform/
COPY src/backend/modules/KlaraHome.Modules.Identity/KlaraHome.Modules.Identity.csproj src/backend/modules/KlaraHome.Modules.Identity/
COPY src/backend/host/KlaraHome.Api/KlaraHome.Api.csproj                        src/backend/host/KlaraHome.Api/

# PublishReadyToRun has to be set at restore time as well: it is what pulls in the
# crossgen2 runtime pack that the publish-time pre-compilation needs.
RUN dotnet restore src/backend/host/KlaraHome.Api/KlaraHome.Api.csproj \
    --runtime linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 ) \
    -p:PublishReadyToRun=true

COPY src/backend/ src/backend/

RUN dotnet publish src/backend/host/KlaraHome.Api/KlaraHome.Api.csproj \
    --configuration $BUILD_CONFIGURATION \
    --runtime linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 ) \
    --self-contained false \
    --no-restore \
    -p:PublishReadyToRun=true \
    -p:ContinuousIntegrationBuild=true \
    --output /app/publish

# --- Runtime -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime

# Recorded on the image so a running container can be traced back to a commit.
ARG VERSION=0.1.0
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="klarahome-api" \
      org.opencontainers.image.description="Klara Home commerce API" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.source="https://github.com/klarahome/klarahome"

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    APP_ROLE=api \
    DOTNET_TieredPMStubs=0 \
    DOTNET_gcServer=1

WORKDIR /app
COPY --from=build /app/publish .

# The chiseled image already defines a non-root `app` user (UID 64198).
USER $APP_UID
EXPOSE 8080

# No shell and no curl in this image, so the application probes itself.
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
    CMD ["/app/KlaraHome.Api", "--healthcheck"]

ENTRYPOINT ["/app/KlaraHome.Api"]
