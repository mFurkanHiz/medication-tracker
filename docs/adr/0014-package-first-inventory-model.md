# ADR 0014 — Package-first inventory with explicit, correctable allocations

Status: Accepted — 2026-10-02

Supersedes the inventory parts of ADR 0008.

## Context

A household does not own "48 tablets of Parol". It owns three boxes: two sealed
boxes of 20 and one opened box with 8 left. When someone takes a tablet, it comes
out of one specific box. The previous model stored a single capacity-and-balance
pair per package row and recorded consumption as ledger rows that merely shared an
`administration_event_id`, so the question "which box did this dose come from, and
was that right?" had no answer and no correction path.

Four distinct realities were collapsed into one or two entities:

1. what a medication *is* (reusable, catalog-level),
2. which physical containers exist (instance-level, each with its own identity),
3. what was planned (person-level, effective-dated),
4. what actually happened and which container paid for it (event-level, auditable).

## Decision

### Four separated concepts

**`MedicationDefinition`** — household catalog. Reusable; never tied to a person.
Carries name, brand, active ingredients, strength text, pharmaceutical form, base
unit, an optional default package capacity, category, tags, notes, an extensible
`external_codes` document for future barcode/GTIN/ATC identifiers, and archive
state. Adding stock selects an existing definition or creates one inline.

**`MedicationPackage`** — exactly one physical container, with its own identifier.
Adding "2 full boxes" creates **two** package rows, not one row with a doubled
quantity. Each package carries:

- `NominalCapacity` and `BaseUnit` as **snapshots taken at creation**. Changing the
  definition's default package size from 20 to 30 later must not turn an existing
  20-tablet box into a 30-tablet box. This is the invariant that forced the
  snapshot.
- `Ordinal`, a stable per-definition integer used to render friendly labels
  ("Kutu 3" / "Box 3") so the interface never shows a UUID.
- Lifecycle `State`: `Sealed`, `Opened`, `Disposed`, `Lost`, `Archived`.
  **`Empty` is deliberately not a state** — it is derived from a zero ledger
  balance, so there is one source of truth for emptiness.
- Optional `ExpiresOn`, `LotNumber`, `Barcode`, `AcquiredOn`, `Source`, `Note`,
  `StorageLocation`.
- `OwnerPersonId` and `HolderPersonId` as **separate** fields, so lending moves
  custody without transferring ownership.
- `IsPinned`, the user's explicitly chosen active box.

**`InventoryLedgerEntry`** — append-only signed deltas. A package's balance is the
sum of its entries; a definition's total is the sum of all its package balances
plus loose (package-independent) balance. No mutable running total exists anywhere.
Entries carry a typed `EntryType`, an optional `PackageId` (null means loose), an
optional `AdministrationEventId`, a `CorrelationId` grouping entries written as one
logical act, an optional `ReversesEntryId`, an actor and both occurrence and
record timestamps.

**`AdministrationAllocation`** — the first-class answer to "which box paid for this
dose". One row per (administration, source) pair with a positive quantity and a
link to its ledger entry. A single dose may span several packages when one box
cannot cover it.

### The consumption policy is a unit-testable domain service

`PackageConsumptionPolicy` is a pure function from candidate packages and a
required quantity to an ordered allocation plan. Its deterministic order is:

1. the package the user pinned, if it has a positive balance;
2. already-opened, non-empty packages before sealed ones;
3. earliest expiry first (packages with no expiry sort after those with one);
4. earliest acquisition, then earliest creation, then `Ordinal`, as stable
   tie-breakers;
5. a sealed package is opened only once no opened package can contribute;
6. a dose splits across packages when the first cannot cover it, and the split
   amounts sum **exactly** to the dose.

Packages held by another person, and packages in `Disposed`, `Lost` or `Archived`
state, are never selected implicitly. The policy never returns a plan that would
drive any package negative, and never returns a partial plan: either the whole
quantity is covered or the caller is told it is not.

This replaces an endpoint-private method ordered by smallest remaining balance.

### Corrections are new entries, never updates

When the user says "actually I used Box 2", the system does **not** update the
historical ledger row. It appends, under one `CorrelationId`:

- a reversal entry crediting the wrongly-charged package,
- a consumption entry debiting the correct package,
- a correction record naming from-package, to-package, quantity, actor, reason and
  record time,

and marks the superseded allocation inactive while inserting the replacement. The
medication's net total is unchanged by construction, and the activity history shows
"stock source corrected from Box 1 to Box 2".

### Untracked / external source

An administration may declare `StockSource = UntrackedExternal`. The event is
recorded with its real quantity and time, no allocations are created, no ledger
entry is written, and no package goes negative. The medication is flagged as
needing reconciliation. This is an advanced, explicitly-chosen path: the default
path still refuses to create negative stock.

### Default experience

The default dose-recording call carries no package identifier. The policy picks the
source. Manual selection — a specific package, loose stock, or untracked/external —
is an optional field on the same command, surfaced in the interface only under
details/advanced.

## Invariants to be enforced by tests

1. Each physical package has a unique identifier.
2. Adding N full boxes creates N packages.
3. No package balance may go negative.
4. A package's nominal capacity never changes when its definition is edited.
5. Definition total = sum of package balances + loose balance.
6. Sum of a tracked administration's active allocations = its actual quantity.
7. An allocation correction leaves the definition total unchanged.
8. A replayed idempotency key never consumes stock twice.
9. One administration may draw from several packages.
10. Archiving a definition does not alter historical packages or administrations.
11. Household authorisation is enforced on every package and inventory operation.
12. Two concurrent consumptions cannot spend the same stock.
13. Fractional quantities stay exact; no binary floating point.
14. A duplicated offline sync does not double-consume.

## Consequences

- `inventory_items` becomes a legacy indirection. It is 1:1 with a medication by
  unique index, so the rebuilt model addresses stock by `MedicationDefinitionId`
  directly. The table and its rows are retained so existing ledger foreign keys and
  history remain valid.
- Emptiness, "is this box open", and "how much is left" are all derived from the
  ledger, so they cannot drift from it.
- Package-level reconciliation becomes possible for advanced users while the
  default count flow stays at medication level.
- The model keeps a clean path to future FHIR R5 mapping without adopting it:
  `MedicationDefinition` ≈ `Medication`, `TreatmentPlanVersion` ≈
  `MedicationRequest`, `AdministrationEvent` ≈ `MedicationAdministration`. Physical
  package instances have no direct R5 equivalent and are intentionally ours. No
  dependency is taken on unpublished R6 drafts.
- This is organisation and audit, not clinical software. The policy decides which
  box to open. It never decides what or whether to take.
