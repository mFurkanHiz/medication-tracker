# Medication Tracker

Medication Tracker is a public portfolio project for household medication inventory, treatment schedules, adherence events, refill forecasting, and reliable offline reminders.

## Locations

- Local repository: `C:\Users\mfspe\source\repos\medication-tracker`
- GitHub repository: `https://github.com/mFurkanHiz/medication-tracker`
- Production web URL: `https://medicationtracker.rapidconfigs.com`
- Reserved test web URL: `https://medicationtracker.test.rapidconfigs.com`
- Fallback public preview: `https://medication-tracker.mf-speed96.chatgpt.site`
- Notion research: `https://app.notion.com/p/3d4afec61a3f81928cace707a08da80e`
- Notion project: `https://app.notion.com/p/3d4afec61a3f81fba16cd94cf9c5fdee`
- Completed Sprint 0: `https://app.notion.com/p/3d4afec61a3f81afaf46da73db2ccf48`
- Completed Sprint 1: `https://app.notion.com/p/3d5afec61a3f81dbaa3bdad4bc7c00ec`
- Completed vertical-slice task: `https://app.notion.com/p/3d4afec61a3f8139a1b4e062ca00a497`
- V1 completion task: `https://app.notion.com/p/3d9afec61a3f81ebabd2fad7483e3c7e`
- Owner V1 acceptance contract: `docs/v1-acceptance.md`
- Resumable V1 checkpoint: `docs/v1-progress.md`

## Current decisions

The domain was rebuilt on 2026-10-02 around the owner's package-first model. See
ADR 0013 for the rebuild rationale, ADR 0014 for the inventory model, and
`docs/domain-model.md` for the model as it stands. The decisions below supersede the
earlier catalog/package wording.

- A medication definition is household catalog data: what a medication *is*. It is
  never owned by a person and never holds stock. The superseded person link is kept
  as a clearly named legacy column rather than destroyed.
- A package is exactly one physical container with its own identity. Adding two full
  boxes creates two packages, not one doubled quantity.
- A package's nominal capacity and unit are snapshots taken at creation, so changing
  a catalog default can never resize a box that already exists.
- A package stores no balance. Remaining amount is derived from the append-only
  ledger, and emptiness with it, so the two cannot drift apart.
- Package ownership and custody are separate fields. Lending moves custody, changes
  no stock, and leaves an immutable loan history.
- Which physical package paid for a dose is a first-class record. One dose may draw
  from several packages, and the amounts sum exactly to what was administered.
- A wrongly-charged package is corrected by appending a matched reversal and
  re-charge under one correlation. The historical entry is never rewritten and the
  medication total is unchanged by construction. The previous answer stays visible.
- A correction onto a source that lacks the stock is refused: reality disagreeing
  with the ledger is a counting problem, not a reason to allow negative stock.
- The default dose-recording call names no package. The policy chooses. A specific
  package, loose stock, or an untracked external source are optional fields behind
  details.
- A dose really taken from stock the household does not track is recorded with its
  real amount and time, writes no ledger entry, and drives no package negative.
- Package selection is a deterministic, unit-tested domain policy: pinned, then
  opened before sealed, then earliest expiry, then oldest, with loose stock last.
  Packages held by another person or retired are never drawn from implicitly.
- Administration outcomes are taken, skipped, partial and extra. Lateness is derived
  from planned versus actual time rather than stored as a status.
- Physical depletion and official refill eligibility are separate facts. The gap
  between them is what the product warns about.
- Treatment plans point at a definition, never at a box, and editing one appends an
  effective-dated immutable version. Recurrence covers daily, selected weekdays and
  every-N-days, plus as-needed, with optional date range, exact time, named period,
  meal relation and minimum interval.
- Bulk counts reconcile atomically and an accepted correction appends a linked
  revision rather than editing history. A count that matched is still recorded.
- Insufficient stock is refused atomically: no partial administration, allocation or
  ledger writes.
- All stock-mutating work for a household is serialised by a per-household advisory
  lock, so two concurrent doses cannot spend the same stock.
- The activity view combines definition, plan, package, inventory, administration and
  allocation-correction events without introducing a second source of truth.
- Endpoints read the caller from the validated session principal. Identity does not
  travel as a request header the middleware rewrites.
- Registration requires matching password confirmation in the clients and API. New
  registrations keep the 12-character minimum; login verifies existing hashes without
  applying registration length policy. No credentials belong in Git.

- Public monorepo
- .NET 10 ASP.NET Core API
- PostgreSQL server database
- Next.js and TypeScript web app
- Expo React Native and TypeScript mobile app
- SQLite-backed offline-first mobile data
- Turkish and English from the first release
- Android-first delivery while retaining iOS compatibility
- Modular monolith and ledger-based inventory
- Initial experience defaults to one household; the domain supports multiple households
- Authentication and authorization seams are built from the start; paid membership is deferred
- EF Core migrations are explicit deployment artifacts; the API does not migrate its database on startup
- The initial hosted environment doubles as the product's test environment, so the primary
  `medicationtracker.rapidconfigs.com` hostname is sufficient for the first deployment
- Keep `medicationtracker.test.rapidconfigs.com` reserved for a separate test environment if one is introduced
- DNS is managed in Cloudflare; do not create or change records until deployment is explicitly authorized
- Sprint 1 commands use a durable mobile SQLite outbox and a household-scoped server idempotency key
- Medication quantities are normalized rational values stored as integer numerator and denominator pairs
- The web application is a Next.js client export backed by the same-origin authenticated API
- Browser sessions use secure HttpOnly cookies; mobile sessions use SecureStore
- The production web container binds only to VPS loopback port `3022`; host Nginx
  terminates origin TLS and Cloudflare proxies the public hostname

## Current milestone

**Owner-accepted V1 is not complete.** The authoritative completion criteria are in
`docs/v1-acceptance.md`; resumable execution state is in `docs/v1-progress.md`.

The `v1.0.0` tag and the live site remain a useful **technical production baseline**,
not an acceptance point. Production currently runs main
`0b463d3a3b4afb15ee5fc0873b89fb0d5f4d50a1`.

The package-first domain rebuild is on `claude/v1-domain-rebuild`, proposed in
[PR #11](https://github.com/mFurkanHiz/medication-tracker/pull/11). It delivers the
rebuilt domain, persistence, data-preserving migration and API — medication
definitions, physical packages, consumption allocations, allocation corrections,
untracked doses, manual source selection, partial and extra doses, low-stock
warnings, official refill eligibility and the refill-gap warning.

**It is not deployable yet.** The web and mobile clients still call the superseded
endpoints and are the next slice. Remaining V1 work, none of it narrowed or deferred:
web rebuild, mobile offline rebuild, reliable local reminders, reports, export,
accessibility, a reproducible synthetic demo seed, and the TR/EN sweep across the
rebuilt surfaces.

No sprint, tag, CI run or deployment may redefine a required V1 item as later work
without explicit owner approval.

## Non-goals for the first release

- Diagnosis or dosage advice
- Automatic drug-interaction claims without a licensed authoritative source
- e-Nabız integration without an official supported API and authorization
- Advanced calculations for liquids, creams, inhalers, and injections
- Billing or paid plans
