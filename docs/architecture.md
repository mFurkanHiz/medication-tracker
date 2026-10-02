# Architecture

## System shape

Medication Tracker starts as a modular monolith with three clients/components:

- ASP.NET Core API backed by PostgreSQL
- Next.js web application for administration and reporting
- Expo React Native mobile application backed by local SQLite

The web application uses a Next.js client export and calls the ASP.NET API over
same-origin `/api/` routes. Authentication uses revocable server sessions and
Secure HttpOnly browser cookies. Only the API has PostgreSQL credentials. The
database and API have no published host ports. See ADR 0006 for mobile sessions.

Mobile commands are written to SQLite first. A durable outbox syncs them to the API when a connection is available. The API validates household authorization, applies commands idempotently, and returns changes after a client cursor.

## Module boundaries

The rebuild replaced a single `Modules/Care/SprintOneEntities.cs` holding fourteen
entities with one directory per boundary, each mapped to its own PostgreSQL schema.
See ADR 0013.

| Module | Schema | Owns |
| --- | --- | --- |
| Identity and Access | `identity` | Accounts, revocable sessions |
| Households | `households` | Households, effective-dated memberships |
| People | `care` | The people whose medication is organised |
| Medication Catalog | `catalog` | Medication definitions and their audit history |
| Inventory | `inventory` | Physical packages, the append-only ledger, loans, custody events, counts |
| Treatments | `treatments` | Plans, immutable effective-dated versions, plan audit history |
| Administrations | `administrations` | Recorded doses, consumption allocations, allocation corrections |
| Refill | `refill` | Low-stock settings and official refill eligibility |
| Sync | `sync` | Idempotency receipts for offline commands |
| Subscriptions and Entitlements | `subscriptions` | Future commercial boundary only |

Still to come as their own boundaries: Notifications, Reporting, and Notes and
Attachments.

Cross-module reads go through the module that owns the data. The application layer
(`apps/api/Application`) holds the transactional services that span modules —
household authorisation, stock projection, and dose recording with its allocation
writes — because reading a balance and writing its consumption must be one
serialised unit.

The pure domain (`apps/api/Domain`) holds no persistence references at all: exact
quantities, the package consumption policy, the allocation correction planner, the
recurrence rule and the refill forecast are all directly testable without a database.

## Identity, household, and membership

An account can belong to zero or more households. A household can contain several managed people and several account memberships. Household roles govern access to health data. A future commercial subscription grants entitlements to an account or household but does not replace authorization.

The initial UI creates one default household. No persistence rule assumes that it is the only household.

Persistence keeps these concepts in separate PostgreSQL schemas and tables:

- `identity.accounts` stores user identity without implying household access.
- `households.households` and effective-dated `households.household_memberships`
  store authorization membership independently from identity.
- `subscriptions.subscriptions` and `subscriptions.entitlements` are a future
  commercial boundary. A subscription belongs to exactly one account or one
  household and never grants a household role by itself.

EF Core migrations live with the API under `Persistence/Migrations`. The API does
not apply migrations at startup. Deployment must first generate and review an
idempotent SQL script, then run it as a separate controlled step. Migration history
uses the `infrastructure` schema.

## Offline and synchronization

- Client-generated UUIDs allow offline creation.
- Sprint 1 mutations carry client-generated UUIDs and a household-scoped idempotency key. The full conflict protocol will add device ID, logical timestamp, and base record version before multi-device editing ships.
- Server changes are read incrementally with an opaque cursor.
- Deletions use tombstones until all relevant devices have synchronized.
- Clinical and inventory conflicts are surfaced; they are never silently resolved with last-write-wins.
- Local dose notifications are the offline reliability path. Server push is a secondary cross-device/caregiver channel.
- A replayed command returns its original result rather than applying twice, enforced
  by unique indexes on `(household_id, idempotency_key)` rather than by application
  convention. A duplicated offline sync therefore cannot double-consume stock.
- Stock-mutating work for one household runs inside a transaction holding a
  per-household advisory lock, so two concurrent doses cannot each read the same
  balance and both decide there is enough.

## Security baseline

- Health data is sensitive; use least privilege and household-scoped authorization.
- TLS is mandatory outside local development.
- Secrets remain outside Git and are injected at runtime.
- Attachments use private storage and short-lived access URLs.
- Audit security-sensitive access and all material health/inventory changes.
- Support consent, export, deletion, backup, and restore workflows before public production use.
