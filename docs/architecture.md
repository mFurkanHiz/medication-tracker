# Architecture

## System shape

Medication Tracker starts as a modular monolith with three clients/components:

- ASP.NET Core API backed by PostgreSQL
- Next.js web application for administration and reporting
- Expo React Native mobile application backed by local SQLite

Mobile commands are written to SQLite first. A durable outbox syncs them to the API when a connection is available. The API validates household authorization, applies commands idempotently, and returns changes after a client cursor.

## Module boundaries

- Identity and Access
- Households
- People
- Medication Catalog
- Treatments and Regimens
- Dose Scheduling
- Administrations
- Inventory
- Prescriptions and Refill Eligibility
- Notifications
- Notes and Attachments
- Sync
- Audit
- Subscriptions and Entitlements (future adapter boundary only)

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

## Security baseline

- Health data is sensitive; use least privilege and household-scoped authorization.
- TLS is mandatory outside local development.
- Secrets remain outside Git and are injected at runtime.
- Attachments use private storage and short-lived access URLs.
- Audit security-sensitive access and all material health/inventory changes.
- Support consent, export, deletion, backup, and restore workflows before public production use.
