# ADR 0007 — Guided medication entry and descriptive package details

Status: Accepted — 2026-09-12

## Context

The first web UI hid medication entry behind a person-dependent primary action.
It did not guide the user from saving a person to adding a medication and schedule.
The user reported being able to add only a person.

## Decision

Keep separate, visible person and medication actions. Medication entry without a
person opens person creation with an explanation; successful creation advances to
medication entry, then to a daily schedule. Forms remount between steps so the
person name cannot leak into the medication name input. A person card can start
medication entry with that person selected.

Persist optional package strength (100 chars), active ingredient (200) and notes
(2000) as descriptive strings in an additive migration. Do not derive a tablet
dose from package strength. Existing clients can omit these fields. Existing rows
remain valid. Web permits zero opening stock with an auditable ledger event.

## Acceptance and remaining boundary

Verify through the browser: create synthetic person, medication details, fractional
stock and schedule, record a dose, reload, verify persistence and stock history.
Integration tests cover metadata, zero stock, invalid lengths and household isolation.
This increment does not mark unfinished Sprint 2/3 work or physical-device acceptance
complete. Device testing remains explicitly deferred by the user.
