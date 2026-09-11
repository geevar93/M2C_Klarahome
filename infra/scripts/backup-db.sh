#!/usr/bin/env bash
# =============================================================================
# Klara Home - nightly database backup (production VPS)
# =============================================================================
# Dumps the PostgreSQL container in this stack to a compressed custom-format file, verifies the
# dump is readable, and deletes dumps older than the retention window.
#
#   sudo /opt/klarahome/infra/scripts/backup-db.sh
#
# Cron (docs/deployment-vps.md section 8):
#   15 2 * * *  /opt/klarahome/infra/scripts/backup-db.sh >> /var/log/klarahome-backup.log 2>&1
#
# Custom format (-Fc) rather than plain SQL: it is compressed, it can be restored selectively,
# and `pg_restore --list` can prove it is readable without restoring it - which is the only
# difference between a backup and a file nobody has ever opened.
#
# A dump written to this VPS survives a bad deploy and a dropped table. It does NOT survive the
# VPS. Copy BACKUP_DIR off the machine - the last line of this script is where to add that - or
# the disaster recovery plan is a hope.
#
# When the database moves to Neon or another managed provider this script no longer applies:
# their point-in-time restore replaces it. Keep taking a periodic logical dump anyway, from any
# machine with the connection string, so a restore is possible into something that is not them:
#   pg_dump "postgresql://user:pass@host/db?sslmode=require" -Fc -f klarahome-$(date +%F).dump
# =============================================================================
set -euo pipefail

REPO_ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
COMPOSE_FILE="${COMPOSE_FILE:-$REPO_ROOT/infra/compose/docker-compose.prod.yml}"
ENV_FILE="${ENV_FILE:-$REPO_ROOT/.env.prod}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/klarahome}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"

if [ ! -f "$ENV_FILE" ]; then
    echo "backup: no env file at $ENV_FILE" >&2
    exit 1
fi

# Read the two values needed, without sourcing the file: it holds secrets, and sourcing it would
# put every one of them into this shell's environment and into any child process.
env_value() {
    sed -n "s/^[[:space:]]*$1[[:space:]]*=[[:space:]]*\(.*\)$/\1/p" "$ENV_FILE" | head -n 1
}

POSTGRES_DB=$(env_value POSTGRES_DB)
POSTGRES_USER=$(env_value POSTGRES_USER)
: "${POSTGRES_DB:?POSTGRES_DB not found in $ENV_FILE}"
: "${POSTGRES_USER:?POSTGRES_USER not found in $ENV_FILE}"

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

stamp=$(date -u +%Y%m%dT%H%M%SZ)
target="$BACKUP_DIR/klarahome-$stamp.dump"

echo "backup: dumping $POSTGRES_DB to $target"

# -T pg_dump inside the container, streaming to the host: no dump file is ever written into the
# container's filesystem, so a full disk there cannot corrupt one.
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" exec -T postgres \
    pg_dump --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom --compress=9 \
    > "$target"

chmod 600 "$target"

# A dump that cannot be listed cannot be restored. Catching that tonight is the whole point.
# Read back from the FILE rather than from a pipe, so this exercises the same seekable archive a
# restore would open, and does not depend on the stack still being up.
if ! docker run --rm -v "$BACKUP_DIR:/backups:ro" postgres:18.6-alpine \
        pg_restore --list "/backups/$(basename "$target")" > /dev/null; then
    echo "backup: FAILED - $target is not a readable dump; keeping it for inspection" >&2
    exit 1
fi

size=$(du -h "$target" | cut -f1)
echo "backup: ok - $target ($size)"

deleted=$(find "$BACKUP_DIR" -name 'klarahome-*.dump' -type f -mtime "+$RETENTION_DAYS" -print -delete | wc -l)
echo "backup: retention $RETENTION_DAYS days, removed $deleted older dump(s)"

# Off-site copy goes here. Until this line exists, a fire in the data centre takes the backups
# with the database. For example:
#   rclone copy "$target" remote:klarahome-backups/
#   aws s3 cp "$target" s3://klarahome-backups/ --storage-class STANDARD_IA
