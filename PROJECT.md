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

## Current decisions

- Registration requires matching password confirmation in the clients and API.
- New registrations retain the 12-character password minimum. Login verifies
  existing account password hashes without applying registration length policy,
  allowing explicitly provisioned test accounts. No credentials belong in Git.

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

## Completed milestone

The former public page was a static preview, not a completed web application.
The authenticated web increment now implements account registration/login, people,
medications, daily schedules, taken/skipped events, stock acquisitions and accepted
counts. Deployment and full-roadmap completion must be tracked independently.
See ADR 0006. The authenticated web/API deployment is live at the primary hostname
using application images from `598394b`, verified by CI `34518807194` and a live
synthetic API smoke test. Browser registration and person creation were also verified.
Mobile authenticated sync is implemented; physical-device acceptance is deferred
at the user's request. Full Sprint 2/3 acceptance remains open.

The tested Sprint 1 vertical slice delivers:

1. Create a person.
2. Add a tablet medication and physical stock.
3. Create a daily regimen.
4. Show today's dose.
5. Record it as taken while offline.
6. Decrement stock through the ledger.
7. Recalculate depletion and sync idempotently.

## Non-goals for the first release

- Diagnosis or dosage advice
- Automatic drug-interaction claims without a licensed authoritative source
- e-Nabız integration without an official supported API and authorization
- Advanced calculations for liquids, creams, inhalers, and injections
- Billing or paid plans
