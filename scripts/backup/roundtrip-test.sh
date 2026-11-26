#!/usr/bin/env bash
# End-to-end rehearsal of the backup and restore path on a throwaway local PostgreSQL (Docker).
#
#   1. start postgres in a container and apply the real EF Core schema (all migrations)
#   2. seed rows, record the exact row counts per table
#   3. backup.sh, then check that the file is encrypted and tamper-evident
#   4. destroy the source database (the "disaster")
#   5. restore-test.sh restores into a scratch database and compares every table's row count
#
# Needs: docker, openssl, and either dotnet (to generate the schema) or SCHEMA_SQL=<path to a SQL file>.
# Never touches Azure or any existing database.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
# shellcheck source=lib.sh
. "$here/lib.sh"

image="${PG_IMAGE:-postgres:18-alpine}"
container="printos-backup-roundtrip-$$"
work="$(mktemp -d)"
export PGUSER=postgres
export PGPASSWORD="roundtrip-$RANDOM-$RANDOM"
export PGDATABASE=printos
export BACKUP_PASSPHRASE="roundtrip-passphrase-$RANDOM-$RANDOM-$RANDOM"
export BACKUP_DIR="$work/backups"
export PG_EXEC="docker exec -i -e PGUSER -e PGPASSWORD $container"

cleanup() {
  docker rm -f "$container" >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

log "starting $image"
docker run -d --name "$container" -e POSTGRES_PASSWORD="$PGPASSWORD" "$image" >/dev/null
for _ in $(seq 1 60); do
  if pg pg_isready -q -U "$PGUSER" && pg psql --no-psqlrc -X -q -d postgres -c 'select 1' >/dev/null 2>&1; then break; fi
  sleep 1
done
pg psql --no-psqlrc -X -q -d postgres -c 'select 1' >/dev/null || die "postgres did not become ready"

schema="${SCHEMA_SQL:-$work/schema.sql}"
if [ -z "${SCHEMA_SQL:-}" ]; then
  command -v dotnet >/dev/null || die "dotnet is not installed; set SCHEMA_SQL to a SQL file with the schema"
  log "generating the schema from the EF Core migrations"
  schema_out="$work/schema.sql"
  if command -v cygpath >/dev/null 2>&1; then schema_out="$(cygpath -m "$schema_out")"; fi
  (cd "$repo/apps/api" && dotnet ef migrations script --idempotent --project src/PrintOs.Api --output "$schema_out" >/dev/null)
fi

pg psql --no-psqlrc -X -q -d postgres -c "CREATE DATABASE $PGDATABASE"
pg psql --no-psqlrc -X -q -v ON_ERROR_STOP=1 -d "$PGDATABASE" < "$schema" >/dev/null

log "seeding data"
pg psql --no-psqlrc -X -q -v ON_ERROR_STOP=1 -d "$PGDATABASE" <<'SQL'
INSERT INTO "Workspaces" ("Id", "Name", "CreatedAt")
SELECT gen_random_uuid(), 'Roundtrip workspace ' || n, now() FROM generate_series(1, 3) AS n;

INSERT INTO "Suppliers" ("Id", "WorkspaceId", "Name")
SELECT gen_random_uuid(), w."Id", 'Supplier ' || n
FROM "Workspaces" w, generate_series(1, 50) AS n;

INSERT INTO "AuditEntries" ("Id", "WorkspaceId", "EntityType", "EntityId", "Action", "Changes", "CreatedAt")
SELECT gen_random_uuid(), w."Id", 'Supplier', n::text, 'Created', '{"name":{"old":null,"new":"Roundtrip marker"}}', now()
FROM "Workspaces" w, generate_series(1, 200) AS n;
SQL

table_counts "$PGDATABASE" > "$work/expected-counts.txt"
log "source: $(grep -c . "$work/expected-counts.txt") tables, $(grep '^AuditEntries ' "$work/expected-counts.txt")"
grep -q '^AuditEntries 600$' "$work/expected-counts.txt" || die "seed did not produce the expected rows"

log "step 3: backup"
backup="$("$here/backup.sh")"
[ -s "$backup" ] || die "no backup file"

if grep -a -q 'PGDMP' "$backup" || grep -a -q 'Roundtrip' "$backup"; then
  die "the backup file contains plaintext"
fi
log "backup is encrypted (no PGDMP header, no plaintext rows)"

log "tamper check: a modified copy must be rejected"
cp "$backup" "$work/tampered.dump.enc"
cp "$backup.sha256" "$work/tampered.dump.enc.sha256"
printf 'X' | dd of="$work/tampered.dump.enc" bs=1 seek=100 conv=notrunc status=none
if "$here/restore-test.sh" "$work/tampered.dump.enc" >/dev/null 2>&1; then
  die "a tampered backup was accepted"
fi

log "wrong passphrase must be rejected"
if BACKUP_PASSPHRASE="a-completely-different-passphrase-123" "$here/restore-test.sh" "$backup" >/dev/null 2>&1; then
  die "restore succeeded with the wrong passphrase"
fi

log "step 4: disaster, dropping the source database"
pg psql --no-psqlrc -X -q -d postgres -c "DROP DATABASE $PGDATABASE WITH (FORCE)"

log "step 5: restore test"
EXPECTED_COUNTS_FILE="$work/expected-counts.txt" "$here/restore-test.sh" "$backup" >/dev/null

log "ROUNDTRIP OK: backup encrypted, tamper and wrong passphrase rejected, restore matches the source row for row"
