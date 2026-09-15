# Domain model

## Treatment and time

A `Regimen` identifies an ongoing treatment instruction. Its `RegimenVersion` records effective start/end dates, dose quantity, route, meal relation, timing rules, and instructions. A new version closes the previous effective period instead of changing history.

Timing supports:

- Exact local time, such as `08:00` and `20:00`
- Named day periods: morning, noon, evening, night
- Exact intervals, weekdays, monthly patterns, cycles, and as-needed use
- Meal relation: fasting, with food, after food, before food, or irrelevant
- Optional acceptable time window and minimum interval between administrations

Named day periods are household/user preferences mapped to local time windows. They are not stored as hard-coded universal hours. Until preference windows and reminders are implemented, V1 stores the named period and uses local midnight only as an internal occurrence key; the UI never presents that key as the instruction time.

## Exact quantities

Medication quantities use a value plus a unit. Fractional tablets use exact rational values so `1/2 + 1/4` remains exactly `3/4`. General forms use domain units such as tablet, capsule, millilitre, drop, actuation, gram, application, or minute. Conversion is allowed only when an explicit conversion definition exists.

## Inventory ledger

Current stock is a projection of immutable ledger entries:

- acquire or refill
- administration consumption
- loss, disposal, found stock, or manual adjustment
- lend, borrow, return, or ownership transfer
- count reconciliation
- correction/reversal

A household `Medication` is independent of a person. An `InventoryItem` aggregates
its stock, while each optional `InventoryPackage` records exact full capacity and
an optional person assignment. Package balance is projected from package-linked
ledger entries, so a sealed 20-of-20 box and opened 8-of-20 box remain distinct while
the medication total is exactly 28. Loose stock remains valid when box details are
unknown. Allocation from loose stock to a package creates balanced ledger entries;
it does not rewrite history. Package assignment changes create actor-attributed
events. Ownership/allocation is separate from physical location.

## Inventory count and bulk update

An `InventoryCountSession` records scope, counter, start/completion time, notes, and lines. Each line stores the calculated quantity before counting, observed quantity, difference, and unit.

Accepting a count creates one reconciliation ledger entry per difference. Bulk counting is therefore auditable and does not erase history. Editing an accepted count creates a new revision that reverses the previous reconciliation and applies the replacement difference. Draft count lines can be edited freely before acceptance.

## Administrations and forecasting

A `DoseOccurrence` is an expected dose. An `AdministrationEvent` records what actually happened: taken, skipped, late, partial, extra, unknown, or corrected. Forecasting derives expected consumption from active regimen versions and adjusts projected stock with actual administration and inventory ledger events.

The tablet slice persists exact quantities as normalized integer numerator and denominator pairs. Recording an administration appends an `AdministrationEvent` and one or more linked negative `InventoryLedgerEntry` rows; multiple rows allow an exact dose to span package boundaries without losing the single administration identity. Neither record overwrites earlier history. A `ProcessedAdministrationCommand` stores the household-scoped idempotency receipt separately from the clinical and inventory events.

The inventory mutation lock also enforces a stock floor. A taken administration is
accepted only when loose stock plus packages eligible for that person cover the
entire exact dose. Rejection appends neither an administration nor a ledger entry.
Medication and regimen deletion are soft deletes, while regimen edits append a new
`RegimenVersion`. Actor-attributed change events and the immutable domain records
form one chronological activity projection for the UI.

Refill eligibility belongs to prescription/insurance data and is not inferred from physical stock. The application compares projected depletion with eligibility to expose a potential coverage gap.
