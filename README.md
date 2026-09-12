# Medication Tracker

An offline-first household medication inventory and adherence platform for web and mobile.

Live web application: [medicationtracker.rapidconfigs.com](https://medicationtracker.rapidconfigs.com)

Create an account and choose **İlaç ekle / Add medication**. If no person exists,
the app guides you through person creation first, then opens medication entry.
Enter the medication name, optional package strength/ingredient/note and opening
tablet stock (0 is allowed). Saving opens the daily schedule form for that medication.
Add a daily schedule and record taken/skipped doses. The medication screen supports
stock additions and counts; the history screen preserves ledger movements.
This is the authenticated core release, not completion of all Sprint 2/3 features.

Fallback preview: [medication-tracker.mf-speed96.chatgpt.site](https://medication-tracker.mf-speed96.chatgpt.site)

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
- Docker for the local PostgreSQL infrastructure

## Validate the workspace

```bash
dotnet test MedicationTracker.slnx
pnpm install
pnpm lint
pnpm typecheck:mobile
pnpm build:web
```

## Local PostgreSQL

Copy `.env.example` to a git-ignored `.env`, replace the development-only password,
and start PostgreSQL:

```bash
docker compose -f compose.dev.yml up -d database
dotnet tool restore
dotnet ef database update --project apps/api --startup-project apps/api
```

Supply `ConnectionStrings__Database` to run the API. Supply
`MEDICATION_TRACKER_TEST_POSTGRES` and run `dotnet test MedicationTracker.slnx` to
include the PostgreSQL migration, readiness-health, and transaction rollback test.
Without that variable, only the Docker-dependent test is reported as skipped.

## Mobile offline sync

The mobile app always commits person, medication, regimen, administration, ledger,
and outbox changes to SQLite first. To exercise upload against a local API, copy
`apps/mobile/.env.example` to `apps/mobile/.env.local` and set the API URL if needed.
Sign in through the app. Session credentials are kept in Expo SecureStore, and each
household has a separate SQLite cache. Client-supplied account UUIDs are rejected.

Outbox rows are sent in creation order whenever the app becomes active or the user
chooses **Şimdi eşitle**. A server acknowledgement is recorded locally only after a
successful response, so a crash or connection loss safely retries the same
household-scoped idempotency key.

The development database port binds to `127.0.0.1` by default. Production must use
an internal container network and must not publish PostgreSQL on a host port. See
[local database development](docs/local-database-development.md).

## Product safety

Medication Tracker records user-entered treatment instructions. It does not diagnose, prescribe, or recommend dose changes. Only synthetic demonstration data belongs in this public repository.

See [PROJECT.md](PROJECT.md), [architecture](docs/architecture.md), [domain model](docs/domain-model.md), and [roadmap](docs/roadmap.md).

## Web hosting

The web client calls the authenticated API through same-origin `/api/` requests.
The custom hostname is `medicationtracker.rapidconfigs.com`. PostgreSQL and the API
run on the isolated project container network with no published host ports.

The production web application runs on the VPS behind Cloudflare as an isolated
container bound to `127.0.0.1:3022`. See [web VPS deployment](docs/web-vps-deployment.md).

## Android physical-device development

Enable USB debugging on the phone, connect it by USB, unlock it, and accept the
computer's RSA authorization prompt. On Windows, point the shell at JDK 17 and the
installed Android SDK before building:

```powershell
$env:JAVA_HOME = 'C:\Program Files\Java\jdk-17.0.2'
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$env:Path = "$env:JAVA_HOME\bin;$env:ANDROID_HOME\platform-tools;$env:Path"
adb devices -l
pnpm --filter mobile android:device
```

The expected Android application ID is `com.rapidconfigs.medicationtracker`. Expo
generates the native `apps/mobile/android` directory for local builds; it remains
git-ignored and must not contain committed signing material. The workspace keeps
pnpm's virtual store at the short `.p` path so native CMake builds remain within
Windows path-length limits.
