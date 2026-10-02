# Notion handoff — V1 domain rebuild

The Notion connector is **not authorised in this session**, so nothing was written to
Notion. This file records exactly what to change there, so the project board can be
brought up to date without re-deriving any of it.

To give an agent Notion access later: authorise the Notion connector in claude.ai
connector settings, or run `claude mcp` / `/mcp` from an interactive session. Until
then this file is the handoff.

Nothing in Notion should be deleted. The existing project, research page, Sprint 0,
Sprint 1 and the earlier Codex task records are historical evidence and stay as they
are.

## 1. Existing records to read first (do not duplicate)

- Project: `https://app.notion.com/p/3d4afec61a3f81fba16cd94cf9c5fdee`
- Research: `https://app.notion.com/p/3d4afec61a3f81928cace707a08da80e`
- Completed Sprint 0: `https://app.notion.com/p/3d4afec61a3f81afaf46da73db2ccf48`
- Completed Sprint 1: `https://app.notion.com/p/3d5afec61a3f81dbaa3bdad4bc7c00ec`
- V1 completion task: `https://app.notion.com/p/3d9afec61a3f81ebabd2fad7483e3c7e`

There must be exactly one Medication Tracker project. Do not create a second one.

## 2. Project-level fields to update

| Field | New value |
| --- | --- |
| V1 status | **In progress — domain rebuilt, clients outstanding.** Not complete. |
| Current branch | `claude/v1-domain-rebuild` |
| Open PR | [#11](https://github.com/mFurkanHiz/medication-tracker/pull/11) |
| Production commit | still `0b463d3a3b4afb15ee5fc0873b89fb0d5f4d50a1` — **not** redeployed |
| Architecture decisions | ADR 0013 (rebuild strategy), ADR 0014 (package-first inventory) |
| Scope authority | `docs/v1-acceptance.md`, reconciled 2026-10-02 |
| Resumable checkpoint | `docs/v1-progress.md` |

Add a project note: the owner redefined the inventory domain around medication
definitions, physical packages and auditable consumption allocations. Acceptance rows
whose previous evidence depended on the superseded model were re-opened; no required
V1 item was narrowed or deferred.

## 3. Close out the existing PR #9 record

PR #9 (`Add weekday and interval treatment schedules`) is **subsumed by PR #11**, not
abandoned or rejected. PR #11 branches from PR #9's head, so its commits and authorship
are preserved and its recurrence rules were carried onto the rebuilt plan model.

Set its task status to *Superseded by PR #11* with that explanation. Do not merge PR #9
separately.

## 4. New sprint to create

**Name:** `Medication Tracker — V1 Domain Rebuild`

**Goal:** Deliver the owner-defined package-first V1 — medication definitions, physical
packages, auditable consumption allocations and corrections — across API, web, mobile
and production, without reducing V1 scope.

### Tasks

Status values below reflect reality as of 2026-10-02.

| # | Task | Status | Notes to paste |
| --- | --- | --- | --- |
| 1 | Existing system audit | **Done** | Measured the product at ~1,500 lines of C# plus ~650 of client code outside migration scaffolding. Kept: CI's packaged-migration gate, production compose/deploy/nginx/smoke, session identity, `ExactQuantity`, advisory-lock serialisation, revisioned counts, PR #9 recurrence. Rejected: the single ambiguous `Medication` entity, the capacity/balance package, the absent allocation record, the absent correction path. Recorded in ADR 0013. Also verified the `X-Account-Id` header was **not** an auth bypass — fragile design, not a live hole. |
| 2 | Package-first domain redesign | **Done** | ADR 0014. `MedicationDefinition` → `MedicationPackage` → `AdministrationEvent` → `AdministrationAllocation` → ledger, with corrections as appended reversal + re-charge. `PackageConsumptionPolicy` is a unit-tested domain service. Commit `3f4d0cc`. |
| 3 | Database / migration strategy | **Done** | Hand-assembled migration. EF scaffolded drop-and-create for five populated tables and mis-guessed two `administration_events` renames by position. Replaced with create-copy-drop plus 13 backfills; historical consumption promoted to allocations. `Down` refused; rollback is the verified backup. Commits `a18b8e1`, `d7c0a60`. |
| 4 | API implementation | **Done** | Catalog, inventory/packages, plans, today, dose recording (automatic / specific package / loose / untracked), allocation correction, refill policy and forecast, counts, workspace and activity reads. Identity now read from the validated principal. |
| 5 | Domain and API test coverage | **Done** | 99 local tests. PostgreSQL suite covers the mandatory acceptance scenario end to end, allocation correction conserving the total, concurrency, idempotency, untracked source, cross-household authorisation, capacity snapshotting, retire-through-ledger, refill gap, and count revisioning. |
| 6 | **Web UX rebuild** | **Next** | The web client still calls the superseded endpoints and is currently broken against the new API. This blocks deployment. Endpoint list and UX rules are in `docs/v1-progress.md` under *Next exact action*. |
| 7 | Mobile / offline rebuild | Open | Durable SQLite + outbox, offline Today flow, idempotent sync, no silent last-write-wins on health or inventory records. Needs physical-device acceptance. |
| 8 | Local reminders | Open | Reliability across reboot, notification and exact-alarm permissions, time-zone change, DST, and app update. Physical-device evidence required. Push is not a substitute. |
| 9 | Reports and export | Open | Basic medication / adherence / inventory reporting plus export with no secret or cross-household leakage. |
| 10 | Accessibility | Open | Large text, touch targets, screen-reader labels on core workflows. |
| 11 | Synthetic demo seed | Open | Reproducible seed for safe public demonstration. Invented people and medications only. |
| 12 | TR/EN completion | Open | Every user-facing string through a translation key across the rebuilt surfaces. |
| 13 | Security hardening review | Open | Re-cover every rebuilt endpoint: household authorisation, export authorisation, rate limiting, log redaction, cross-household regression. |
| 14 | Production migration and deploy | **Blocked** | Requires task 6 first, then an owner-approved preflight report. Do not deploy from this branch while the web client is broken. |
| 15 | Final owner acceptance | Open | Owner runs the acceptance flow and explicitly approves. |

## 5. Per-task fields to keep current

For each task above, maintain:

- **Summary** — one line on what it delivers.
- **Requested / Plan** — the acceptance rows it satisfies, by name from
  `docs/v1-acceptance.md`.
- **Status** — as in the table, updated as work lands.
- **Component** — API / Web / Mobile / Database / CI / Deployment / Docs.
- **Environment** — Local / CI / Production.
- **Branch, commit, PR** — `claude/v1-domain-rebuild`, the commit SHA, PR #11.
- **CI** — the run ID that produced the evidence.
- **Implementation notes** — what was built and anything surprising found.
- **Deployment status** — explicitly "not deployed" until the owner approves a preflight.
- **Open risks** — see below.

Do not create a Notion task for every small code change. Track meaningful execution
units only.

Architecture decisions belong in repository ADRs, not only in Notion. Each Notion task
that changes architecture should link to its ADR rather than restating it.

## 6. Open risks to record

1. **The web client is broken against the new API.** Deploying this branch before the
   web rebuild would take the production site down. Deployment is blocked on task 6.
2. **The rebuild migration is not reversible.** `Down` deliberately throws. The rollback
   path is the pre-deployment PostgreSQL backup, which `deploy-production.sh` now takes
   and verifies with `pg_restore -l` before changing anything.
3. **Production is behind main, and now far behind this branch.** Do not assume anything
   on main is live. Verify the running image revision, migration history and schema on
   the VPS during preflight.
4. **Migrations now rename tables.** The deploy script stops this project's api and web
   containers before applying them, which introduces brief downtime. That is deliberate:
   a rename cannot run safely underneath the previous release.
5. **No local database.** This workstation has neither Docker nor PostgreSQL, so all
   database, concurrency and migration evidence comes from GitHub Actions. Any claim
   about database behaviour must cite a CI run.
6. **Mobile reminder reliability is unproven** and cannot be proven in CI. It needs a
   physical Android device.

## 7. Evidence links to attach

- PR: https://github.com/mFurkanHiz/medication-tracker/pull/11
- Commits: `a75338e` (decisions), `3f4d0cc` (domain core), `a18b8e1` (rebuild),
  `d7c0a60` (ordering fixes)
- ADRs: `docs/adr/0013-v1-domain-rebuild-strategy.md`,
  `docs/adr/0014-package-first-inventory-model.md`
- Acceptance contract: `docs/v1-acceptance.md`
- Checkpoint: `docs/v1-progress.md`
- CI run IDs: record the run on PR #11's head commit once green.
