# ADR 0013 — Rebuild the domain and client layers on the existing infrastructure

Status: Accepted — 2026-10-02

Supersedes the product-model parts of ADR 0007 and ADR 0008. Does not supersede
ADR 0001–0004, 0006, or the recurrence rules in ADR 0012.

## Context

The owner took the project back from the previous agent-driven build and rejected
parts of the product/domain model and the overall quality of the resulting
application. A takeover audit of commit `a1c761a` (main `0b463d3a` plus the open
PR #9 schedule work) produced the following measurements.

The application code is far smaller than the repository suggests. Excluding EF
Core migration scaffolding, generated designer files and the lockfile, the whole
product is roughly:

| Area | Lines | Note |
| --- | --- | --- |
| `Modules/Care/CareEndpoints.cs` | 299 | mobile/sync surface |
| `Modules/Care/WorkspaceEndpoints.cs` | 452 | web surface |
| `Modules/Care/SprintOneEntities.cs` | 266 | **all 14 care entities in one file** |
| `Domain/**` | 139 | 4 files |
| `apps/web/src/**` | ~300 | one `Tracker()` component |
| `apps/mobile/src/**` + `App.tsx` | ~350 | |

Roughly 11,000 of the ~13,000 tracked C# lines are migration designer snapshots.
The durable engineering value of this repository is therefore **not** in its
domain code.

### What the audit found to be genuinely good

- **CI** (`.github/workflows/ci.yml`). It extracts `migrations.sql` from the built
  API image and executes that exact file twice against a blank PostgreSQL 18
  database and twice against a seeded seven-migration baseline, then asserts
  ledger/ownership preservation. It also pins image revision labels to the commit
  SHA. This is stronger than most production pipelines and must be preserved.
- **Deployment** (`compose.production.yml`, `deploy/deploy-production.sh`,
  `deploy/nginx/`, `deploy/smoke-test.ps1`). Memory-limited containers, no public
  host ports for API or PostgreSQL, loopback-only web binding, backup before
  migrate, revision-label verification.
- **Session identity**. `__Host-` cookie prefix, SHA-256 token hashes at rest,
  `SameSite=Strict`, a custom-header requirement on mutations, auth rate limiting,
  and `AuthenticationBoundaryTests` proving a client-supplied `X-Account-Id`
  header cannot authenticate.
- **`ExactQuantity`**. Normalised rational with `checked` arithmetic. Correct.
- **Account / Household / Membership / Subscription / Entitlement** are already
  five separate concepts.
- **Ledger-derived balances** and `pg_advisory_xact_lock` household serialisation
  for consumption.
- **Revisioned inventory count batches** with an immutable correction chain.
- The `PostgreSqlApiFactory` / `PostgreSqlFactAttribute` integration-test harness.
- **Recurrence rules from PR #9** — Monday-based weekday bitmask, effective-start
  anchored N-day intervals, `DateOnly` arithmetic rather than 24-hour increments.

### What the audit found to be wrong

1. **The central entity is ambiguous.** `Medication` is a household catalog row
   that also carries `PersonId` ("kept nullable for backwards compatibility"). The
   same type is used to mean "kind of drug" and, through a mandatory 1:1
   `InventoryItem`, "the stock of that drug". A `ck_medications_tablet_form` check
   constraint hard-codes `form = 'tablet'`.
2. **`InventoryPackage` is not a physical package.** It has capacity, an owner and
   a holder, and nothing else. No opened state, no expiry, no lot, no acquisition
   date, no storage location, no stable user-facing ordinal, and no snapshot of the
   base unit. `AssignOwner` writes owner and holder together, collapsing ownership
   and custody.
3. **There is no allocation record.** "Which physical box did this dose come from"
   exists only implicitly, as ledger rows that happen to share an
   `administration_event_id`. Nothing can be queried, displayed, or corrected as a
   first-class fact.
4. **There is no correction path.** The owner's central requirement — the system
   auto-deducted Box 1 but the user actually used Box 2 — is entirely absent. No
   reversal, no re-allocation, no correction audit.
5. **There is no manual package selection and no untracked/external source.** A
   real dose taken with no matching stock can only be dropped or rejected.
6. **The consumption policy is not a policy.** It is
   `CareEndpoints.TryAddPackageAwareConsumption`, a private static method inside an
   endpoint class. It orders candidate packages by *smallest remaining balance*,
   has no notion of expiry or of a user-pinned package, and cannot be unit tested.
7. **Stringly-typed domain.** `ScheduleType`, `RecurrenceKind`, `DayPeriod`,
   `MealRelation`, ledger `Reason`, change-event `Kind` and administration
   `Outcome` are all bare strings validated by scattered literal lists and SQL
   `IN (...)` constraints.
8. **Administration outcomes are `taken` or `skipped` only**, enforced by
   `ck_administration_events_outcome`. Partial and extra doses cannot be recorded.
   `regimen_version_id` is mandatory and
   `(household_id, regimen_version_id, scheduled_for)` is unique, so an extra dose
   in an existing slot is structurally impossible.
9. **Identity travels as a mutated request header.** Middleware strips
   `X-Account-Id` and re-injects it from the validated session; `CareEndpoints`
   then binds it with `[FromHeader]`. This is currently safe and is covered by a
   test, but it makes request-header rewriting load-bearing for authorisation.
10. **Three required V1 areas have no implementation at all**: low-stock warnings,
    official refill eligibility, refill-gap warnings. Reports, export, local
    reminders and accessibility are likewise unimplemented.
11. **The clients are not products.** The web application is a single
    `Tracker()` component holding all state, with sub-200-line files made of
    700-character lines. The mobile application is a 78-line `App.tsx`.

## Decision

**Option B: rebuild the domain, application and client layers; preserve the
infrastructure.** Specifically:

- **Keep** CI, Docker images, compose files, the deployment script, nginx, the
  smoke test, PostgreSQL, EF Core, the session/identity module, `ExactQuantity`,
  the household authorisation concept, the advisory-lock concurrency strategy, the
  integration-test harness, and every applied migration.
- **Rebuild** the care domain around
  `MedicationDefinition → MedicationPackage → AdministrationEvent →
  AdministrationAllocation → InventoryLedgerEntry`, with corrections as new
  append-only entries.
- **Rebuild** the web and mobile clients as real applications.

Full rewrite (Option C) was rejected: the infrastructure listed above is the most
expensive and most nearly correct part of the repository, and the production
database holds live data and ten applied migrations that must survive.

Targeted refactor (Option A) was rejected: five of the owner's requirements
(physical package identity, explicit allocation, allocation correction, untracked
source, manual source selection) cannot be expressed in the current schema without
changing its central entities anyway, and the current domain is small enough that
rebuilding it costs less than bending it.

### Disposition of PR #9

PR #9 (`codex/v1-schedule-patterns`, "Add weekday and interval treatment
schedules") is **kept, not discarded**. Its recurrence logic is exactly what the
owner's schedule requirement asks for and it is correct: a locale-independent
Monday-first bitmask, effective-start-anchored intervals, `DateOnly` arithmetic
across DST boundaries, and tests for each. This rebuild branches **from** PR #9's
head rather than from `main`, so its commits and authorship are preserved, and its
`RecurrenceRule` is carried into the new `TreatmentPlanVersion` model rather than
rewritten. PR #9 is therefore subsumed by this work rather than merged separately.

### Migration posture

Additive and in-place, never destructive. Concepts that survive are renamed and
extended with `RenameTable`/`RenameColumn`/`AddColumn` so existing rows keep their
history; concepts that are new get new tables. No production table is dropped and
no historical ledger, count, loan or administration row is rewritten. The
`medications.person_id` column is retained as a deprecated legacy column rather
than dropped, so the information it holds is not destroyed; the domain stops
reading it.

### Local verification limit

This workstation has neither Docker nor a local PostgreSQL server, so the ten
PostgreSQL integration tests cannot run here and are reported as skipped. Database
concurrency, migration and invariant evidence for this rebuild therefore comes
from GitHub Actions CI runs, which is where it must come from in any case.

## Consequences

- Owner-defined V1 scope is unchanged. This ADR changes *how* the product is
  built, not *what* must be delivered; `docs/v1-acceptance.md` remains the
  authority on completion.
- The acceptance contract gains rows for the newly explicit concepts (physical
  package identity, allocation, allocation correction, untracked source, manual
  source selection) that the previous model could not express. These are
  clarifications of the owner's requirements, not additions to scope.
- Rows previously marked `DONE` against the old model are re-opened where the new
  model changes what the criterion means. A `DONE` row that depended on
  `Medication.PersonId` or on implicit ledger allocation is not evidence for the
  rebuilt model.
- Production deployment of this rebuild requires a preflight report and explicit
  owner approval, as before.
