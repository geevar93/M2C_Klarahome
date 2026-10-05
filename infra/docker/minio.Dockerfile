# =============================================================================
# Klara Home - MinIO image, built from source
# =============================================================================
# MinIO archived its open-source server and client: the `minio/minio` and
# `minio/mc` images can no longer be pulled from Docker Hub and dl.min.io
# answers 410 Gone for every community binary. The source is still on GitHub,
# so this image compiles the two releases the stack was already pinned to.
#
# One image carries both binaries plus a shell, because the stack needs all
# three: the server's healthcheck runs `mc ready local`, and the minio-init
# one-shot runs a `sh -c` script of `mc` commands.
#
# Each source tree is fetched by COMMIT, not by tag - a tag can be moved, a
# commit hash cannot. To move to another release, change the RELEASE and COMMIT
# pair together (`git ls-remote --tags https://github.com/minio/minio`; use the
# `^{}` line, which is the commit the tag points at).
#
# The build needs no files from the repository:
#   docker build -f infra/docker/minio.Dockerfile -t klarahome/minio:dev infra/docker
# =============================================================================

# --- Build -------------------------------------------------------------------
FROM golang:1.24.13-alpine3.23 AS build
RUN apk add --no-cache git

ARG MINIO_RELEASE=RELEASE.2025-09-07T16-13-09Z
ARG MINIO_COMMIT=07c3a429bfed433e49018cb0f78a52145d4bedeb
ARG MC_RELEASE=RELEASE.2025-08-13T08-35-41Z
ARG MC_COMMIT=7394ce0dd2a80935aded936b09fa12cbb3cb8096

# Compiling MinIO is memory-hungry; four packages at a time keeps the build
# inside a few GB instead of scaling with every core on the machine.
ENV CGO_ENABLED=0 GOFLAGS=-p=4

# The -X flags are the ones upstream's buildscripts/gen-ldflags.go produces for
# a release build, so `minio --version` reports the release rather than
# DEVELOPMENT (which also switches off some release-only behaviour).
WORKDIR /src/minio
RUN git init -q . \
    && git remote add origin https://github.com/minio/minio.git \
    && git fetch -q --depth 1 origin "$MINIO_COMMIT" \
    && git checkout -q FETCH_HEAD
RUN --mount=type=cache,target=/go/pkg/mod \
    --mount=type=cache,target=/root/.cache/go-build \
    version="$(echo "${MINIO_RELEASE#RELEASE.}" | sed -E 's/T([0-9]+)-([0-9]+)-([0-9]+)Z$/T\1:\2:\3Z/')" \
    && go build -tags kqueue -trimpath -o /out/minio -ldflags "-s -w \
        -X github.com/minio/minio/cmd.Version=$version \
        -X github.com/minio/minio/cmd.CopyrightYear=${version%%-*} \
        -X github.com/minio/minio/cmd.ReleaseTag=$MINIO_RELEASE \
        -X github.com/minio/minio/cmd.CommitID=$MINIO_COMMIT \
        -X github.com/minio/minio/cmd.ShortCommitID=$(echo "$MINIO_COMMIT" | cut -c1-12)" .

WORKDIR /src/mc
RUN git init -q . \
    && git remote add origin https://github.com/minio/mc.git \
    && git fetch -q --depth 1 origin "$MC_COMMIT" \
    && git checkout -q FETCH_HEAD
RUN --mount=type=cache,target=/go/pkg/mod \
    --mount=type=cache,target=/root/.cache/go-build \
    version="$(echo "${MC_RELEASE#RELEASE.}" | sed -E 's/T([0-9]+)-([0-9]+)-([0-9]+)Z$/T\1:\2:\3Z/')" \
    && go build -tags kqueue -trimpath -o /out/mc -ldflags "-s -w \
        -X github.com/minio/mc/cmd.Version=$version \
        -X github.com/minio/mc/cmd.CopyrightYear=${version%%-*} \
        -X github.com/minio/mc/cmd.ReleaseTag=$MC_RELEASE \
        -X github.com/minio/mc/cmd.CommitID=$MC_COMMIT \
        -X github.com/minio/mc/cmd.ShortCommitID=$(echo "$MC_COMMIT" | cut -c1-12)" .

# AGPLv3: the licence text travels with the binaries.
RUN mkdir /out/licenses \
    && cp /src/minio/LICENSE /out/licenses/minio-LICENSE \
    && cp /src/mc/LICENSE /out/licenses/mc-LICENSE

# --- Runtime -----------------------------------------------------------------
FROM alpine:3.23.6
RUN apk add --no-cache ca-certificates

COPY --from=build /out/minio /out/mc /usr/bin/
COPY --from=build /out/licenses /licenses

ARG MINIO_RELEASE=RELEASE.2025-09-07T16-13-09Z
ARG MC_RELEASE=RELEASE.2025-08-13T08-35-41Z
LABEL org.opencontainers.image.title="Klara Home MinIO (server + mc)" \
      org.opencontainers.image.source="https://github.com/minio/minio" \
      org.opencontainers.image.licenses="AGPL-3.0-only" \
      in.klarahome.minio.release="$MINIO_RELEASE" \
      in.klarahome.mc.release="$MC_RELEASE"

# Same as the upstream image: mc keeps its config (and the default `local`
# alias the healthcheck relies on) somewhere writable that is not the data volume.
ENV MC_CONFIG_DIR=/tmp/.mc

EXPOSE 9000 9001
VOLUME ["/data"]

ENTRYPOINT ["minio"]
