# Domain model

The authority on *why* this shape was chosen is ADR 0013 (rebuild decision) and
ADR 0014 (package-first inventory). This document describes the model as it stands.

## The four realities the model keeps separate

A household does not own "48 tablets of Parol". It owns a *kind of medication*, some
*physical boxes*, a *plan* for who takes what, and a *history* of what actually
happened and which box paid for it. Collapsing any two of those is what made the
previous model unable to answer "which box did this dose come from, and was that
right?".

```
MedicationDefinition ──┬── MedicationPackage ──┐
   (catalog)           │      (one box)        │
                       │                       ├── InventoryLedgerEntry
                       │                       │      (append-only truth)
                       └── TreatmentPlan       │
                             └── TreatmentPlanVersion
                                   └── AdministrationEvent
                                         └── AdministrationAllocation ──┘
                                               └── AllocationCorrection
```

## Catalog — `catalog.medication_definitions`

What a medication *is*: `Parol 500 mg Tablet`. Reusable, household-scoped, never tied
to a person and never holding stock.

Carries name, brand, manufacturer, strength text, pharmaceutical form, counting unit,
active ingredients, an optional default package capacity, category, tags, notes, an
extensible `external_codes` JSON document reserved for future barcode/GTIN/ATC
identifiers, and archive state.

Every field is a label the user typed. None of it is interpreted clinically and none of
it drives dose arithmetic or unit conversion.

`legacy_person_id` holds the person link the superseded model carried. It is retained
so the information is not destroyed; nothing reads it.

## Inventory — `inventory.packages` and `inventory.ledger_entries`

A **package is exactly one physical container**. Adding "2 full boxes of 20" creates
two rows with two identifiers, not one row of forty.

| Field | Why it exists |
| --- | --- |
| `nominal_capacity_*`, `unit` | **Snapshots.** Changing the catalog default from 20 to 30 must not turn an existing 20-tablet box into a 30-tablet box. |
| `ordinal` | A stable per-medication number so the interface can say "Box 3" and never show a UUID. |
| `state` | `Sealed`, `Opened`, `Disposed`, `Lost`, `Archived`. There is deliberately **no `Empty`** — emptiness is derived from a zero ledger balance, so it cannot drift or go stale after a correction puts stock back. |
| `opened_at` | When the seal was broken. Set once; a replayed command does not move it. |
| `expires_on`, `lot_number`, `barcode`, `acquired_on`, `source`, `storage_location`, `note` | Real attributes of a real box. All optional, all absent from the default form. |
| `owner_person_id` vs `holder_person_id` | **Separate.** Lending moves custody and leaves ownership alone, which is what makes a loan auditable. |
| `is_pinned` | The box the user chose to use next. At most one per medication, enforced by a filtered unique index. |

A package stores **no balance**. Its remaining amount is the sum of its ledger entries.

The **ledger is the only source of truth for quantity**. Entries are append-only signed
deltas, typed by `entry_type`: `Acquire`, `Consume`, `CorrectionReversal`,
`CorrectionConsume`, `Found`, `Loss`, `Dispose`, `CountAdjustment`, `PackageTransfer`,
`ManualAdjustment`. A null `package_id` means package-independent (loose) stock.

`correlation_id` groups entries written as one logical act — the several packages one
dose spanned, or the reversal and re-charge of one correction. `reverses_entry_id`
points at the entry a reversal undoes.

A zero delta is legal **only** for `CountAdjustment`, because "we counted this and it
matched" is a real event worth recording. Every other type must actually move stock.

## Treatments — `treatments.plans` and `treatments.plan_versions`

A plan points at a **definition, never a box**, so it survives every package being
replaced. Which box a dose comes from is decided at the moment of use.

Versions are immutable and effective-dated. Editing a plan appends a version; every
administration stays linked to the version in force when it happened, so changing
today's dose cannot retroactively make last month's adherence look wrong. A new
version may not start before the one it replaces.

Recurrence supports `Daily`, `SelectedWeekdays` (Monday-first seven-bit mask) and
`EveryNDays` (anchored on the effective start), plus `AsNeeded`. All arithmetic is
`DateOnly` arithmetic: adding 24-hour spans would drift across a daylight-saving
transition and eventually move a plan onto the wrong local day.

Exact local time, named day period, meal relation and minimum interval are recorded as
the user entered them. A named period has no invented hour; local midnight is used only
as an internal occurrence key and is never shown as a reminder time.

## Administrations — events, allocations and corrections

An `AdministrationEvent` records what a person actually did. `plan_version_id` is
**optional**, because an extra or unplanned dose is a real event with no scheduled slot
and refusing to record it would lose health information.

Outcomes are `Taken`, `Skipped`, `PartialDose`, `ExtraDose`. There is no `Late`
outcome: lateness is the difference between planned and actual time, so storing it
would create a second, drifting source of truth and would force the product to pick a
threshold that is really a clinical judgement.

`stock_source` is `TrackedInventory`, `UntrackedExternal` or `NotApplicable`. An
untracked dose keeps its real amount and time, writes no ledger entry, creates no
allocation and drives no package negative — so the product never has to choose between
losing a real health record and corrupting its inventory.

An `AdministrationAllocation` is the first-class answer to **which box paid for this
dose**. One dose may have several when it spans packages, and a tracked dose's active
allocations always sum to exactly the amount administered.

A correction never updates history. It appends, under one correlation: a reversal
crediting the wrongly-charged package, an equal consumption debiting the correct one,
and an `AdministrationAllocationCorrection` naming from, to, quantity, actor, reason and
time. The superseded allocation is marked inactive rather than deleted, so the previous
answer stays visible. Because the two amounts are equal and opposite, the medication's
total is unchanged by construction rather than by arithmetic that could drift.

A correction onto a source that lacks the stock is **refused**: reality disagreeing with
the ledger is a counting problem, not something to paper over with negative stock.

## Which package the system picks

`PackageConsumptionPolicy` is a pure function, so the choice is testable and reviewable
instead of emerging from an endpoint:

1. the package the user pinned, if it still holds stock;
2. already-opened packages before sealed ones, so a household finishes what it started;
3. earliest expiry first;
4. earliest acquisition, then earliest creation, then ordinal, as deterministic
   tie-breaks;
5. package-independent (loose) stock last, because the product is package-first.

A package that is disposed, lost, archived, empty, or **held by another person** is
never drawn from implicitly. A dose splits across packages when one cannot cover it,
and either the whole amount is covered or the caller is told the shortfall — never a
partial plan.

The default dose-recording call names no package at all. Manual selection — a specific
package, loose stock, or untracked/external — is an optional field on the same command.

This policy organises stock. It never decides whether a medication should be taken and
never alters a dose.

## Refill — `refill.medication_refill_policies`

Physical depletion and official refill eligibility are **separate fields**, because
having twelve tablets left says nothing about whether the pharmacy will dispense more
today. The gap between them is exactly what the product warns about.

`RefillForecast` walks forward over each plan's real due days rather than dividing by
an average daily rate: a Monday/Thursday plan does not consume a constant amount per
day. As-needed plans contribute nothing, and a medication with only as-needed plans is
reported as not forecastable rather than given a fabricated date.

## Quantities

`ExactQuantity` is a normalised rational. A half tablet is `1/2`, never `0.5`.
Arithmetic widens to `Int128` internally and throws on overflow rather than wrapping,
because a wrapped medication amount is worse than a failed request. Ordering is exact
cross-multiplication. Every quantity column in the database is an integer
numerator/denominator pair, and a test asserts that no floating-point column exists in
any quantity, capacity, dose or threshold.

## Concurrency and idempotency

All stock-mutating work for a household runs inside one transaction holding
`pg_advisory_xact_lock` keyed on the household, so two concurrent doses cannot each read
the same balance and both decide there is enough. The lock is per household, so
unrelated households never block each other.

A replayed command — the case a mobile client hits when it loses the network after the
server committed — returns the original result instead of consuming stock again,
enforced by unique indexes on `(household_id, idempotency_key)` rather than by
application convention alone.

## Identity and access

Account, household, membership, person and subscription/entitlement remain five separate
concepts. Membership is effective-dated, so revoking access does not delete its history.
Endpoints read the caller from the validated principal; identity no longer travels as a
request header the middleware rewrites.

## Relationship to FHIR

Named so a future mapping stays possible, without adopting FHIR as the internal model:
`MedicationDefinition` ≈ R5 `Medication`, `TreatmentPlanVersion` ≈ `MedicationRequest`,
`AdministrationEvent` ≈ `MedicationAdministration`. Physical package instances have no
direct R5 equivalent and are intentionally ours. No dependency is taken on unpublished
R6 drafts.

## Product boundary

This is a medication organisation and adherence product. It records what the user or
their clinician decided. It never diagnoses, never recommends or calculates a dose,
never invents a drug interaction, and never tells anyone what to take.
