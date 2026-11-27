# Privacy: export, deletion and retention

Issue #31. Settings, section "Account and data" (`apps/web/components/AccountDataSection.vue`), backed by `POST /api/account/export` and `POST /api/account/delete` (`Modules/Account`).

## Export

- Needs a signed-in user and the user's password again (re-authentication for this request). A wrong password counts as a failed sign-in attempt (lockout after 5).
- Rate limited in process: 3 exports per hour per workspace (`Account:ExportsPerWindow`, `Account:ExportWindowMinutes`). The limiter is per API instance, which is enough for the single container app; use a shared limiter if the API is ever scaled out.
- The response is a ZIP that is written to the response while it is built (rows are read with a streaming query, files are copied one by one from `IFileStorage`), so memory use does not grow with the size of the workspace. Uploaded files are capped at `Account:MaxExportFileBytes` (default 2 GiB); files beyond that are skipped and listed in `export-warnings.json` inside the ZIP.
- Contents: `README.md` (schema), `data/<Entity>.json` for every `IWorkspaceScoped` entity, `data/Workspace.json`, `data/Account.json` (email and user name only), `data/Bambu.json` (`connected` yes/no and printer serials), and `files/...` (model files, thumbnails, license proofs).
- Never exported: password hashes, security stamps, data protection keys, the Bambu Lab access token or account details, storage keys.

### How the export stays complete and safe

The exporter is generic over the EF model: it exports every entity type that implements `IWorkspaceScoped`. Rules for a new entity or property:

- A new `IWorkspaceScoped` entity must get a description in `ExportCatalog`, or `ExportCoverageTests` fails.
- A property whose name looks secret (password, secret, token, stamp, hash, apikey, accesskey, credential, salt, privatekey) is never exported unless it carries `[ExportAllowed("why")]`; `[ExportExcluded("why")]` (property or class) removes it explicitly. The test fails until one of the two is chosen.
- A property holding a key in file storage is marked `[StoredFileKey]`: the key is not exported, the file is added to the ZIP and the row gets a `<property>File` path.

## Deletion

- Needs a signed-in user, the password again and the workspace name typed exactly (case and surrounding spaces are ignored).
- Effective immediately and irreversible. In one database transaction every row of the workspace is deleted (entities in foreign-key order, derived from the EF model, so new entities are covered automatically), then the user accounts of the workspace and the workspace. After the commit all stored files are deleted from `IFileStorage`; a failure to delete a single blob is logged (no content) and does not undo the deletion.
- The audit log entries and the Bambu Lab connection (including the encrypted token and devices) are rows of the workspace and disappear with it. The Bambu background worker stops the workspace's subscriptions before the data is removed and is signalled to reconcile.
- Sign-out everywhere: the accounts no longer exist, so every refresh token is rejected (401) and signing in is impossible. Access tokens already issued (at most 30 minutes) point to a workspace that no longer exists and the account endpoints reject them.
- Multiple users: a workspace is the unit of ownership. Deleting it removes all its member accounts; today a workspace has exactly one owner. When roles are introduced, only an owner may delete the workspace, and other members would need a separate "leave workspace" action.

## Retention

- Live database and blob storage: nothing is kept after deletion.
- Backups (`backups.md`): Azure automated backups age out after 14 days, the encrypted logical dumps after the retention configured for that container, and blob soft delete/versioning after its retention period. Deleted data can therefore still exist in those copies until they expire; they are encrypted and are not used to restore a single account. If a backup is restored, accounts deleted after that backup must be deleted again (keep a list of deletion times from the log line `Workspace ... erased`).
- Application logs contain workspace ids but no content or credentials.
