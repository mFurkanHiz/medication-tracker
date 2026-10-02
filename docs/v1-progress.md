# V1 execution checkpoint

Short by design. It exists so a new session can continue the V1 programme without
reconstructing the product history from a long conversation.

## Current state

- Owner-accepted V1: **NOT COMPLETE**. See `docs/v1-acceptance.md` for the authority
  on scope and `docs/adr/0013-v1-domain-rebuild-strategy.md` for why the domain was
  rebuilt rather than extended.
- Last verified production release: main `0b463d3a3b4afb15ee5fc0873b89fb0d5f4d50a1`,
  deployed from CI run `35211378823`. **Production has not been deployed from the
  rebuild branch.**
- Active branch: `claude/v1-domain-rebuild`, proposed in PR #11, branched from PR #9's
  head so the schedule work is preserved rather than merged separately.
- PR #9 is **subsumed** by PR #11, not abandoned. Its recurrence rules are carried
  forward onto `TreatmentPlanVersion`. Do not merge PR #9 separately.
- PR #10 (packaged migration SQL fix) is already on main.

## What the rebuild has delivered

Domain, application, persistence and API layers for the package-first model:

- `MedicationDefinition` — catalog only, never person-owned, no longer tablet-only.
- `MedicationPackage` — one row per physical container, with snapshotted capacity and
  unit, a real lifecycle, expiry/lot/barcode/location, separate owner and holder, and
  emptiness derived from the ledger.
- `AdministrationAllocation` — which package paid for a dose, as a queryable fact.
- `AdministrationAllocationCorrection` — the owner-critical "I actually used Box 2"
  path, as an appended reversal plus re-charge that conserves the total.
- Untracked/external administrations, manual source selection, partial and extra doses.
- `PackageConsumptionPolicy` — the deterministic, unit-tested selection order.
- `MedicationRefillPolicy` and `RefillForecast` — low stock, official refill
  eligibility and the refill-gap warning, with depletion walked over real due days.
- Module boundaries replacing `SprintOneEntities.cs`.
- Identity read from the validated principal rather than a rewritten request header.
- A hand-assembled, data-preserving migration plus a CI gate that asserts row by row
  that the production-shaped baseline survived it.

## What is still open

These are `OPEN` or `PARTIAL` in the acceptance contract and have **not** been
narrowed or moved out of V1:

1. **Web rebuild.** The client still calls the superseded endpoints, so it is
   currently broken against the new API. This is the next slice and it blocks
   deployment.
2. **Mobile offline rebuild** — durable SQLite/outbox, offline Today flow, idempotent
   sync, plus physical-device acceptance.
3. **Local reminders** — reliability across reboot, permission, time-zone, DST and app
   update, with physical-device evidence.
4. Reports, export, accessibility, reproducible demo seed, TR/EN sweep across the
   rebuilt surfaces.
5. Package-level count reconciliation is modelled (`count_sessions.package_id`) but has
   no client surface yet.

## Next exact action

Rebuild `apps/web` against the new API. The surface it needs:

- `GET /api/households/{id}/workspace` — people, definitions with totals, package
  counts and package detail, plans, refill policies.
- `GET /api/households/{id}/today` — due doses with `planVersionId`, dose and
  `hasEnoughStock`.
- `POST /api/households/{id}/administrations` — one-tap default is
  `{ planVersionId, scheduledFor }`; `source` and `packageId` are the advanced path.
- `POST .../administrations/{id}/allocations/{allocationId}/correction` — the
  change-which-box flow.
- `POST /api/households/{id}/inventory/{definitionId}/stock` — `capacityNumerator`,
  `fullPackages`, `openedPackages[]`.
- `GET /api/households/{id}/activity` — unified history including allocation
  corrections.
- `PUT .../medication-definitions/{id}/refill-policy` and `GET .../forecast`.

Keep the default surface simple: a medication row shows `48 tablets • 3 packages`, and
package detail, ledger, allocation and correction live under details/advanced. Every
string goes through a translation key in TR and EN.

Do not deploy to production until the web client works against the new API, and then
only after an owner-approved preflight.

## Resume protocol

When the owner says **"continue" / "kaldığın yerden devam et"**:

1. Read `AGENTS.md`, `docs/v1-acceptance.md`, and this file.
2. Inspect `git status` and recent commits. Preserve existing work; never restart from
   scratch.
3. Resume the next `OPEN`/`PARTIAL` acceptance row, starting from *Next exact action*.
4. Read only the code and docs the current slice needs.
5. Run targeted tests while editing; run the full gate before a checkpoint or merge.
6. Commit coherently, update this file with evidence and the next exact action, then
   report.
7. Never redefine a required V1 item as "later" without explicit owner approval.

## Evidence checkpoint

- 2026-10-02: Takeover audit of `a1c761a`. Measured the product at roughly 1,500 lines
  of C# plus 650 of client code outside migration scaffolding; identified CI's packaged
  migration gate, the production deploy path, session identity, `ExactQuantity`,
  advisory-lock serialisation, revisioned counts and PR #9's recurrence rules as worth
  keeping, and the single ambiguous `Medication` entity, the balance-pair package, the
  absent allocation record and the absent correction path as the core defects. Recorded
  in ADR 0013 and ADR 0014; acceptance contract reconciled with the owner's redefined
  scope and rows whose evidence depended on the superseded model re-opened.
- 2026-10-02: Verified the `X-Account-Id` header is **not** an authentication bypass —
  the middleware strips the client's copy before re-deriving it from the session, and
  `AuthenticationBoundaryTests` covers it. It was fragile design, not a live hole, and
  endpoints now read the principal directly instead.
- 2026-10-02: Domain core committed (`3f4d0cc`) with the consumption policy, allocation
  correction planner and extended exact-quantity type. 72 tests passing locally.
- 2026-10-02: Rebuild committed (`a18b8e1`). 99 tests pass locally; 25 PostgreSQL tests
  skipped because this workstation has neither Docker nor a PostgreSQL server, so all
  database, concurrency and migration evidence comes from CI. Web lint, mobile
  typecheck and web production build pass.
- 2026-10-02: Found that the scaffolded migration would have **dropped five populated
  tables** and mis-assigned two `administration_events` column renames by position,
  writing plan-version identifiers into a medication column. Replaced with a
  hand-assembled create-copy-drop plus thirteen backfills; `tests/migrations/`
  now seeds a production-shaped baseline (all pre-rebuild migrations) and asserts
  preservation of every affected table and of the medication total.
- 2026-10-02: Found that the superseded count flow wrote a zero-delta ledger entry
  whenever a count matched. A zero delta is now legal for count adjustments only, and
  the rebuilt count flow still records "counted and matched" rather than discarding it.
- 2026-10-02: `deploy-production.sh` now stops this project's api and web containers
  before applying migrations, because a table rename cannot run safely underneath the
  previous release, and verifies the backup is readable before anything changes. No
  other compose project is touched.
- 2026-10-02: PR #11 opened. CI evidence to be recorded against the run on its head
  commit.
