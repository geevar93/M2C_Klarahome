#!/usr/bin/env sh
# Drives the Klara Home local development stack (bash/sh/WSL2 equivalent of dev.ps1).
#
#   ./infra/scripts/dev.sh up
#   ./infra/scripts/dev.sh logs postgres
#   ./infra/scripts/dev.sh reset
set -eu

REPO_ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
COMPOSE_FILE="$REPO_ROOT/infra/compose/docker-compose.dev.yml"
ENV_FILE="$REPO_ROOT/.env"

LOCAL_OVERRIDE="$REPO_ROOT/infra/compose/docker-compose.local.yml"
COMMAND=${1:-up}
[ $# -gt 0 ] && shift

env_value() {
    # env_value KEY DEFAULT
    value=""
    if [ -f "$ENV_FILE" ]; then
        value=$(sed -n "s/^[[:space:]]*$1[[:space:]]*=[[:space:]]*\(.*\)$/\1/p" "$ENV_FILE" | head -n 1)
    fi
    [ -n "$value" ] && printf '%s' "$value" || printf '%s' "$2"
}

# Which reverse proxy fronts the stack. `caddy` (the default) is plain http on port 80 in the
# production topology; `traefik` is the original https edge with the self-signed certificate.
EDGE=$(env_value DEV_EDGE caddy | tr '[:upper:]' '[:lower:]')
case "$EDGE" in
    caddy)   SCHEME=http;  FILES="-f $COMPOSE_FILE -f $LOCAL_OVERRIDE" ;;
    traefik) SCHEME=https; FILES="-f $COMPOSE_FILE" ;;
    *) echo "DEV_EDGE must be 'caddy' or 'traefik' (got '$EDGE')." >&2; exit 64 ;;
esac

compose() {
    # $FILES is deliberately unquoted: it is two or four separate arguments.
    # shellcheck disable=SC2086
    if [ -f "$ENV_FILE" ]; then
        (cd "$REPO_ROOT" && docker compose $FILES --env-file "$ENV_FILE" "$@")
    else
        (cd "$REPO_ROOT" && docker compose $FILES "$@")
    fi
}

show_urls() {
    domain=$(env_value DEV_DOMAIN klarahome.localhost)
    bind=$(env_value BIND_ADDRESS 127.0.0.1)
    pg=$(env_value POSTGRES_PORT 5432)
    rd=$(env_value REDIS_PORT 6379)
    s3=$(env_value MINIO_API_PORT 9000)
    console=$(env_value MINIO_CONSOLE_PORT 9001)
    smtp=$(env_value MAILPIT_SMTP_PORT 1025)
    mail_ui=$(env_value MAILPIT_UI_PORT 8025)
    cat <<EOF

Klara Home applications
  Storefront         $SCHEME://$domain
  Admin back office  $SCHEME://admin.$domain
  API                $SCHEME://api.$domain

Klara Home dev services
  MinIO console      $SCHEME://minio.$domain   (or http://$bind:$console)
  MinIO S3 API       $SCHEME://s3.$domain      (or http://$bind:$s3)
  Mailpit            $SCHEME://mail.$domain    (or http://$bind:$mail_ui)
  PostgreSQL         $bind:$pg
  Redis              $bind:$rd
  SMTP (Mailpit)     $bind:$smtp

EOF
    if [ "$EDGE" = caddy ]; then
        echo "Edge: Caddy, plain http on port 80 (DEV_EDGE=caddy). No certificate to trust."
    else
        echo "  Traefik dashboard  https://traefik.$domain"
        echo "The TLS certificate is self-signed - accept the browser warning, or see docs/dev-setup.md."
        echo "Accept it on api.$domain too: the two apps call it with XHR, which gets no"
        echo "warning to click and simply fails."
    fi
}

case "$COMMAND" in
    up)
        # Two passes on purpose: --wait stops as soon as ANY container exits, and
        # minio-init is a one-shot that exits 0. Start everything first, then wait
        # only on the long-running services.
        compose up -d --remove-orphans "$@"
        compose up -d --no-recreate --wait "$EDGE" postgres redis minio mailpit
        compose ps
        show_urls
        ;;
    down)    compose down --remove-orphans "$@" ;;
    restart) compose restart "$@" ;;
    status)  compose ps "$@" ;;
    logs)    compose logs -f --tail 100 "$@" ;;
    urls)    show_urls ;;
    reset)
        echo "This deletes every dev volume: database, object storage and captured mail."
        printf 'Type the word DELETE to continue: '
        read -r answer
        if [ "$answer" = "DELETE" ]; then
            compose down --volumes --remove-orphans
            echo "Volumes removed. Run 'up' to rebuild a clean environment."
        else
            echo "Cancelled."
        fi
        ;;
    *)
        echo "Usage: $0 {up|down|restart|status|logs|reset|urls} [args]" >&2
        exit 64
        ;;
esac
