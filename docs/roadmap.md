# Roadmap

## Delivery status — 2026-09-13

The authenticated core web release is live. V1 catalog/package/flexible-use changes
are implemented locally and await full CI plus production acceptance: person-free
medication entry, full/opened packages, package assignment, category/tag/status
filters, and regular/as-needed plans with optional dates/times. Mobile authenticated
sync is implemented; physical-device testing is deferred by the user. Remaining
Sprint 2/3 work is listed explicitly below.

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

- Package/opened-package support — V1 implementation in verification
- Package-to-person assignment audit — V1 implementation in verification
- Inventory count sessions and bulk reconciliation
- Count revision/correction workflow
- Low-stock thresholds and forecast notifications
- Lending, returning, and ownership allocation
- Official refill eligibility and gap alerts

## Sprint 3 — Reliable schedules

- Exact-time and named-period schedule recording — V1 implementation in verification
- Meal relation, as-needed use and minimum-interval recording — V1 implementation in verification
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
