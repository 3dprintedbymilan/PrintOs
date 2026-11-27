# Backups and restore

Issue #29. Production is Azure Database for PostgreSQL flexible server `psql-printos-ne` and the storage account `stprintosprod` (see `azure.md`). Nothing in this document has been run against production, no Azure resources were created for it, and no key is stored in the repository.

## Layers

| Layer | What it protects against | Where it lives | Encryption |
|---|---|---|---|
| Azure automated backups and point-in-time restore (PITR) | Bad deploy, accidental delete, corruption in the last 14 days | Inside Azure, managed by the service | Always on, Microsoft-managed keys (AES-256) |
| Scheduled logical backup (`pg_dump`) | Losing the server or subscription, long-term copies, moving to another host | Azure Blob container, a separate copy you hold the key for | AES-256 with a passphrase only the owner has, on top of storage encryption |
| Blob protection for `printos-files` | Overwritten or deleted uploaded files | Same storage account (soft delete, versioning) | Always on, Microsoft-managed keys |

The first layer is the fast path. The second is the independent safety net.

## 1. Azure automated backups (database)

Flexible server takes snapshots and WAL backups automatically; the retention is already 14 days (`azure.md`). Check or change it without touching data:

```
az postgres flexible-server show -g rg-printos-prod -n psql-printos-ne --query backup
az postgres flexible-server update -g rg-printos-prod -n psql-printos-ne --backup-retention 14   # 7 to 35 days
```

- Geo-redundant backup can only be chosen when a server is created (`--geo-redundant-backup Enabled`). The server is in North Europe with one copy of the backups in the same region. The logical backup below is therefore the cross-region/off-server copy: put it in a storage account in another region (Sweden Central already exists) if regional loss matters to you.
- Restore is always into a **new** server, never in place.

### Point-in-time restore runbook

1. Decide the target time (UTC), just before the incident.
2. Create the restored server (Azure Portal: PostgreSQL server, Backup and restore, Restore; or CLI):
   ```
   az postgres flexible-server restore -g rg-printos-prod -n psql-printos-restore \
     --source-server psql-printos-ne --restore-time "2026-11-26T10:00:00Z"
   ```
3. Check the restored data: connect with `psql` and look at the latest rows (for example `SELECT max("CreatedAt") FROM "AuditEntries"`).
4. Point the API at it: change the `connection-string` secret of `ca-printos-api` (host `psql-printos-restore.postgres.database.azure.com`) and restart the revision.
5. When confident, delete or keep the old server per your needs; rename in `azure.md`.

The audit log (#28) helps here: its newest row tells you how far the restored data reaches.

## 2. Scheduled encrypted logical backup

Scripts in `scripts/backup` (bash, `openssl`, and PostgreSQL client tools or Docker):

| Script | Purpose |
|---|---|
| `backup.sh` | `pg_dump --format=custom` piped through `openssl enc -aes-256-cbc -pbkdf2 -iter 600000` into `printos-<UTC time>.dump.enc` plus a `.sha256` file |
| `restore-test.sh <file>` | verifies the checksum, decrypts, restores into a **new scratch database**, checks tables and applied EF migrations (and optionally exact row counts), drops the scratch database |
| `roundtrip-test.sh` | rehearsal on a throwaway Docker Postgres: real schema from the EF migrations, seed, backup, tamper and wrong-passphrase checks, drop the source, restore, compare every table's row count |
| `lib.sh` | shared helpers |

Configuration is via environment variables only: `PGHOST PGPORT PGUSER PGPASSWORD PGDATABASE PGSSLMODE`, `BACKUP_PASSPHRASE` (24+ characters), `BACKUP_DIR`. `PG_EXEC` runs the Postgres client tools elsewhere, for example `PG_EXEC="docker run --rm -i -e PGHOST -e PGPORT -e PGUSER -e PGPASSWORD -e PGSSLMODE postgres:18-alpine"`, so you do not have to install a matching `pg_dump` 18 locally.

Encryption details and limits:

- The passphrase is read from the environment (never the command line), so it does not show up in process lists.
- `openssl enc` is not an authenticated mode. Tampering is detected through the SHA-256 file and, as a second line, by `pg_restore` refusing a damaged archive. If you prefer [age](https://age-encryption.org), swap `encrypt_stream` and `decrypt_stream` in `lib.sh`; nothing else changes.
- The passphrase is the only thing that can open a backup. **Store it in your password manager and a second safe place.** If it is lost, the dumps are unrecoverable. Do not commit it, and do not put it in the Azure portal only.

### Manual backup and restore (any machine)

```
export PGHOST=psql-printos-ne.postgres.database.azure.com PGUSER=printosadmin PGDATABASE=printos PGSSLMODE=require
export PGPASSWORD='...'            # from the secret store
export BACKUP_PASSPHRASE='...'     # 24+ characters
bash scripts/backup/backup.sh

# restore into a local scratch server (never the production host)
export PGHOST=localhost PGUSER=postgres PGPASSWORD=... PGSSLMODE=prefer
bash scripts/backup/restore-test.sh backups/printos-20261126T020000Z.dump.enc
```

To actually bring a dump back as the live database after a disaster: restore into a fresh server (`createdb printos`, then `openssl enc -d ... | pg_restore --no-owner --dbname printos`, the same pipeline `restore-test.sh` uses), run the API once so any newer migrations apply, then switch the connection string secret.

### The scheduled workflow (draft, disabled by default)

`.github/workflows/backup.yml` has two jobs:

- `rehearsal` runs on pull requests that touch the scripts, weekly and on demand. It needs nothing from Azure.
- `backup` runs nightly at 02:17 UTC and **does nothing until the repository variables exist**, the same pattern as `deploy.yml` (`vars.AZURE_CLIENT_ID != ''`). It dumps, encrypts, restore-tests the fresh file on a throwaway Postgres in the runner, then uploads the encrypted file and checksum to `database/` in the backup container.

To enable it (one time, by the owner):

1. Create a private blob container for backups, preferably in a storage account other than `stprintosprod` and in another region. Add a lifecycle rule to expire old dumps (for example 90 days) and turn on soft delete.
2. Give the deploy identity (or a dedicated one with a federated credential) the **Storage Blob Data Contributor** role on that container only.
3. Create a database role for backups with `pg_read_all_data` and allow the runner to reach the server. A GitHub-hosted runner has no fixed IP: either allow Azure services in the server firewall (the least effort, weaker) or run the job on a self-hosted runner / Container Apps job in the VNet.
4. Add repository variables `BACKUP_PG_HOST`, `BACKUP_PG_USER`, `BACKUP_STORAGE_ACCOUNT`, `BACKUP_CONTAINER` (plus the existing `AZURE_*` ones) and repository secrets `BACKUP_PG_PASSWORD` and `BACKUP_PASSPHRASE`.
5. Run the workflow manually once and check the blob and the green restore-test step.

If the job fails, GitHub emails the repository owner: that is the alert. A backup that has not appeared in the container for more than 36 hours is an incident.

## 3. Storage backups (`printos-files`)

Uploaded files (model library, #19) are protected with blob-native features instead of a copy job. Apply once (owner, review before running):

```
az storage account blob-service-properties update -g rg-printos-prod --account-name stprintosprod \
  --enable-delete-retention true --delete-retention-days 14 \
  --enable-container-delete-retention true --container-delete-retention-days 14 \
  --enable-versioning true \
  --enable-change-feed true --enable-restore-policy true --restore-days 7
```

- Soft delete and versioning bring back deleted or overwritten files; the restore policy rolls a container back to a point in time (`az storage blob restore`).
- Everything is encrypted at rest by the storage service (Microsoft-managed keys). For files you also hold elsewhere (the originals on your PC) this is enough; if the files are the only copy, add a second storage account and a scheduled `azcopy sync` to it.
- Redundancy: `stprintosprod` should be at least GRS if regional loss matters (`az storage account update --sku Standard_GRS`), which costs about double.

## 4. Restore test

The restore test is the proof that backups work. Two levels:

1. **Automated rehearsal** (`roundtrip-test.sh`, the `rehearsal` job): proves the scripts and the encryption round trip with the real schema on every relevant change and weekly.
2. **Real backup check** (the `restore-test.sh` step inside the nightly `backup` job): restores every night's actual production dump into a scratch Postgres and fails the job when tables or migrations are missing.

Additionally, once per quarter do a manual drill: take a PITR restore (section 1) into a temporary server, open the app against it, then delete the server. Record the date here.

### Rehearsal result (2026-11-26, Docker Desktop, PostgreSQL 18)

```
bash scripts/backup/roundtrip-test.sh
source: 21 tables, AuditEntries 600
backup is encrypted (no PGDMP header, no plaintext rows)
tamper check: a modified copy must be rejected
wrong passphrase must be rejected
step 4: disaster, dropping the source database
row counts match the source exactly
restore test passed: 21 tables, 10 migrations applied
ROUNDTRIP OK: backup encrypted, tamper and wrong passphrase rejected, restore matches the source row for row
```

Run it yourself with `bash scripts/backup/roundtrip-test.sh` (needs Docker and the .NET SDK with `dotnet-ef`, or `SCHEMA_SQL=<file>`). On Windows use Git Bash and set `MSYS_NO_PATHCONV=1`.

## Not covered

- RPO of the logical backup is 24 hours; PITR is minutes. Use PITR for "undo the last hour".
- The Azure Key Vault is not used; the passphrase lives with the owner. Moving it to Key Vault is a possible follow-up.
- Application secrets (the Data Protection keys live in the database and are therefore in the dump; the API connection string secret is recreated by hand).
