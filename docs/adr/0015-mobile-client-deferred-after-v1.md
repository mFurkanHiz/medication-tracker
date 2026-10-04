# ADR 0015 — The mobile client is deferred past V1; its infrastructure is kept

Status: Accepted — 2026-10-04

Narrows the mobile half of the V1 acceptance scope defined in `docs/v1-acceptance.md`.
Owner-approved: the owner asked, on 2026-10-04, for the mobile infrastructure to remain
while mobile feature code moves to a later version, and for server work to continue
planned so that mobile stays compatible.

## Context

V1 was specified with two clients. The web client is finished and deployed; the Expo
client is a working skeleton — a SQLite snapshot, a durable outbox, local reminders and
a Today screen that records offline — that has never run on a physical Android device
and has no way to reach one.

That last part is the decisive fact, and it is not a coding problem. There is no release
channel: no store listing, no EAS build, no internal distribution. A defect fixed on the
phone therefore lands on `main` and reaches nobody. The two mobile slices delivered in
Sprint 5 are already in that position.

Continuing to build mobile features under those conditions costs real work and changes
nothing for any user, while each slice adds surface that must be kept correct, migrated
and translated. Meanwhile the remaining V1 rows that *do* reach users — a web
accessibility audit, log redaction, auth rate limiting, the owner's acceptance run — are
blocked behind nothing but the time being spent on a client nobody can install.

Two scope changes were therefore available: finish mobile and delay V1, or deliver V1 on
the web and move the mobile client to a later version. Only the owner can make that
choice, because it changes what V1 means. The owner chose the second.

## Decision

### 1. Mobile feature code stops at its current state

No further mobile screens, fields or schema versions are written for V1. The Expo app
stays at on-device schema version 2. The two Sprint 5 slices stay, because both fixed
things that told a user something untrue rather than adding surface.

### 2. The infrastructure is kept, and kept green

The Expo app, its SQLite snapshot, outbox, reminder scheduler and migration harness all
remain in the repository and in CI. `pnpm typecheck:mobile` and
`pnpm check:mobile-migration` keep running on every commit. Deferred is not abandoned: a
client that stops compiling is a client that will be rewritten rather than resumed, and
the on-device migration path in particular has to stay provably safe, because the next
mobile version will inherit whatever version-2 databases exist by then.

### 3. Server work continues mobile-compatible, and that is enforced

The server already sends everything an offline client needs: effective-dated plan
versions with `isPaused`, the household's caution notes on all three reads in one shape,
the minimum-gap fields, day period, meal relation and instructions, quantities as exact
numerator/denominator pairs, and idempotent command endpoints.

Keeping it that way is now a test, not an intention: `MobileContractTests` pins the field
set `/workspace` and `/today` must carry, asserting presence and type rather than
exhaustiveness, so additive change stays free and removal or renaming fails CI.

This matters specifically *because* mobile is deferred. While a client is being written,
a dropped field is a failing build on somebody's screen within the hour. While it is
deferred, the same drop is silent for months and the cost lands on whoever resumes the
work — as unbudgeted server work, discovered after they have already estimated the
mobile job.

### 4. The acceptance document records deferral, not completion

Acceptance rows 28 (offline mobile) and 29 (local reminders) are marked `DEFERRED` with
this ADR and the owner's approval date. They are **not** marked `DONE`. The mobile halves
of row 34 (accessibility) and row 37 (security: mobile secure storage review) are
likewise recorded as deferred, with their web halves still required for V1.

V1 is therefore a web release with mobile infrastructure in place. Anyone reading the
acceptance document later must be able to see that offline mobile was not delivered, and
nothing in this ADR or that document may be worded so as to suggest otherwise.

## Consequences

- V1's remaining required work is web and operational: the accessibility audit, log
  redaction, auth rate limiting beyond the auth endpoints, the owner-approved deployment
  preflight, a final green CI run, and the owner's acceptance.
- The mobile release-channel decision (EAS build and internal distribution, or a
  development build with physical-device acceptance only) is deferred with the client.
  It is an owner decision because it involves an account, signing keys and cost.
- A later mobile version resumes from a compiling app, a guarded migration path and a
  contract-tested server, and should need no server change to render what the web renders
  today. If it does need one, `MobileContractTests` was wrong about what the phone reads,
  and that is the place to fix it.
- The risk accepted is drift of a kind no test catches: the Expo SDK, the notification
  library and the Android platform will all move while nothing exercises them on a device.
  The longer the deferral, the more of the resumed work is upgrade rather than feature.
