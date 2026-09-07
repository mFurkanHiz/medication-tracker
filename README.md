# Medication Tracker

An offline-first household medication inventory and adherence platform for web and mobile.

The project tracks the difference between a treatment plan, physical household stock, actual administrations, and official refill eligibility. Its core use cases include fractional tablet doses, effective-dated regimen changes, inventory counts, low-stock forecasting, and auditable lending or returning medication between people.

## Repository layout

```text
apps/api       .NET 10 ASP.NET Core API
apps/web       Next.js web application
apps/mobile    Expo React Native mobile application
docs           architecture, domain model, ADRs, and roadmap
tests          automated .NET tests
```

## Prerequisites

- .NET SDK 10
- Node.js 22 or later
- pnpm 10
- Android SDK for device builds
- Docker for the complete local infrastructure (added in the infrastructure milestone)

## Validate the workspace

```bash
dotnet test MedicationTracker.slnx
pnpm install
pnpm lint
pnpm typecheck:mobile
pnpm build:web
```

## Product safety

Medication Tracker records user-entered treatment instructions. It does not diagnose, prescribe, or recommend dose changes. Only synthetic demonstration data belongs in this public repository.

See [PROJECT.md](PROJECT.md), [architecture](docs/architecture.md), [domain model](docs/domain-model.md), and [roadmap](docs/roadmap.md).
