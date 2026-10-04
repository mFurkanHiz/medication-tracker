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

The `v1.0.0` tag and the live site are a **technical production baseline**, not an
acceptance point. Merging to `main` deploys automatically, so the live commit is whatever
`main` last deployed; the deploy job prints the commit and proves the public site answers.

The package-first domain rebuild (ADR 0014) is merged and live, and the web client is
rebuilt against it: Today, medications with package detail, effective-dated plans, people,
history, inventory counting, lending, refill settings, reports, export, the household's own
caution notes and an advisory minimum gap.

**Mobile is deferred past V1 by owner approval (ADR 0015, 2026-10-04).** The Expo app
remains in the repository as working infrastructure — SQLite snapshot, durable outbox,
local reminders, a Today screen that records offline — and stays compiling and migration-
tested on every commit. It has never run on a physical device and there is no release
channel to put it on one. Acceptance rows 28 and 29 are recorded `DEFERRED`, not `DONE`:
**V1 is a web release with mobile infrastructure in place.**

Server work continues mobile-compatible, and that is enforced rather than intended:
`MobileContractTests` pins the field set `/workspace` and `/today` must carry for an
offline client, so removing or renaming one fails CI while additive change stays free.

Remaining V1 work, all of it web or operational: the web accessibility audit (row 34), log
redaction and rate limiting beyond the auth endpoints (row 37), the owner-approved
deployment preflight (row 39), a final green CI run (row 38), the owner's visual judgement
on the web surface (row 30), and the owner's acceptance run (row 40).

No sprint, tag, CI run or deployment may redefine a required V1 item as later work
without explicit owner approval. Where the owner grants one, it is recorded as `DEFERRED`
in the acceptance document with a date and an ADR — never as `DONE`, and never by
deleting or softening the row.

## Non-goals for the first release

- Diagnosis or dosage advice
- Automatic drug-interaction claims without a licensed authoritative source
- e-Nabız integration without an official supported API and authorization
- Advanced calculations for liquids, creams, inhalers, and injections
- Billing or paid plans
