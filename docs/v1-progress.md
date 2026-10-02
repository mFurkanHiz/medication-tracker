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
- The API and the web client are rebuilt. The **mobile client is not** and still calls
  the superseded endpoints.
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

1. **Mobile offline rebuild** — durable SQLite/outbox, offline Today flow, idempotent
   sync, plus physical-device acceptance. The mobile client still calls the superseded
   endpoints.
2. **Local reminders** — reliability across reboot, permission, time-zone, DST and app
   update, with physical-device evidence.
3. **Reports** and **export**, neither of which exists.
4. **Inventory counting has no client surface.** The API and its revisioning are proven;
   nothing in the web client calls them yet.
5. **Accessibility** has no formal audit, and **TR/EN** is complete for web but not for
   mobile.
6. Package-level count reconciliation is modelled (`count_sessions.package_id`) but has
   no client surface.

## Next exact action

Rebuild `apps/mobile` against the new API, offline-first.

The core daily workflow must work with no network: see what is due, record a dose, and
sync later without double-consuming stock. The server side of that is already proven —
a replayed idempotency key returns the original event and consumes once — so the work is
the client's durable queue, not the protocol.

Shape it as:

- A SQLite snapshot of `GET /api/households/{id}/workspace` and
  `GET /api/households/{id}/today`, refreshed when online.
- A durable outbox row per command, written before the request is attempted and cleared
  only on a successful response, so a crash or a lost connection safely retries the same
  `idempotencyKey`.
- `POST /api/households/{id}/administrations` with `{ planVersionId, scheduledFor,
  idempotencyKey }` for the one-tap path.
- Session credentials in Expo SecureStore; a separate SQLite cache per household.
- Conflicts surfaced, never resolved silently as last-write-wins on a health or
  inventory record.

Then local reminders, which need physical-device evidence across reboot, notification
and exact-alarm permissions, time-zone change, DST and app update.

The web client is rebuilt and the API is green, so the branch is no longer
self-inconsistent. Production deployment still requires an owner-approved preflight, and
should not happen while the mobile client is broken against the new API unless the owner
accepts that mobile is temporarily non-functional.

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
- 2026-10-02: PR #11 CI run **`37036017899`** passed with **124 tests, 0 failed, 0
  skipped** — the first run in which every PostgreSQL integration test executed rather
  than being skipped. Both jobs green. That run covers:
  - the mandatory acceptance scenario end to end (48 total across three packages, the
    opened box chosen automatically, an explicit box for the next dose, the correction
    restoring Box C and leaving the total unchanged, and the next package opening when
    Box C empties);
  - allocation correction refused onto a source without the stock;
  - two concurrent doses contending for one tablet, exactly one succeeding, balance
    never negative;
  - a replayed idempotency key recording one dose and consuming stock once;
  - an untracked external dose writing no ledger entry and no negative stock;
  - cross-household authorisation on every rebuilt read and write;
  - a catalog capacity edit leaving existing packages at their snapshotted capacity;
  - a dose split across packages summing exactly to the amount administered;
  - retiring a package removing its stock through a typed ledger entry;
  - the refill-gap warning (depletion day 5, eligibility day 14, nine-day gap);
  - a count that matched still being recorded, and a correction creating a revision
    while the original stays untouched;
  - weekday/interval schedules including DST boundaries, and off-schedule doses refused;
  - plan edits appending a version with historical administrations unchanged;
  - web lint, mobile typecheck, web production build, both Docker builds with matching
    revision labels;
  - the packaged `migrations.sql` applied twice to a blank PostgreSQL 18 database, and
    twice to a production-shaped baseline of all eleven pre-rebuild migrations seeded
    with synthetic rows, followed by `verify-upgrade.sql` asserting row by row that
    definitions, plans, plan versions, packages, ledger entries, counts, audit history,
    the promoted allocations and the medication total all survived.
- 2026-10-02: Two defects found and fixed only because the PostgreSQL suite actually
  ran: the migration inserted historical allocations before their table existed, and
  the rebuilt harness never applied migrations so every integration test had been
  failing with a 500. A third, smaller one followed — the POST test helper could not
  accept the empty body a 204 correctly carries.
- 2026-10-02: Web client rebuilt against the new API. The 153-line single-component
  tracker is replaced by a typed API client, an exact-quantity module that never
  converts an amount to a float, complete TR/EN dictionaries typed so a missing
  translation fails the build, and screens per concern. Verified in a browser at desktop
  and 375px: sign-in renders, the TR/EN toggle switches the whole interface, touch
  targets and focus rings hold up. Three defects found by running it rather than
  assuming — an unlayered `button { color: inherit }` reset that beat Tailwind's layered
  utilities and left dark-on-dark button labels, three fetch effects that set state
  synchronously, and a locale read copied out of localStorage by an effect instead of
  through `useSyncExternalStore`. The authenticated screens are typed against the API
  client but were not exercised in a browser, because this workstation has no PostgreSQL
  to run the API against.
