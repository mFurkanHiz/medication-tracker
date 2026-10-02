# Medication Tracker

An offline-first household medication inventory and adherence platform for web and
mobile, built around a question most medication apps cannot answer: **which physical
box did that tablet come from, and what happens when the answer was wrong?**

Live web application:
[medicationtracker.rapidconfigs.com](https://medicationtracker.rapidconfigs.com)

> **Status.** The live site runs the previous release. The package-first domain rebuild
> described below is on `claude/v1-domain-rebuild` and is not deployed yet; the web and
> mobile clients are being rebuilt against it. Owner-accepted V1 is not complete — see
> [`docs/v1-acceptance.md`](docs/v1-acceptance.md).

## The problem

A household does not own "48 tablets of paracetamol". It owns three boxes: two sealed
boxes of 20 and one opened box with 8 left. Someone takes a tablet out of one specific
box. Later they realise they took it from a different box than the app assumed.

Most trackers model this as a single mutable `quantity` field, which cannot represent
any of it. This one models four separate realities and keeps them separate:

| Reality | Entity |
| --- | --- |
| What a medication *is* | `MedicationDefinition` — reusable household catalog, never person-owned |
| Which containers exist | `MedicationPackage` — one row per physical box, with its own identity |
| What is planned | `TreatmentPlan` + immutable effective-dated versions |
| What happened, and which box paid | `AdministrationEvent` + `AdministrationAllocation` |

## What makes it interesting

**Package-first inventory.** Adding "2 full boxes of 20" creates two package rows, not
one row of forty. Each snapshots its own nominal capacity, so changing the catalog's
default box size from 20 to 30 can never retroactively resize a box that already
exists. A package stores no balance at all — remaining amount, and emptiness with it,
is derived from an append-only ledger, so the two cannot drift apart.

**Corrections that preserve history.** The system auto-charged Box 1; the user says
they actually used Box 2. Nothing is updated. A matched reversal and re-charge are
appended under one correlation, the superseded allocation is retained but marked
inactive, and the medication total is unchanged *by construction* rather than by
arithmetic that could drift. The activity view shows "stock source corrected from
Box 1 to Box 2". A correction onto a box that lacks the stock is refused, because
reality disagreeing with the ledger is a counting problem, not a reason to permit
negative stock.

**Exact quantities.** A half tablet is `1/2`, never `0.5`. Every quantity is a
normalised rational stored as an integer numerator/denominator pair; arithmetic widens
to `Int128` and throws on overflow rather than wrapping. A test asserts that no
floating-point column exists in any quantity, capacity, dose or threshold.

**Simple by default, powerful when needed.** The everyday flow is one tap — medication,
dose, Taken — and names no package, amount or ledger concept. Choosing a specific box,
loose stock, an untracked external source, a partial dose or an extra dose are all
optional fields on the same command, surfaced only under details.

**A real answer for untracked doses.** Someone genuinely took a dose from a strip in
their bag. The event is recorded with its real amount and time, no ledger entry is
written, and no package goes negative — so the product never has to choose between
losing a health record and corrupting its inventory.

**Depletion versus eligibility.** Physical depletion and official prescription refill
eligibility are separate facts, and the gap between them is what the product warns
about. The forecast walks each plan's real due days rather than dividing by an average,
because a Monday/Thursday plan does not consume a constant amount per day.

**Offline-first with provable idempotency.** Mobile commands commit to SQLite first and
sync through a durable outbox. A replayed command returns its original result instead
of consuming stock twice, enforced by unique database indexes rather than application
convention. All stock-mutating work for a household is serialised by a per-household
advisory lock, so two concurrent doses cannot spend the same tablet — proven against
real PostgreSQL, not an in-memory provider.

## Architecture

```text
Next.js web ─┐
             ├─► ASP.NET Core API (modular monolith) ─► PostgreSQL
Expo mobile ─┘        │
   └─ SQLite + outbox │
                      ├─ Domain      pure, no persistence: exact quantities,
                      │              consumption policy, correction planner,
                      │              recurrence rule, refill forecast
                      ├─ Application transactional services spanning modules
                      └─ Modules     catalog · inventory · treatments ·
                                     administrations · refill · people ·
                                     identity · households · sync · audit
```

Each module owns its own PostgreSQL schema. The pure domain has no database
references, so the selection policy, correction arithmetic and recurrence rules are
directly testable. .NET 10, EF Core, PostgreSQL 18, Next.js, Expo React Native,
Docker Compose behind nginx and Cloudflare, GitHub Actions.

Deliberately **not** used without a measured need: microservices, Kubernetes, Kafka,
event buses.

Design decisions are recorded as ADRs in [`docs/adr`](docs/adr). The two that explain
the current shape are
[0013 — rebuild strategy](docs/adr/0013-v1-domain-rebuild-strategy.md) and
[0014 — package-first inventory](docs/adr/0014-package-first-inventory-model.md).
The model itself is described in [`docs/domain-model.md`](docs/domain-model.md).

## Repository layout

```text
apps/api       .NET 10 ASP.NET Core API
apps/web       Next.js web application
apps/mobile    Expo React Native mobile application
docs           architecture, domain model, ADRs, and roadmap
tests          automated .NET tests
```

## Prerequisites

- .NET SDK 10
- Node.js 22 or later
- pnpm 10
- Android SDK for device builds
- Docker for the local PostgreSQL infrastructure

## Validate the workspace

```bash
dotnet test MedicationTracker.slnx
pnpm install
pnpm lint
pnpm typecheck:mobile
pnpm build:web
```

## Local PostgreSQL

Copy `.env.example` to a git-ignored `.env`, replace the development-only password,
and start PostgreSQL:

```bash
docker compose -f compose.dev.yml up -d database
dotnet tool restore
dotnet ef database update --project apps/api --startup-project apps/api
```

Supply `ConnectionStrings__Database` to run the API. Supply
`MEDICATION_TRACKER_TEST_POSTGRES` and run `dotnet test MedicationTracker.slnx` to
include the PostgreSQL migration, readiness-health, and transaction rollback test.
Without that variable, PostgreSQL-dependent tests are reported as skipped.

## Mobile offline sync

The mobile app always commits person, medication, regimen, administration, ledger,
and outbox changes to SQLite first. To exercise upload against a local API, copy
`apps/mobile/.env.example` to `apps/mobile/.env.local` and set the API URL if needed.
Sign in through the app. Session credentials are kept in Expo SecureStore, and each
household has a separate SQLite cache. Client-supplied account UUIDs are rejected.

Outbox rows are sent in creation order whenever the app becomes active or the user
chooses **Şimdi eşitle**. A server acknowledgement is recorded locally only after a
successful response, so a crash or connection loss safely retries the same
household-scoped idempotency key.

The development database port binds to `127.0.0.1` by default. Production must use
an internal container network and must not publish PostgreSQL on a host port. See
[local database development](docs/local-database-development.md).

## Product safety

Medication Tracker records user-entered treatment instructions. It does not diagnose, prescribe, or recommend dose changes. Only synthetic demonstration data belongs in this public repository.

See [PROJECT.md](PROJECT.md), [architecture](docs/architecture.md), [domain model](docs/domain-model.md), and [roadmap](docs/roadmap.md).

## Web hosting

The web client calls the authenticated API through same-origin `/api/` requests.
The custom hostname is `medicationtracker.rapidconfigs.com`. PostgreSQL and the API
run on the isolated project container network with no published host ports.

The production web application runs on the VPS behind Cloudflare as an isolated
container bound to `127.0.0.1:3022`. See [web VPS deployment](docs/web-vps-deployment.md).

## Android physical-device development

Enable USB debugging on the phone, connect it by USB, unlock it, and accept the
computer's RSA authorization prompt. On Windows, point the shell at JDK 17 and the
installed Android SDK before building:

```powershell
$env:JAVA_HOME = 'C:\Program Files\Java\jdk-17.0.2'
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$env:Path = "$env:JAVA_HOME\bin;$env:ANDROID_HOME\platform-tools;$env:Path"
adb devices -l
pnpm --filter mobile android:device
```

The expected Android application ID is `com.rapidconfigs.medicationtracker`. Expo
generates the native `apps/mobile/android` directory for local builds; it remains
git-ignored and must not contain committed signing material. The workspace keeps
pnpm's virtual store at the short `.p` path so native CMake builds remain within
Windows path-length limits.
