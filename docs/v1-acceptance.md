# V1 acceptance contract

This document is the source of truth for **owner-accepted V1** scope. It restores the approved product baseline from the original Medication Tracker research/MVP definition and prevents a technical milestone, partial vertical slice, or production deployment from being relabeled as complete V1.

## Completion rule

V1 is complete only when **every required item below is implemented, verified with evidence, and accepted by the owner**.

A production deployment, version tag, green CI run, or smoke test is evidence, but none of them alone means V1 is complete.

Do not move a required item to a later release, narrow its meaning, or mark it optional unless the owner explicitly approves that scope change.

Status legend:

- `DONE` — implemented and verified with evidence.
- `PARTIAL` — meaningful implementation exists, but the V1 acceptance criterion is not fully met or not fully verified.
- `OPEN` — required work remains or implementation has not been proven.

## Required V1 product scope

| Area | Acceptance criterion | Current status (2026-09-16) | Evidence / remaining gap |
| --- | --- | --- | --- |
| Accounts & households | Account, household membership and person/profile foundation works with household authorization. | DONE | Authenticated household-scoped web/API core is live. |
| Medication catalog | Medication can exist independently of a person and supports core descriptive metadata. | DONE | Delivered in the current production baseline. |
| Medication lifecycle | Full medication create/read/update/soft-delete behavior preserves history and disables incompatible active relationships safely. | DONE | PR #2 implements audited update/soft-delete, cascades active-plan deactivation, and passes PostgreSQL household-authorization/history coverage in CI run `34984202695`. |
| Package inventory | Full and opened packages keep exact capacity and remaining stock; package-to-person allocation is auditable. | DONE | Delivered in current production baseline. |
| Exact quantities | Half/quarter/multiple-tablet quantities use exact rational storage; no binary floating-point quantity math. | DONE | Exact numerator/denominator model exists. |
| Stock safety | A use event cannot create negative stock; insufficient stock is rejected atomically without partial administration/ledger writes. | DONE | PR #2 serializes household consumption, checks eligible exact stock before writes, and verifies zero partial administration/ledger writes in PostgreSQL CI run `34984202695`. |
| Inventory ledger | Stock acquisition, consumption and corrections are represented as auditable ledger events rather than silent total overwrites. | PARTIAL | Core ledger exists; full V1 event coverage still needs final acceptance. |
| Inventory counts | Bulk count/reconciliation exists and accepted count corrections are revisioned rather than rewriting history. | DONE | PR #5 adds atomic multi-medication count batches, immutable correction chains, stale-revision rejection, idempotency and household authorization; all 17 API tests pass against PostgreSQL in CI run `35020426322`. |
| Lending / return | Medication can be lent/borrowed/returned between people while preserving ownership/allocation and audit history. | DONE | PR #6 implements whole-package lend/return between household people with separate owner/current holder, immutable assignment/activity history, idempotent transitions, authorization and exact stock conservation (including use during a loan); PostgreSQL API and client/Docker CI run `35084942604` passed. |
| Treatment schedules | Daily, selected-weekday and interval-based schedules are supported, including exact times and named periods where applicable. | PARTIAL | Regular/as-needed, exact time and named period exist; selected-weekday/arbitrary interval acceptance is not proven. |
| Schedule versioning | Editing a treatment schedule creates an effective-dated version; historical administration remains linked to the historical version. Soft-delete preserves history. | DONE | PR #2 creates immutable dated versions, selects the latest version valid for the requested day, preserves historical administration links, and soft-deletes the regimen root; covered by PostgreSQL CI run `34984202695`. |
| Dose context | Optional date ranges, meal relation and minimum interval are supported. | DONE | Delivered in current production baseline. |
| Administration history | Today flow records required outcomes: taken, skipped, late, partial/under-dose, extra/over-dose and correction semantics without rewriting history. | PARTIAL | Taken/skipped foundation exists; full original MVP outcome set is not proven. |
| Depletion forecast | Planned and actual stock consumption support an explainable depletion forecast. | PARTIAL | Projection exists; final V1 validation must cover real workflows and corrections. |
| Low-stock warnings | User can configure/use low-stock or depletion warnings. | OPEN | Current roadmap still lists alerts as remaining work. |
| Official refill eligibility | Official refill/re-prescription eligibility date is modeled separately from physical depletion. | OPEN | Required by approved MVP but not delivered in current baseline. |
| Refill-gap warning | Product warns when projected depletion precedes official refill eligibility. | OPEN | Required by approved MVP but not delivered in current baseline. |
| Offline mobile | Android core daily workflow works offline with durable SQLite/outbox persistence and later idempotent sync. | PARTIAL | Offline/sync implementation exists; final physical-device acceptance is deferred/not complete. |
| Local reminders | Device-local medication reminders work offline and recover safely after reboot/permission/time-zone/DST changes. | OPEN | Reliable notification delivery and recovery tests remain outstanding. |
| Web management | Web supports the V1 management workflows against the authenticated API. | PARTIAL | Core web is live; missing CRUD/audit/V1 workflows must be completed. |
| Reports | At least the agreed basic medication/adherence/inventory reporting surface exists. | OPEN | Not proven in current V1 baseline. |
| Export | User can export the agreed basic V1 data without exposing secrets or cross-household data. | OPEN | Not proven in current V1 baseline. |
| Turkish & English | All V1 user-facing text is covered by TR/EN localization and no core V1 screen depends on hard-coded single-language text. | PARTIAL | Translation structure exists; roadmap says localization completion remains. |
| Accessibility | V1 supports accessible large text / usable scaling for core workflows. | OPEN | Original MVP requirement; final implementation evidence missing. |
| Demo / synthetic data | Reproducible synthetic/demo data supports safe testing and portfolio demonstration without real health data. | PARTIAL | Synthetic smoke data exists; owner-facing V1 demo dataset/flow requires final acceptance. |
| Audit/history view | Medication, schedule, inventory/package and administration changes are visible in a coherent activity/history surface. | DONE | PR #2 returns and renders one household-scoped chronological activity surface for medication, regimen, package assignment, inventory and administration changes; covered by PostgreSQL CI run `34984202695`. |
| Security/privacy | Household authorization, secret handling and synthetic-data-only repository rules pass regression testing. | PARTIAL | Foundation exists; every new V1 endpoint/workflow must preserve it. |
| CI & deployment | Final V1 commit passes API tests, web lint/build, mobile typecheck/build checks, production Docker build/deploy and production smoke tests. | PARTIAL | Previous technical release passed; final owner-V1 commit must pass again. |
| Owner acceptance | Owner runs the final agreed acceptance flow and explicitly approves V1. | OPEN | Mandatory final gate. |

## What the existing `v1.0.0` means

The 2026-09-14 `v1.0.0` tag is a useful **technical production baseline**, not owner-accepted V1. It proved that a substantial subset of the system could be built, deployed and smoke-tested. It must not be used as evidence that all original V1 requirements are complete.

## Acceptance evidence rules

For a row to move to `DONE`, record concrete evidence in the commit/PR or `docs/v1-progress.md`, such as:

- implementation commit(s),
- targeted automated tests,
- relevant integration tests,
- CI run,
- physical-device test when the feature depends on Android/device behavior,
- production smoke evidence where deployment behavior matters,
- owner confirmation for UX/product acceptance.

Do not mark an item `DONE` only because a type/entity/file exists.

## Scope authority

If `PROJECT.md`, `docs/roadmap.md`, a Notion task, an older release note, or an agent summary conflicts with this document about V1 completion, **this document wins unless the owner explicitly changes V1 scope**.
