# ADR 0009 — Audited CRUD, regimen revisions and stock floor

Status: Accepted — 2026-09-15

## Context

The first V1 production review showed that medication editing covered only catalog
metadata, usage plans could neither be fully edited nor deleted, and a taken event
could consume more eligible stock than remained. The latter appended a negative
loose-stock entry. The stock-history screen also omitted medication and regimen
changes, so it was not a complete user-visible audit trail.

## Decision

Medication and regimen reads remain part of the household workspace. Medication
updates cover every editable catalog field and append an actor-attributed
`MedicationChangeEvent`. Medication deletion is a soft delete: the row, inventory
ledger and historical administrations remain, while the medication and its active
regimens disappear from current views.

Regimen updates never mutate a historical `RegimenVersion`. They update the
regimen's current person/medication relationship, append a complete new version,
and append an actor-attributed `RegimenChangeEvent`. Regimen deletion is also a
soft delete. Historical administrations continue to reference the exact version
that was active when they were recorded.

Before recording a taken administration, the API holds the household inventory
lock and totals only eligible package stock plus loose stock using exact rational
quantities. If that balance is smaller than the dose, it returns
`insufficient_stock` and writes neither the administration nor any ledger entry.
The immutable ledger is never clamped or rewritten.

The workspace exposes a chronological activity projection assembled from durable
medication, regimen, administration, inventory-ledger and package-assignment
records. This projection powers the user-facing **İşlemler / Activity** view; it
does not introduce a second mutable source of truth.

## Consequences

- Current medication and plan records have complete create/read/update/delete UI
  flows while physical deletion remains unavailable by design.
- Existing negative balances, if any, remain visible audit facts and must be
  reconciled by an explicit stock count; new administrations cannot deepen them.
- Old clients may keep using the metadata settings endpoint, while current clients
  use the full medication update endpoint.
- Inventory and administration events remain append-only. Their correction model
  is a new revision or reconciliation event, not destructive CRUD.
- Household membership authorization remains independent from account identity and
  future subscription or entitlement state.
