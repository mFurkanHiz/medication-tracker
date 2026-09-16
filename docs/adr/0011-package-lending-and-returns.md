# ADR 0011: Package lending separates ownership from current allocation

## Status

Accepted — 2026-09-16

## Context

V1 requires medication lending and return between household people with preserved
ownership, allocation, and audit history. A package previously had one person
assignment, so temporarily assigning it to a borrower lost the original owner.

## Decision

Treat lending as a whole-package transition. `OwnerPersonId` is the persistent
owner; `PersonId` is the current holder and remains the eligibility source for
administration consumption. Existing assignments are backfilled as owners.

An active `InventoryLoan` stores owner, borrower, lending actor/time, and eventual
return actor/time. Loan and return each append an actor-attributed package assignment
event and a household-scoped idempotency receipt under the inventory transaction
lock. A partial unique index permits only one active loan per package. Direct owner
reassignment and medication deletion are rejected while a package is on loan.

Lending and returning do not append a quantity ledger entry: the tablets remain in
the same physical package with the same exact balance. The loan and assignment
records are the immutable allocation audit, while any use during the loan still
appends normal administration and inventory ledger entries. An empty package cannot
be lent.

## Consequences

- Owner and current holder are independently visible in the workspace and web UI.
- Returns restore allocation to the owner without restoring consumed stock.
- Household authorization and idempotent replay cover both transitions.
- Individual-tablet transfers would need a separate explicit stock-transfer
  workflow; this decision does not silently convert package loans into one.
