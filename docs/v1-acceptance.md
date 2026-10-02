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
| 1 | Accounts & households | Account, household, membership, person/profile and future entitlement remain separate concepts, with household authorisation enforced on every care operation. | DONE | Five concepts remain separate (`PersistenceModelTests`). CI `37036017899` drives a second household's signed-in client at every rebuilt read and write and asserts 403 on each, then asserts the target household's stock is unchanged. |
| 2 | Medication definitions | A medication is a reusable household catalog record, never tied to a person. It supports name, brand, active ingredients, strength text, pharmaceutical form, base unit, optional default package capacity, category, tags, notes, extensible external identifiers and archive state. | DONE | `MedicationDefinition` per ADR 0014. The tablet-only check constraint and the person foreign key are gone from the domain; the former person link is retained as `legacy_person_id` rather than destroyed. CI `37036017899`. |
| 3 | Definition lifecycle | Create / read / update / archive preserves history and safely disables incompatible active relationships. | DONE | CI `37036017899` archives a definition and asserts its packages, recorded doses and medication total are intact while the active plan is deactivated and the cascade is audited. |
| 4 | Physical package identity | Each physical container is its own entity with its own identifier. Adding N full boxes creates N packages. Capacity and base unit are snapshotted at creation and are never changed by a later catalog edit. | DONE | CI `37036017899` asserts three distinct identifiers and ordinals from one add-stock call, and that changing the catalog default from 20 to 30 leaves every existing package at 20. |
| 5 | Package state & attributes | A package has a real lifecycle (sealed / opened / disposed / lost / archived) with emptiness **derived** from its ledger balance, plus optional expiry, lot, barcode, acquisition date, source, note, storage location, and separate owner and holder. | DONE | No balance or `IsFull` column exists (`PersistenceModelTests`). CI `37036017899` asserts sealed to opened on first draw, that a retired package is never drawn from again, and that lending moves the holder while the owner is unchanged. |
| 6 | Full vs opened entry | Adding a full package sets remaining = capacity automatically; adding an opened package accepts a remaining amount that cannot exceed capacity. | DONE | Represented as lifecycle state plus ledger balance, not a mutable flag. A remaining amount above capacity is rejected. CI `37036017899`. |
| 7 | Loose inventory | Package-independent stock is supported as an advanced path without displacing the package-first default. | DONE | Loose balance is tracked, is selected only after every eligible package, can be chosen explicitly, and can be the target of a correction. Unit tests plus CI `37036017899`. |
| 8 | Exact quantities | Half / quarter / multiple-unit amounts use exact rational storage. No binary floating-point quantity arithmetic anywhere. | DONE | `ExactQuantity` with `Int128` intermediates that throw rather than wrap. A model test asserts no floating-point column exists in any quantity, capacity, dose or threshold. CI `37036017899` splits a three-halves dose across packages and asserts the parts sum exactly. |
| 9 | Inventory ledger | Every stock change is an append-only typed ledger event (acquire, consume, correction reversal/apply, found, loss, dispose, count adjustment, package allocation, lend, return). No mutable running total is a source of truth. | DONE | `LedgerEntryType` replaces free text; balances are always derived. CI `37036017899` asserts a retired package's stock leaves through a typed `Loss` entry with actor and reason, and that a correction appends three entries summing to the original consumption. |
| 10 | Consumption allocation | Which physical package paid for a dose is a first-class, queryable record. One dose may span several packages, and allocation amounts sum **exactly** to the administered amount. | DONE | `AdministrationAllocation`, with a unique index tying each consuming ledger entry to exactly one allocation. CI `37036017899` asserts the sum equals the administered amount for a split dose. |
| 11 | Deterministic source policy | Package selection is a unit-testable domain policy (pinned, then opened, then earliest expiry, then earliest acquisition), not logic embedded in an endpoint. | DONE | `PackageConsumptionPolicy`, fifteen unit tests plus CI `37036017899` covering pinned preference, opened-before-sealed, and opening the next package when one empties. |
| 12 | Allocation correction | The user can say "I actually used Box 2". The system appends a reversal and a new consumption, keeps the medication total unchanged, never updates the historical ledger row, and shows the correction in history. | DONE | **Owner-flagged as critical.** CI `37036017899` asserts Box C restored, Box B debited, total unchanged, the superseded allocation retained and inactive, the correction visible with from/to labels, and the original ledger entry unmodified. A correction onto a source lacking the stock is refused. |
| 13 | Manual source selection | Recording a dose defaults to automatic selection with no package choice, and optionally accepts a specific package, loose stock, or untracked/external source. | DONE | API proven by CI `37036017899`; the rebuilt web client records a dose with no source field at all and offers a specific package, loose stock or an untracked source only under details. |
| 14 | Untracked / external source | A real dose with insufficient tracked stock can be recorded as untracked/external: the event is kept, no negative stock is created, no ledger entry is written, and the medication is flagged for reconciliation. | DONE | API proven by CI `37036017899` with zero stock present: the event is recorded, no ledger entry and no allocation are written. the rebuilt web client offers it as an advanced source and labels the dose as untracked in history. |
| 15 | Stock safety | A default-path dose can never create negative stock; insufficient stock is rejected atomically with no partial administration, allocation or ledger writes. | DONE | CI `37036017899` asserts a refused dose leaves no administration, no allocation and an unchanged balance. |
| 16 | Concurrency | Two concurrent consumptions cannot spend the same stock. Proven against PostgreSQL, not in-memory. | DONE | CI `37036017899` fires two simultaneous doses at one remaining tablet: exactly one succeeds, the other is refused with `insufficient_stock`, and the balance lands on zero rather than negative. |
| 17 | Idempotency | A replayed command (same idempotency key) never consumes stock twice, including after an offline duplicate sync. | DONE | Server guarantee proven by CI `37036017899`: a replayed key returns the original event and consumes once. The mobile client now closes the other half — the idempotency key is generated and committed to SQLite *before* the request is attempted, so a crash between the write and the response retries the same key rather than issuing a new one. |
| 18 | Inventory counts | Bulk count and reconciliation exists; accepted corrections create a new revision and never rewrite history. Medication-level counting for the default user, package-level reconciliation for advanced users. | PARTIAL | Medication-level counting, revisioning and stale-revision rejection proven by CI `37036017899`, including that a count which matched is still recorded. Package-level reconciliation is modelled (`count_sessions.package_id`) and neither counting surface exists in the client yet. |
| 19 | Lending / return | Whole-package lending preserves ownership while moving custody, does not change stock, and keeps an immutable loan/return history. | DONE | CI `37036017899` asserts the total is unchanged by lending, owner and holder diverge, the owner can no longer draw implicitly, and the borrower can. the rebuilt web client lends and marks returned from the package row. |
| 20 | Treatment schedules | Daily, selected weekdays, every-N-days, exact local time, named periods, optional date range, meal relation, minimum interval and as-needed are all supported. | DONE | PR #9's rules carried onto `TreatmentPlanVersion`. CI `37036017899` covers weekday masks and intervals across a daylight-saving transition and refuses a dose on a non-due day or at a time the plan does not define. |
| 21 | Schedule versioning | Editing a plan creates an effective-dated version; historical administrations stay linked to the version that was in force. Soft delete preserves history. | DONE | CI `37036017899` asserts two immutable versions, the historical dose still linked to version 1 with version 1's dose, and a back-dated edit refused. |
| 22 | Today workflow | The daily flow is one tap: medication, dose, "Taken". No package, ledger, allocation or idempotency concept is visible by default. | DONE | the rebuilt web client records a dose with one button: it sends only the plan version and its scheduled instant, so the amount comes from the plan and the package from the policy. No package, ledger, allocation or idempotency concept is visible by default. |
| 23 | Administration outcomes | Taken, skipped, partial/under-dose and extra/over-dose are recordable without rewriting history. Lateness is **derived** from planned vs actual time rather than stored as a status. | DONE | All four outcomes are recordable and lateness is derived, not stored. The scheduled-slot unique index is filtered so an extra dose in an existing slot is possible while a replayed scheduled dose cannot duplicate. Proven by CI `37036017899`, which also covers a second device recording the same slot: the same outcome replays, a different one is refused with `slot_already_recorded` rather than overwriting. Both clients offer partial and extra doses under details. |
| 24 | Depletion forecast | Planned and actual consumption produce an explainable depletion forecast that respects the real recurrence pattern. | DONE | `RefillForecast` walks real due days rather than an average daily rate, and reports an as-needed-only medication as not forecastable instead of inventing a date. CI `37036017899` asserts a five-day projection to the day. |
| 25 | Low-stock warnings | The user can configure and receive low-stock / depletion warnings. | DONE | Configurable amount threshold and day horizon with the trigger reason reported, proven by CI `37036017899`. the rebuilt web client edits both and shows the warning on the medication. Reminder delivery to a device is row 29. |
| 26 | Official refill eligibility | Official refill / re-prescription eligibility date is modelled **separately** from physical depletion. | DONE | `MedicationRefillPolicy.NextEligibleRefillOn`, never derived from stock. CI `37036017899`. |
| 27 | Refill-gap warning | The product warns when projected depletion falls before official refill eligibility. | DONE | CI `37036017899` asserts depletion on day five against eligibility on day fourteen yields a nine-day gap. the rebuilt web client shows it as a distinct warning, separate from simply running low. |
| 28 | Offline mobile | The Android core daily workflow works fully offline with durable SQLite + outbox persistence, survives app termination, and syncs idempotently later. Conflicts are never silently last-write-wins on health or inventory records. | PARTIAL | Implemented: a SQLite snapshot, a durable outbox whose key is committed before the request, serialised sync, and a Today screen that renders and records with no network at all. Conflicts are marked, never resolved last-write-wins. **Physical-device acceptance is outstanding and cannot be produced in CI** — nothing here has run on an Android device. |
| 29 | Local reminders | Device-local reminders fire offline and recover correctly across reboot, permission changes, time-zone changes, DST transitions and app updates. Push is not a substitute. | PARTIAL | Implemented against the installed library rather than assumed behaviour: it declares `RECEIVE_BOOT_COMPLETED` and registers receivers for `BOOT_COMPLETED` and `MY_PACKAGE_REPLACED`, so reboot and app update recover by themselves; it has no time-zone receiver, so the app reconciles on every foreground. Three limitations are stated rather than hidden: no `SCHEDULE_EXACT_ALARM`, so Android may defer a reminder under Doze; a plan with only a named period gets no reminder, because inventing an hour is exactly what the product refuses to do; and the first reminder after a time-zone change can still fire at the old moment. **Device evidence outstanding.** |
| 30 | Web management | Web manages definitions, packages, people, plans, history, counts, lending, refill, reports and export, at portfolio visual quality, not an admin-panel dump. | PARTIAL | Rebuilt as typed screens per concern: Today, medications with package detail, plans with versioning, people, history, refill settings, lending, and now reports and export. Every screen but the authenticated ones had been browser-verified before; the reports screen was driven against a live API and read back at desktop and 375px in both locales. Still missing from the web surface: **inventory counting**. |
| 31 | Reports | At least the agreed basic medication / adherence / inventory reporting surface exists. | DONE | Two household-scoped reads plus a web screen. Adherence replays the effective-dated schedule over the period, so a missed dose is distinguishable from a day the plan never asked for; a stopped plan keeps the slots it placed before it stopped; an as-needed plan places none. Counts, the exact ratio pair and the inventory view (totals, packages, depletion, refill gap) are proven by `AdherenceReportTests` and `ReportApiTests` and were read off the rendered screen in a browser at desktop and 375px in both locales. |
| 32 | Export | The user can export the agreed basic V1 data with no secret leakage and no cross-household data. | DONE | `GET /export` returns the whole household chain as a downloaded JSON file — definitions, packages, ledger, plan versions, administrations, allocations, corrections, counts — and is covered by the cross-household test this row demands: a second household's rows are absent by name and by identifier, and the file carries no e-mail address, password hash or session token. A non-member is refused. Downloaded from the browser as a real 39 KB file. |
| 33 | Turkish & English | Every user-facing string resolves through a translation key in both locales. No hard-coded user-visible text. | DONE | Both clients resolve every user-visible string through a translation key in Turkish and English, and in both the English dictionary is typed as `Record<MessageKey, string>` so a missing counterpart fails the build rather than shipping an untranslated screen. |
| 34 | Accessibility | Core workflows are usable at large font sizes with adequate touch targets and meaningful screen-reader labels. | PARTIAL | Web: relative units, labelled controls, hints wired through `aria-describedby`, a never-removed focus ring, and a reduced-motion rule; verified in a browser at 375px. Mobile: 48dp touch targets, labelled inputs, announced radio groups and alert live regions. No formal audit has been run and no screen-reader pass has happened on a device. |
| 35 | Demo / synthetic data | A reproducible synthetic demo seed supports safe testing and public portfolio demonstration. No real health data in Git, Notion, screenshots, fixtures or logs. | PARTIAL | Every test and migration fixture is synthetic and labelled as such. A reproducible demo seed does not exist yet. |
| 36 | Unified history / audit | Definition, package, plan, inventory, administration and correction changes appear in one coherent household-scoped activity surface with actor attribution. | DONE | The activity endpoint returns all six kinds including allocation corrections with from/to package labels and actor attribution, proven by CI `37036017899`. the rebuilt web client renders stock movements, recorded doses and corrections as distinct streams rather than flattening them. |
| 37 | Security / privacy | Household authorisation, least privilege, secure cookies, mobile secure storage, CSRF/CORS posture, secret handling, auth rate limiting, export authorisation, cross-household regression tests and log redaction all hold. | PARTIAL | Identity no longer travels as a rewritten request header (ADR 0013, finding 9), and cross-household access is re-tested on every rebuilt endpoint in CI `37036017899`. Export authorisation now holds and is tested: the export is refused to a non-member, returns 401 unauthenticated, and excludes accounts, credentials, sessions and membership rows by construction rather than by filtering. Outstanding: log redaction unverified, mobile storage unreviewed, rate limiting still auth-only. See `docs/security-threat-model.md`. |
| 38 | CI & quality gates | The final V1 commit passes .NET tests, web lint, web production build, mobile typecheck, PostgreSQL integration tests, both Docker builds and the packaged-migration SQL gate. | PARTIAL | CI `37036017899` passed all of these with 124 tests, 0 failed and 0 skipped. The **final** V1 commit must pass again. |
| 39 | Safe migration & deployment | Migrations are additive, non-destructive, idempotent, and tested on blank, current-main and production-baseline-shaped databases. Deployment takes a verified backup, touches only this project's containers, and is gated on an owner-approved preflight. | PARTIAL | Migration side complete: CI `37036017899` applies the packaged SQL twice to a blank database and twice to a seeded production-shaped baseline of all eleven pre-rebuild migrations, then asserts row-by-row preservation. The deploy script now stops only this project's api and web containers before migrating and verifies the backup is readable. The production deployment itself awaits an owner-approved preflight. |
| 40 | Owner acceptance | The owner runs the final agreed acceptance flow and explicitly approves V1. | OPEN | Mandatory final gate. Not approached. |

After the reports and export slice: **30 DONE, 9 PARTIAL, 1 OPEN** of 40 required rows.

A row is only `DONE` when a user can actually reach the behaviour. Several server-side capabilities are deliberately held at `PARTIAL` until a client surfaces them, rather than counted as finished because the API exists.

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
