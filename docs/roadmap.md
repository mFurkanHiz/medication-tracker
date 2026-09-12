# Roadmap

## Verified delivery status — 2026-09-13

The authenticated core web release is live: accounts, people, tablet inventory,
daily schedules, taken/skipped events, stock additions, accepted counts and ledger
history. CI and a live synthetic API workflow passed. Browser registration and
person creation passed; the browser tooling became unavailable before the remaining
person → medication → daily schedule flow, medication package details, dose recording,
exact stock decrement and reload persistence passed in the live browser. Mobile authenticated sync is implemented;
physical-device testing is deferred by the user. Sprint 2 and Sprint 3 below are
not complete. Do not label the entire product finished based on the core release.

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

- Package/opened-package support
- Inventory count sessions and bulk reconciliation
- Count revision/correction workflow
- Low-stock thresholds and forecast notifications
- Lending, returning, and ownership allocation
- Official refill eligibility and gap alerts

## Sprint 3 — Reliable schedules

- Exact-time and named-period schedules
- Meal relation and minimum interval
- Time-zone, daylight-saving, reboot, and permission recovery tests
- Turkish and English localization completion

## Later

- Multiple-household UI
- Caregiver collaboration and escalation
- Paid subscriptions and entitlements
- Liquid, drop, cream, inhaler, and injection workflows
- Barcode/OCR assisted entry
- Expiry and lot tracking
- Reports, exports, symptoms, measurements, and approved integrations
