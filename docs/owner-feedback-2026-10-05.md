# Owner live-test notes, 2026-10-05 — evaluation and candidate slicing

The owner is testing the deployed site after Sprint 6 and sent a first batch of notes
before finishing. This document records the notes verbatim in substance, what the code
does today for each one, an engineering evaluation, and a candidate slicing for the
planning session. **Nothing here is scheduled or approved.** The owner asked for notes and
an evaluation, explicitly not code, and said testing continues; more notes follow.

Status vocabulary used below: *defect* is behaviour the code already promises and does
not deliver; *change* is new behaviour the owner wants; *decision* is a point the owner
has to settle before the slice can be designed.

## 1. Caution tags matched against the person's other medicines — change, needs a decision

**Owner's note.** Keep the free-prose caution notes (they are general, a note). Add a
structured list — medicines not to combine with, and active ingredients not to combine
with — as tags. When a person has that medicine and also has one of the tagged ones, the
app should catch it and warn.

**Today.** `CautionNotes.DoNotTakeWith` is free prose on the definition, shown on the
Today row and the inventory card. The code's own contract says it is "never parsed,
matched against a drug database, cross-referenced with another medication, or checked
before a dose is recorded", and a test holds that line. The definition already carries a
structured `ActiveIngredients` list.

**Evaluation.** This is the one note that touches the product boundary, so it needs the
owner's explicit decision and an ADR, not a quiet implementation. The boundary says the
product "never invents a drug interaction". Matching a tag the household typed against a
medicine name or ingredient the household also typed invents nothing: both halves are the
household's own record, and the app would be reminding them of their own note. That is
defensible **only** under these constraints, which the ADR must state:

- The match is mechanical equality on normalised text (trimmed, case- and
  diacritic-insensitive) against the person's other medicines' names and
  `ActiveIngredients`. No synonyms, no drug database, no inference.
- The warning attributes the claim to the household, never to the software: *"You noted
  that Allerset should not be taken with ibuprofen; Ayşe also has an ibuprofen plan"* —
  never *"these interact"*.
- It never blocks anything: not plan creation, not recording a dose. Same posture as the
  minimum-gap warning.
- Silence is not a safety claim. The screen that shows the warning must also say that it
  only reflects what the household entered.
- Scope is the **person**, not the cabinet: the other medicine must appear in that
  person's active plans (scheduled or as-needed, not paused, not deleted). A household
  cabinet holds other people's medicines.

Cost: one migration (two array columns or a child table on the definition), a boundary
amendment in `docs/domain-model.md`, `docs/v1-acceptance.md` and `PROJECT.md` ("never
invents an interaction; may remind the household of its own notes"), the boundary test
changed deliberately, web (definition form tag inputs, a warning on the plan form and a
badge on the Today row). The mobile contract test only pins existing fields; new fields
pass.

## 2. Box names and a location note — change

**Owner's note.** Boxes should be nameable when needed, and should carry a note about
where in the home they are ("TV unit, left drawer").

**Today.** `MedicationPackage` already has `StorageLocation`, `Note`, `Source`,
`LotNumber`, `ExpiresOn` and `AcquiredOn`, `UpdateDetails()` exists and
`PUT /packages/{id}` is wired. The web add-stock form has a location field, but it applies
the same value to every box created in that request, and **the web has no way to edit a
box after creation** — it only shows location and lot when set. There is no name field;
boxes are identified by their ordinal ("Kutu 2").

**Evaluation.** Small. Add `Label` (short, optional) to the package — one migration — and
give the web an *Edit box* dialog over the existing PUT exposing label, location, note,
expiry, lot and acquisition date. The row reads "Kutu 2 · Yatak odası şifonyer, üstten 2.
çekmece" when set, "Kutu 2" otherwise; the ordinal stays the identity fallback.

## 3. Lost / Discarded buttons are too prominent — change

**Owner's note.** Do not foreground "Kayıp olarak işaretle" and "Atıldı olarak
işaretle"; they are not everyday actions.

**Today.** Both are `danger` buttons on every box row beside the everyday actions
(active box, lend).

**Evaluation.** Web only, no API change. Move both (and probably *lend*) under a per-box
"Diğer işlemler" disclosure using the existing `Advanced` pattern, leaving the row with the
one or two actions people use daily. Both actions are reversible via reinstatement, so the
demotion loses nothing.

## 4. Making another box active fails — **defect, confirmed and reproduced**

**Owner's note.** With one box active, another cannot be made active. Sensible, but the
previous one should be released by the same action.

**Today.** The API already does exactly that: `POST /packages/{id}/pin` unpins the other
pinned box, then pins this one, in one `SaveChanges`. The owner saw *"Bir şeyler ters
gitti"* instead because the request returned **500**.

**Root cause.** Both rows change in the same unit of work. EF Core orders the two UPDATEs
by primary key, not by which one releases the slot, and the filtered unique index
`ix_packages_single_pinned` (`medication_definition_id WHERE is_pinned`) is checked per row
immediately. When the box being pinned sorts first, PostgreSQL raises `23505 duplicate
key` before the unpin runs. Reproduced locally on 2026-10-05 with a throwaway test that
swapped the pin in alternating orders across six fresh medicines: one of six failed with
`23505 ... "ix_packages_single_pinned"`. Which order fails depends on which box has the
smaller id, so for any given pair of boxes it is **deterministic** — the owner hits it
every time with Kutu 1 and Kutu 2, and would never hit it with a pair that happens to sort
the other way. The throwaway test was not kept; the fix carries its own.

**Fix shape.** Release before acquire, explicitly: save the unpin, then the pin, inside one
transaction — or include `is_pinned` in the index key so EF sees the value swap. The first
is the obvious one to read. Test: swap in both orders. No migration. The generic banner is
a second, smaller point: a 500 arrives with no code, so the client can only say "something
went wrong"; the real fix is not to 500.

By the standing rule in `docs/v1-progress.md`, a defect the owner actually hit comes
before new scope. It is held only because the owner asked for notes, not code, this round.

## 5. "Compute the official refill date from stock" — change

**Owner's note.** The official date may stay empty, but when set it should be easy to set
to the day the medicine runs out, given the plan is entered: 20 tablets at 2 a day ends in
10 days, at 1 a day in 20. A button beside the date field computes and fills it. For
as-needed use the exact date is unknown, so officially assume daily use at the plan's dose.

**Today.** `NextEligibleRefillOn` is typed by hand and documented as "never derived from
stock". The depletion forecast already walks the real due days of every scheduled plan and
exposes `projectedDepletionOn` and `daysOfStockRemaining`; the Temin dialog shows them.
As-needed plans contribute nothing to that forecast, by design.

**Evaluation.** Most of it exists. The *official* horizon differs from the depletion
forecast in exactly one rule: as-needed plans count as **daily at their dose**. So the
button is a pure domain function — the existing forecast over a plan list with as-needed
rewritten as daily — and a client action that fills the field, which the user still
confirms by saving. "Never derived" becomes "suggested on request, confirmed by the user";
the remarks on `MedicationRefillPolicy` and the Temin hint must say so. Button disabled
with a hint when the medicine has no plan or no stock. No migration for the button itself.

Decisions: the exact depletion day, or some days before it (lead time is a household
setting, not a rule the product should encode)? And whether an unofficial medicine (§6)
shows the field at all.

## 6. Official / unofficial flag per medicine — change

**Owner's note.** Each medicine is official (paid by health insurance), unofficial (bought
with own money) or unspecified (behaves as official). The official-date computation applies
to official and unspecified medicines; unofficial ones do not need it.

**Today.** No such concept. Packages carry a free-text `Source`.

**Evaluation.** A three-valued enum on the definition, stored as a string like the other
enums — one migration — read by §5 and by the Temin dialog. The owner's per-medicine model
is the right first step. The precise model is per **box** (a prescribed medicine with one
box bought privately), because the pharmacy's dispensing clock runs on what it dispensed,
not on what the household holds; design the computation to take a package filter so a
per-box override can come later without reworking §5.

## 7. An ended plan vanishes — change, with a trap to avoid

**Owner's note.** Ending a plan removed it completely. It must be recoverable; something
like past plans.

**Today.** "Planı sonlandır" is a soft delete (`DeletedAt`) with **no confirmation**, no
restore route, and the workspace hides deleted plans. Recorded doses stay in history.
Pause/resume (Sprint 2) is the reversible verb; delete is the only "end".

**Evaluation.** Two ways to recover, one of them wrong. Clearing `DeletedAt` restores the
plan *as if it had never ended*, so the adherence replay reads the ended period as missed
doses — a plan ended on the 1st and restored on the 10th would show nine days of misses.
The right shape follows the effective-dating rule already in the domain: **ending appends a
version with `EffectiveTo` = the end date**, the plan stays visible under *Geçmiş planlar*,
and *restart* appends a new version effective from today, copying the shape. History is
preserved, the gap is outside the effective period, and the replay is correct. Hard delete
stays only for mistakes, behind a confirmation, or goes away. Needs: two routes (end,
restart), the workspace returning ended plans with a flag, a past-plans section in the web,
a confirmation on end. Probably no migration: versions already carry `EffectiveTo`.

## 8. Monthly plans — change

**Owner's note.** Some medicines are taken monthly; the plan form should allow that.

**Today.** Patterns are Daily, SelectedWeekdays and EveryNDays (1–3650). "Every 30 days"
is possible and drifts off the calendar month.

**Evaluation.** A new pattern. Two candidates: *day X of each month* (`DayOfMonth`, with
the 29th–31st clamped to the month's last day), or *every N months anchored on the start
date* (`EveryNMonths`, N = 1 monthly, 3 quarterly, 12 yearly) with the same clamping. The
second covers the owner's later "long-interval medicines" in the same stroke and is the one
to prefer; the first is a special case of it. One migration (a column), the due-day rule
and its validation, the plan form, i18n. The forecast and the adherence replay walk days
through the rule and need no change. A new enum value does not break the pinned mobile
contract.

## Candidate slicing — for the planning session, not a plan

Order by the standing rules: defects first, then small independent wins, one migration per
turn (the migration count is asserted by `tests/migrations/*.sql`).

| # | Slice | Kind | Migration | Depends on |
|---|-------|------|-----------|------------|
| 1 | Active-box swap 500 (§4), test both orders | defect | no | — |
| 2 | Demote Lost/Discarded (§3) | change, web only | no | — |
| 3 | Box label + Edit box dialog (§2) | change | A | — |
| 4 | End / restart a plan with past plans (§7) | change | no (expected) | decision D3 |
| 5 | Official flag (§6) + compute-from-stock button (§5) | change | B | decisions D2, D5 |
| 6 | Every-N-months pattern (§8) | change | C | decision D4 |
| 7 | Caution tags + person-scoped warning (§1) | change + ADR | D | decision D1 |

Decisions the owner has to make before slices 4–7 can be designed:

- **D1** Approve the boundary refinement in §1 under its constraints, or keep cautions as
  prose only.
- **D2** Official flag per medicine now (recommended), per box later if needed.
- **D3** "End" as an effective-dated version with restart (recommended) versus delete with
  undelete.
- **D4** Every-N-months anchored on the start date (recommended) versus day-of-month only.
- **D5** Compute-from-stock fills the depletion day itself, or a household-set number of
  days before it; and hide the official date for unofficial medicines or not.
- **D6** Whether slice 1 may ship while testing continues. A merge to `main` redeploys
  `api` and `web` with a few seconds of interruption and no data change; it is held only
  because the owner asked for no code this round.

## Later versions — recorded, not scoped

The owner listed these for later versions and said the focus now stays medication
tracking and its reminders. Recorded in `docs/roadmap.md` under *Later*:

- Vaccine tracking (dated series with boosters — one-off dated doses, a different shape
  from a recurrence).
- Long-interval medicines (quarterly, yearly) — covered by §8's every-N-months pattern.
- Notifications: remind a set time before, remind again, snooze. **The core of the
  product and currently absent on the web**: reminders were a mobile responsibility and the
  mobile client is deferred (ADR 0015). The next version's planning has to pick a delivery
  path — resume mobile (local notifications already built, no release channel), web push
  from the Next.js app (service worker + VAPID + a server-side scheduler, installable on
  Android; iOS requires home-screen installation), or both.
- Menstrual-cycle tracking, daily health notes and a mood journal (what was felt, whether
  something triggered it). These extend into special-category health data well beyond
  medication; the owner's own reading is that they may become a separate product that
  integrates with this one. Decide module-versus-product by whether the data must be
  joined with medication events (then a module behind the same household model) or stands
  alone (then a product on the API).
