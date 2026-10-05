# Roadmap

## Delivery status — 2026-09-15

The 2026-09-14 `v1.0.0` release is live and passed CI plus production smoke/acceptance for the subset it implemented. It is a **technical production baseline, not owner-accepted V1**.

Owner-accepted V1 is governed by [`v1-acceptance.md`](./v1-acceptance.md). The original approved MVP/V1 scope includes several items that were previously listed below as later Sprint 2/3 work. Those items remain V1 requirements unless the owner explicitly changes scope. Current resumable execution state is in [`v1-progress.md`](./v1-progress.md).

Delivered in the technical baseline includes person-free medication entry, exact full/opened packages, package assignment, category/tag/status filters, and regular/as-needed plans with optional dates, times, named periods and meal relation. Mobile authenticated sync is implemented, but physical-device final acceptance and other V1 gaps remain.

**Scope change — 2026-10-04 (ADR 0015).** The owner explicitly moved the mobile client past V1 and kept its infrastructure. The sentence above about physical-device acceptance is therefore now history: V1 is a web release, acceptance rows 28 and 29 are `DEFERRED` rather than outstanding, and the mobile sprint items listed further down this roadmap belong to a later version. The remaining V1 work is web and operational.

## Sprint 0 — Foundation

- Repository, CI quality gates, architecture decisions, and local developer workflow
- Project isolation and contributor/agent guidance
- Initial API, web, mobile, and test applications
- Security and privacy threat-model baseline

## Sprint 1 — First vertical slice

- Household, membership, and person foundation
- Tablet medication and exact fractional quantity
- Inventory acquisition ledger
- Effective-dated daily regimen
- Today view and taken/skipped administration
- Offline mobile queue and idempotent sync proof
- Stock depletion projection

## Sprint 2 — Inventory operations

- Package/opened-package support — delivered in technical baseline
- Package-to-person assignment audit — delivered in technical baseline
- Inventory count sessions and bulk reconciliation — required for owner V1
- Count revision/correction workflow — required for owner V1
- Low-stock thresholds and forecast notifications — required for owner V1
- Lending, returning, and ownership allocation — required for owner V1
- Official refill eligibility and gap alerts — required for owner V1

## Sprint 3 — Reliable schedules

- Exact-time and named-period schedule recording — delivered in technical baseline
- Meal relation, as-needed use and minimum-interval recording — delivered in technical baseline
- Selected-weekday and interval-based scheduling — required for owner V1
- Time-zone, daylight-saving, reboot, notification-permission and rescheduling acceptance — required for owner V1
- Turkish and English localization completion — required for owner V1

## V1 completion work across slices

These requirements cut across the earlier sprint labels and are tracked in `v1-acceptance.md`:

- full medication CRUD with history-preserving soft-delete,
- full treatment-plan CRUD with effective-dated versioning,
- atomic insufficient-stock rejection,
- complete administration outcome/history semantics,
- coherent medication/regimen/inventory/administration activity view,
- web management completion,
- basic reporting and export,
- accessible large-text behavior,
- synthetic/demo data and final owner acceptance.

## Later

- Multiple-household UI beyond the V1 domain/authorization foundation
- Caregiver collaboration and escalation
- Paid subscriptions and entitlements
- Liquid, drop, cream, inhaler, and injection workflows
- Barcode/OCR assisted entry
- Expiry and lot tracking
- Advanced reports, symptoms, measurements, and approved integrations

Recorded from the owner's live-test notes of 2026-10-05, for versions after the next one;
the focus stays medication tracking and its reminders. Evaluation in
`docs/owner-feedback-2026-10-05.md`.

- Reminders on the web — the product's core, currently a mobile responsibility while the
  mobile client is deferred (ADR 0015). The next planning round picks the delivery path:
  resume mobile, web push from the Next.js app, or both. "Remind N minutes before",
  "remind again" and snooze belong to whichever path is chosen.
- Vaccine tracking: dated series with boosters, a different shape from a recurrence.
- Long-interval medicines (quarterly, yearly): covered by an every-N-months pattern.
- Menstrual-cycle tracking, daily health notes, and a mood journal with triggers. These
  reach into special-category health data beyond medication; the owner's own reading is
  that they may become a separate product integrating with this one. Decide module versus
  product by whether the data must be joined with medication events.
