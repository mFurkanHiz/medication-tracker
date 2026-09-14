# Roadmap

## Delivery status — 2026-09-14

V1 is live and passed CI plus production acceptance. It includes person-free
medication entry, exact full/opened packages, package assignment,
category/tag/status filters, and regular/as-needed plans with optional dates,
times, named periods and meal relation. Mobile authenticated sync is implemented;
physical-device testing is deferred by the user. Remaining Sprint 2/3 work is
listed explicitly below and is outside V1.

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

- Package/opened-package support — delivered in V1
- Package-to-person assignment audit — delivered in V1
- Inventory count sessions and bulk reconciliation
- Count revision/correction workflow
- Low-stock thresholds and forecast notifications
- Lending, returning, and ownership allocation
- Official refill eligibility and gap alerts

## Sprint 3 — Reliable schedules

- Exact-time and named-period schedule recording — delivered in V1
- Meal relation, as-needed use and minimum-interval recording — delivered in V1
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
