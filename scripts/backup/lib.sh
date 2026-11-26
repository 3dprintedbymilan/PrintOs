#!/usr/bin/env bash
# Shared helpers for the PrintOS backup scripts. Sourced, not executed.

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*" >&2; }

die() { log "ERROR: $*"; exit 1; }

# PG_EXEC lets the PostgreSQL client tools run elsewhere, for example inside a container:
#   PG_EXEC="docker exec -i -e PGUSER -e PGPASSWORD my-postgres"
#   PG_EXEC="docker run --rm -i -e PGHOST -e PGPORT -e PGUSER -e PGPASSWORD -e PGSSLMODE postgres:18-alpine"
# When it is empty the tools must be on PATH (use the version of the server, or newer).
pg() {
  if [ -n "${PG_EXEC:-}" ]; then
    # shellcheck disable=SC2086
    $PG_EXEC "$@"
  else
    "$@"
  fi
}

require_passphrase() {
  [ -n "${BACKUP_PASSPHRASE:-}" ] || die "BACKUP_PASSPHRASE is not set"
  [ "${#BACKUP_PASSPHRASE}" -ge 24 ] || die "BACKUP_PASSPHRASE must be at least 24 characters"
  export BACKUP_PASSPHRASE
}

# AES-256-CBC with a PBKDF2-derived key. The passphrase is read from the environment so it never
# shows up in the process list. Integrity is checked separately with a SHA-256 of the ciphertext.
encrypt_stream() {
  openssl enc -aes-256-cbc -salt -pbkdf2 -iter 600000 -pass env:BACKUP_PASSPHRASE
}

decrypt_stream() {
  openssl enc -d -aes-256-cbc -salt -pbkdf2 -iter 600000 -pass env:BACKUP_PASSPHRASE
}

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | cut -d ' ' -f 1
  else
    shasum -a 256 "$1" | cut -d ' ' -f 1
  fi
}

# One line per public table: "<table> <row count>", sorted. Used to compare a restore with its source.
table_counts() {
  local database="$1"
  pg psql --no-psqlrc -X -At -F ' ' -d "$database" -c "
    SELECT table_name,
           (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, '')))[1]::text
    FROM information_schema.tables
    WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
    ORDER BY table_name"
}
