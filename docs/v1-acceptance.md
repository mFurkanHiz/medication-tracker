# V1 acceptance contract

This document is the source of truth for **owner-accepted V1** scope.

It was reconciled on **2026-10-02** against the owner's takeover instruction, which
redefined the inventory domain around medication definitions, physical packages,
explicit consumption allocations and auditable allocation corrections. Where that
instruction conflicts with an older document, ADR, Notion record or agent summary,
the owner instruction wins and this document records the result. Earlier decisions
remain in Git history; see ADR 0013 and ADR 0014 for the rebuild rationale.

## Completion rule

V1 is complete only when **every required item below is implemented, verified with
evidence, and explicitly accepted by the owner**.

A production deployment, version tag, green CI run, smoke test, merged PR or
partial vertical slice is evidence. None of them alone means V1 is complete.

Do not move a required item to a later release, narrow its meaning, or mark it
optional unless the owner explicitly approves that scope change.

Status legend:

- `DONE` — implemented and verified with recorded evidence.
- `PARTIAL` — meaningful implementation exists but the criterion is not fully met
  or not fully verified.
- `OPEN` — required work remains, or implementation has not been proven.

## Why some previously `DONE` rows were re-opened

The 2026-09-16 revision of this file marked several rows `DONE` against a model in
which `Medication` carried a `PersonId`, a package was a capacity/balance pair, and
consumption allocation existed only implicitly as ledger rows sharing an
`administration_event_id`. The owner has rejected that model.

A row whose evidence depended on the superseded model is **not** evidence for the
rebuilt model. Those rows are re-opened here. This is not a scope reduction and not
a loss of work: the implementations still exist in Git history, and the parts that
remain correct (exact quantities, ledger-derived balances, advisory-lock
serialisation, revisioned counts, lending/return, recurrence rules) are carried
forward rather than rewritten.

## Required V1 product scope

| # | Area | Acceptance criterion | Status | Evidence / remaining gap |
| --- | --- | --- | --- | --- |
| 1 | Accounts & households | Account, household, membership, person/profile and future entitlement remain separate concepts, with household authorisation enforced on every care operation. | PARTIAL | Five concepts are already separate and session identity is sound. Authorisation must be re-proven on every rebuilt endpoint. |
| 2 | Medication definitions | A medication is a reusable household catalog record, never tied to a person. It supports name, brand, active ingredients, strength text, pharmaceutical form, base unit, optional default package capacity, category, tags, notes, extensible external identifiers and archive state. | OPEN | Rebuilt as `MedicationDefinition` per ADR 0014. The old `Medication.PersonId` and the `form = 'tablet'` check constraint are removed from the domain. |
| 3 | Definition lifecycle | Create / read / update / archive preserves history and safely disables incompatible active relationships. | OPEN | Must be re-proven against `MedicationDefinition`. |
| 4 | Physical package identity | Each physical container is its own entity with its own identifier. Adding N full boxes creates N packages. Capacity and base unit are snapshotted at creation and are never changed by a later catalog edit. | OPEN | New requirement made explicit by the owner instruction. ADR 0014 invariants 1, 2, 4. |
| 5 | Package state & attributes | A package has a real lifecycle (sealed / opened / disposed / lost / archived) with emptiness **derived** from its ledger balance, plus optional expiry, lot, barcode, acquisition date, source, note, storage location, and separate owner and holder. | OPEN | New requirement made explicit by the owner instruction. |
| 6 | Full vs opened entry | Adding a full package sets remaining = capacity automatically; adding an opened package accepts a remaining amount that cannot exceed capacity. | OPEN | Must not be represented as a mutable `IsFull` flag. |
| 7 | Loose inventory | Package-independent stock is supported as an advanced path without displacing the package-first default. | PARTIAL | Loose ledger entries already work; must be reconciled with the rebuilt model. |
| 8 | Exact quantities | Half / quarter / multiple-unit amounts use exact rational storage. No binary floating-point quantity arithmetic anywhere. | PARTIAL | `ExactQuantity` is correct and kept; needs comparison/scaling operations and re-proof across the rebuilt paths. |
| 9 | Inventory ledger | Every stock change is an append-only typed ledger event (acquire, consume, correction reversal/apply, found, loss, dispose, count adjustment, package allocation, lend, return). No mutable running total is a source of truth. | PARTIAL | Ledger exists; typed events, correlation, reversal links and full workflow coverage are outstanding. |
| 10 | Consumption allocation | Which physical package paid for a dose is a first-class, queryable record. One dose may span several packages, and allocation amounts sum **exactly** to the administered amount. | OPEN | New requirement. `AdministrationAllocation` per ADR 0014, invariants 6 and 9. |
| 11 | Deterministic source policy | Package selection is a unit-testable domain policy (pinned → opened → earliest expiry → earliest acquisition), not logic embedded in an endpoint. | OPEN | Replaces `CareEndpoints.TryAddPackageAwareConsumption`. |
| 12 | Allocation correction | The user can say "I actually used Box 2". The system appends a reversal and a new consumption, keeps the medication total unchanged, never updates the historical ledger row, and shows the correction in history. | OPEN | **Owner-flagged as critical.** ADR 0014 invariant 7. Requires an automated test. |
| 13 | Manual source selection | Recording a dose defaults to automatic selection with no package choice, and optionally accepts a specific package, loose stock, or untracked/external source. | OPEN | Advanced path only; must not complicate the default flow. |
| 14 | Untracked / external source | A real dose with insufficient tracked stock can be recorded as untracked/external: the event is kept, no negative stock is created, no ledger entry is written, and the medication is flagged for reconciliation. | OPEN | New requirement. The default path must still refuse negative stock. |
| 15 | Stock safety | A default-path dose can never create negative stock; insufficient stock is rejected atomically with no partial administration, allocation or ledger writes. | PARTIAL | Advisory-lock approach is kept; must be re-proven for allocations. |
| 16 | Concurrency | Two concurrent consumptions cannot spend the same stock. Proven against PostgreSQL, not in-memory. | PARTIAL | Existing serialisation is sound; needs an explicit concurrent-consumption regression test. |
| 17 | Idempotency | A replayed command (same idempotency key) never consumes stock twice, including after an offline duplicate sync. | PARTIAL | Receipt tables exist; must be re-proven for allocations and corrections. |
| 18 | Inventory counts | Bulk count and reconciliation exist; accepted corrections create a new revision and never rewrite history. Medication-level counting for the default user, package-level reconciliation for advanced users. | PARTIAL | Revisioned batches are kept; package-level reconciliation is outstanding. |
| 19 | Lending / return | Whole-package lending preserves ownership while moving custody, does not change stock, and keeps an immutable loan/return history. | PARTIAL | Implemented; must be re-integrated with separate owner/holder fields in the rebuilt package. |
| 20 | Treatment schedules | Daily, selected weekdays, every-N-days, exact local time, named periods, optional date range, meal relation, minimum interval and as-needed are all supported. | PARTIAL | PR #9's recurrence logic is correct and carried forward; must be re-proven on `TreatmentPlanVersion`. |
| 21 | Schedule versioning | Editing a plan creates an effective-dated immutable version; historical administrations stay linked to the version that was in force. Soft delete preserves history. | PARTIAL | Behaviour exists on `Regimen`/`RegimenVersion`; carried into `TreatmentPlan`. |
| 22 | Today workflow | The daily flow is one tap: medication, dose, "Taken". No package, ledger, allocation or idempotency concept is visible by default. | OPEN | Default simplicity is an explicit owner requirement, not a nicety. |
| 23 | Administration outcomes | Taken, skipped, partial/under-dose and extra/over-dose are recordable without rewriting history. Lateness is **derived** from planned vs actual time rather than stored as a status. | OPEN | Current schema allows only `taken`/`skipped` and forbids an extra dose in an existing slot. |
| 24 | Depletion forecast | Planned and actual consumption produce an explainable depletion forecast that respects the real recurrence pattern. | PARTIAL | Recurrence-aware projection exists; needs validation against corrections and real workflows. |
| 25 | Low-stock warnings | The user can configure and receive low-stock / depletion warnings. | OPEN | No implementation. |
| 26 | Official refill eligibility | Official refill / re-prescription eligibility date is modelled **separately** from physical depletion. | OPEN | No implementation. |
| 27 | Refill-gap warning | The product warns when projected depletion falls before official refill eligibility. | OPEN | No implementation. |
| 28 | Offline mobile | The Android core daily workflow works fully offline with durable SQLite + outbox persistence, survives app termination, and syncs idempotently later. Conflicts are never silently last-write-wins on health or inventory records. | OPEN | A 78-line `App.tsx` does not meet this. Needs a real rebuild plus physical-device acceptance. |
| 29 | Local reminders | Device-local reminders fire offline and recover correctly across reboot, permission changes, time-zone changes, DST transitions and app updates. Push is not a substitute. | OPEN | No implementation. Requires physical-device evidence. |
| 30 | Web management | Web manages definitions, packages, people, plans, history, counts, lending, refill, reports and export, at portfolio visual quality — not an admin-panel dump. | OPEN | Current web is one `Tracker()` component. |
| 31 | Reports | At least the agreed basic medication / adherence / inventory reporting surface exists. | OPEN | No implementation. |
| 32 | Export | The user can export the agreed basic V1 data with no secret leakage and no cross-household data. | OPEN | No implementation. |
| 33 | Turkish & English | Every user-facing string resolves through a translation key in both locales. No hard-coded user-visible text. | PARTIAL | Structure exists; completion is unproven and must cover all rebuilt surfaces. |
| 34 | Accessibility | Core workflows are usable at large font sizes with adequate touch targets and meaningful screen-reader labels. | OPEN | No evidence. |
| 35 | Demo / synthetic data | A reproducible synthetic demo seed supports safe testing and public portfolio demonstration. No real health data in Git, Notion, screenshots, fixtures or logs. | PARTIAL | Synthetic test data exists; a reproducible demo seed does not. |
| 36 | Unified history / audit | Definition, package, plan, inventory, administration and correction changes appear in one coherent household-scoped activity surface with actor attribution. | PARTIAL | An activity surface exists; must cover allocations and corrections. |
| 37 | Security / privacy | Household authorisation, least privilege, secure cookies, mobile secure storage, CSRF/CORS posture, secret handling, auth rate limiting, export authorisation, cross-household regression tests and log redaction all hold. | PARTIAL | Foundation is sound. Identity must stop travelling as a mutated request header (ADR 0013, finding 9), and every rebuilt endpoint must be re-covered. |
| 38 | CI & quality gates | The final V1 commit passes .NET tests, web lint, web production build, mobile typecheck, PostgreSQL integration tests, both Docker builds and the packaged-migration SQL gate. | PARTIAL | Pipeline is strong and kept; must pass on the final commit. |
| 39 | Safe migration & deployment | Migrations are additive, non-destructive, idempotent, and tested on blank, current-main and production-baseline-shaped databases. Deployment takes a verified backup, touches only this project's containers, and is gated on an owner-approved preflight. | PARTIAL | Existing gates are strong; the rebuild's migration must pass all three shapes. |
| 40 | Owner acceptance | The owner runs the final agreed acceptance flow and explicitly approves V1. | OPEN | Mandatory final gate. |

## Mandatory acceptance scenario

This exact scenario, specified by the owner, must be covered by a domain/unit test,
an API integration test against PostgreSQL, and a web acceptance check where
feasible.

Given the synthetic definition `Parol 500 mg Tablet` with a default package size of
20 tablets, and stock:

| Package | Remaining / capacity | State |
| --- | --- | --- |
| Box A | 20 / 20 | sealed |
| Box B | 20 / 20 | sealed |
| Box C | 8 / 20 | opened |

- Total reads **48 tablets across 3 packages**.
- One normal dose of 1 tablet draws automatically from **Box C** (opened before
  sealed) leaving A = 20, B = 20, C = 7, total 47.
- The user then records the next dose explicitly from **Box B**, leaving B = 19,
  C = 7.
- If the system had auto-charged Box C and the user corrects it to Box B, then after
  the correction Box C is restored, Box B is debited, **the total is unchanged**, and
  the correction is visible in history.
- When Box C reaches zero, the next automatic dose opens the next eligible package.

## What the existing `v1.0.0` tag means

The 2026-09-14 `v1.0.0` tag is a **technical production baseline**, not
owner-accepted V1. It proved a subset could be built, deployed and smoke-tested. It
is not evidence that any requirement in this table is satisfied.

## Explicitly out of V1 scope

Not required now, and the architecture must not foreclose them: clinical diagnosis,
automatic dose recommendation, drug-interaction checking, AI medical advice, e-Nabız
integration, OCR, barcode-scanning UI, pharmacy integration, advanced
liquid/cream/injection dose calculation, payments and subscriptions, Kubernetes and
microservices.

## Product boundary

This is a medication organisation and adherence product. It records what the user or
their clinician decided. It never diagnoses, never recommends a dose, never invents
a drug interaction, and never tells the user what to take.

## Evidence rules

For a row to move to `DONE`, record concrete evidence in the commit, PR or
`docs/v1-progress.md`:

- implementation commits,
- targeted automated tests naming the invariant they prove,
- PostgreSQL integration tests where database behaviour matters,
- a CI run ID,
- physical-device evidence where Android behaviour matters,
- production smoke evidence where deployment behaviour matters,
- explicit owner confirmation for UX and product acceptance.

A row does not become `DONE` because a type, file or endpoint exists.

## Scope authority

If `PROJECT.md`, `docs/roadmap.md`, a Notion task, a release note, an ADR or an
agent summary conflicts with this document about V1 completion, **this document
wins unless the owner explicitly changes V1 scope**.
