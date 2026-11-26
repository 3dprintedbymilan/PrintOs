#!/usr/bin/env bash
# Logical, encrypted backup of the PrintOS database.
#
#   pg_dump (custom format, compressed) | openssl AES-256 | file
#
# Connection: standard libpq variables (PGHOST, PGPORT, PGUSER, PGPASSWORD, PGDATABASE, PGSSLMODE).
# Required:   BACKUP_PASSPHRASE (at least 24 characters; keep it in a password manager, NOT in the repo).
# Optional:   BACKUP_DIR (default ./backups), PG_EXEC (see lib.sh).
# Output:     $BACKUP_DIR/printos-<UTC timestamp>.dump.enc and a .sha256 file next to it.
# The path of the encrypted file is the only thing printed on stdout.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
. "$here/lib.sh"

require_passphrase
: "${PGDATABASE:?PGDATABASE is not set}"
backup_dir="${BACKUP_DIR:-./backups}"
mkdir -p "$backup_dir"

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
target="$backup_dir/printos-$stamp.dump.enc"
partial="$target.partial"
trap 'rm -f "$partial"' EXIT

log "dumping $PGDATABASE"
pg pg_dump --format=custom --no-owner --no-privileges --dbname "$PGDATABASE" | encrypt_stream > "$partial"
[ -s "$partial" ] || die "backup is empty"

mv "$partial" "$target"
printf '%s  %s\n' "$(sha256_of "$target")" "$(basename "$target")" > "$target.sha256"
log "wrote $target ($(wc -c < "$target" | tr -d ' ') bytes)"
printf '%s\n' "$target"
