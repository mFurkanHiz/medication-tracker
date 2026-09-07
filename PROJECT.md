# Medication Tracker

Medication Tracker is a public portfolio project for household medication inventory, treatment schedules, adherence events, refill forecasting, and reliable offline reminders.

## Locations

- Local repository: `C:\Users\mfspe\source\repos\medication-tracker`
- GitHub repository: `https://github.com/mFurkanHiz/medication-tracker`
- Notion research: `https://app.notion.com/p/3d4afec61a3f81928cace707a08da80e`
- Notion project, sprint, and task URLs: add after creation

## Current decisions

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

## Immediate milestone

Deliver a tested vertical slice:

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
