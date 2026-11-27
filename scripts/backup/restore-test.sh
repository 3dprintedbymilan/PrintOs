#!/usr/bin/env bash
# Restore test: proves an encrypted backup can be decrypted and restored.
#
#   restore-test.sh <backup.dump.enc>
#
# It restores into a NEW scratch database (printos_restore_<timestamp>) on the server given by the
# PG* variables, checks the result and drops the scratch database again. Nothing else is touched.
# Point it at a scratch/local PostgreSQL, never at production. It refuses Azure hosts unless
# ALLOW_RESTORE_TO_AZURE=1 (only for a deliberate restore into a throwaway Azure server).
#
# Required: BACKUP_PASSPHRASE, PGHOST/PGPORT/PGUSER/PGPASSWORD of a server where the user may CREATE DATABASE.
# Optional: EXPECTED_COUNTS_FILE (lines "<table> <rows>", the roundtrip test uses it for an exact comparison),
#           MIN_TABLES (default 1), KEEP_SCRATCH=1 (do not drop the scratch database), PG_EXEC (see lib.sh).
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
. "$here/lib.sh"

backup="${1:-}"
[ -n "$backup" ] && [ -f "$backup" ] || die "usage: restore-test.sh <backup.dump.enc>"
require_passphrase

case "${PGHOST:-}" in
  *azure.com*) [ "${ALLOW_RESTORE_TO_AZURE:-}" = "1" ] || die "refusing to restore into an Azure host (set ALLOW_RESTORE_TO_AZURE=1 for a throwaway server)" ;;
esac

if [ -f "$backup.sha256" ]; then
  expected="$(cut -d ' ' -f 1 "$backup.sha256")"
  [ "$(sha256_of "$backup")" = "$expected" ] || die "checksum mismatch: the backup file is corrupt or was modified"
  log "checksum ok"
else
  log "no .sha256 file next to the backup, skipping the checksum"
fi

scratch="printos_restore_$(date -u +%Y%m%d%H%M%S)_$$"
cleanup() {
  if [ "${KEEP_SCRATCH:-}" != "1" ]; then
    pg psql --no-psqlrc -X -q -d postgres -c "DROP DATABASE IF EXISTS \"$scratch\" WITH (FORCE)" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

log "creating scratch database $scratch"
pg psql --no-psqlrc -X -q -v ON_ERROR_STOP=1 -d postgres -c "CREATE DATABASE \"$scratch\""

log "decrypting and restoring"
decrypt_stream < "$backup" | pg pg_restore --no-owner --no-privileges --exit-on-error --dbname "$scratch"

tables="$(table_counts "$scratch")"
table_total="$(printf '%s\n' "$tables" | grep -c . || true)"
[ "$table_total" -ge "${MIN_TABLES:-1}" ] || die "restored database has only $table_total tables"

migrations="$(pg psql --no-psqlrc -X -At -d "$scratch" -c 'SELECT count(*) FROM "__EFMigrationsHistory"' 2>/dev/null || echo 0)"
if [ "${REQUIRE_MIGRATIONS_HISTORY:-1}" = "1" ] && [ "${migrations:-0}" -lt 1 ]; then
  die "restored database has no applied EF Core migrations"
fi

if [ -n "${EXPECTED_COUNTS_FILE:-}" ]; then
  if ! diff <(sort "$EXPECTED_COUNTS_FILE") <(printf '%s\n' "$tables" | sort) >&2; then
    die "row counts differ between the source and the restored database"
  fi
  log "row counts match the source exactly"
fi

log "restore test passed: $table_total tables, $migrations migrations applied"
printf '%s\n' "$tables"
