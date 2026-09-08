# ADR 0002: PostgreSQL persistence and explicit migrations

- Status: Accepted
- Date: 2026-09-08

## Decision

Use EF Core with the Npgsql provider for server persistence. Keep one
`MedicationTrackerDbContext` for the modular monolith while mapping module-owned
tables into separate PostgreSQL schemas. Store EF migration history in the
`infrastructure` schema.

Apply migrations as an explicit operational step. The API must not call
`Database.Migrate` during startup. Generate a reviewed idempotent SQL script for
deployment, and use `dotnet ef database update` only for local development.

Model accounts, households, effective-dated household memberships, subscriptions,
and entitlements as separate records. Database constraints ensure that a future
subscription targets exactly one account or household; entitlement never replaces
household authorization.

## Context

The first vertical slice needs durable PostgreSQL storage and integration-test
transactions. It must also retain multi-household authorization seams and avoid
coupling future paid plans to identity or membership. Automatic startup migration
would give every API instance schema-change privileges and can race during rollout.

## Consequences

- Module ownership is visible in schema names without splitting the server into
  multiple services or databases.
- Migration SQL can be reviewed, applied once, and audited independently of API
  startup.
- Readiness reports database connectivity while liveness remains independent of
  PostgreSQL availability.
- Local PostgreSQL is bound to loopback; a future production Compose definition
  must omit host port publication entirely.
