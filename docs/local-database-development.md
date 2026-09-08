# Local database development

## Configuration

Copy `.env.example` to `.env`. The `.env` file is ignored by Git. Replace the
example password and export the same connection string before starting the API:

```powershell
Copy-Item .env.example .env
$env:ConnectionStrings__Database = 'Host=127.0.0.1;Port=5432;Database=medication_tracker;Username=medication_tracker;Password=<local-password>'
```

Only synthetic data belongs in the development and test databases. Never place a
real connection string, health record, or credential in source control.

## Start PostgreSQL and apply migrations

```powershell
docker compose -f compose.dev.yml up -d database
dotnet tool restore
dotnet ef database update --project apps/api --startup-project apps/api
```

The development port is bound to `127.0.0.1`. This host binding exists only so a
host-launched API and test runner can connect. Production PostgreSQL must be reachable
only over the private application network and must not have a `ports` mapping.

PostgreSQL 18 stores its version-specific data directory below
`/var/lib/postgresql`, so the named volume intentionally mounts that parent path.
Do not change it to the pre-18 `/var/lib/postgresql/data` mount.

## Migration discipline

Create one coherent migration for each schema change:

```powershell
dotnet ef migrations add <MigrationName> --project apps/api --startup-project apps/api --output-dir Persistence/Migrations
```

Review the migration and model snapshot. Before deployment, generate and review an
idempotent script instead of granting the API schema-change privileges:

```powershell
dotnet ef migrations script --idempotent --project apps/api --startup-project apps/api --output artifacts/migrations.sql
```

Do not edit accepted historical migrations. Add a corrective migration.

## Health checks

- `/health/live` proves that the API process is running and does not depend on the database.
- `/health/ready` checks that `MedicationTrackerDbContext` can connect to PostgreSQL.
- `/health` runs all registered checks.

## PostgreSQL integration test

The ordinary API suite skips the Docker-dependent test when no explicit test
connection is supplied. To run it against the local Compose database:

```powershell
$env:MEDICATION_TRACKER_TEST_POSTGRES = $env:ConnectionStrings__Database
dotnet test MedicationTracker.slnx
```

The test applies pending migrations, checks `/health/ready`, opens a transaction,
writes a synthetic `.invalid` account, verifies it, and rolls the transaction back.
The API job in `.github/workflows/ci.yml` supplies an isolated PostgreSQL service,
so this test is mandatory rather than skipped in CI.
