# ADR 0008 — Independent medication catalog, package stock and flexible use

Status: Accepted — 2026-09-13

## Context

The first vertical slice attached every medication directly to a person and kept
only one aggregate stock balance. It could not distinguish a full 20-tablet box
from an opened 8-of-20 box. Its daily regimen also required a start date and exact
time, so it could not faithfully record as-needed instructions or named periods.

## Decision

Medication is a household catalog record. Its former person foreign key becomes
nullable for backwards compatibility; new person relationships are expressed by
package assignment and person-specific regimens. Category, normalized tags and
active state are catalog metadata. Metadata changes append an actor-attributed
audit event.

An `InventoryPackage` records exact capacity and optional person assignment. Its
remaining amount is projected from immutable ledger entries linked to that package.
Moving existing loose stock into a package appends equal negative and positive
allocation entries, preserving the total. Assignment and removal append assignment
events. Administrations may create multiple ledger entries under one administration
when consumption crosses package boundaries. Packages assigned to another person
are not consumed implicitly.

`RegimenVersion` supports `scheduled` and `as_needed`. Start/end and exact time are
nullable. A scheduled version requires either an exact local time or a named period;
as-needed versions require neither. Named periods, meal relation and minimum interval
are stored as user-entered instructions, not medical advice. The server uses local
midnight only as an internal occurrence key for named periods and never displays it
as a reminder time.

## Consequences

- Existing aggregate-stock records remain valid as loose stock and can be allocated
  into packages without changing their balance.
- Package capacity and remaining amounts use exact rational quantities.
- Forecasts exclude as-needed plans because their future consumption is unknown.
- Reminder delivery and user-configurable named-period windows remain separate work;
  V1 records and displays the instruction without inventing a universal hour.
- Account identity, household membership and future subscription/entitlement remain
  separate authorization concepts.

