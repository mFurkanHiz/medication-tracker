# Mobile resume plan

The mobile client was deferred past V1 with its infrastructure kept (ADR 0015). The owner
has set the order: finish the Sprint 7 slices, then resume mobile, then later features.
This is the hand-over for whoever picks the phone up: what exists, what the server now has
that the phone does not, and the order to close the gap. **No mobile code is written by
this plan.**

## What exists

- Expo React Native app under `apps/mobile`: `SignInScreen`, `TodayScreen`,
  `DoseDetailsSheet`; SQLite with an on-device schema (`SCHEMA_VERSION = 2`) and an
  outbox for offline recording; local reminders scheduled from plans
  (`src/notifications/reminders.ts`) for the `Daily`, `SelectedWeekdays` and `EveryNDays`
  patterns; snapshot of the workspace and the Today list (`src/data/snapshot.ts`).
- CI runs `pnpm typecheck:mobile` and `pnpm check:mobile-migration` on every commit, and
  `MobileContractTests` on the server pins the fields the phone reads from `/workspace`
  and `/today`.
- No release channel: no store listing, no EAS build, no internal distribution. Every
  mobile slice so far reached nobody.

## What the server gained in Sprint 7 that the phone does not know

Ordered by what would be wrong on the phone, not by size.

1. **The governing-version rule changed** (PR #52). A day is governed by the latest
   version that had started, provided it has not ended — no longer by the highest
   version covering the day. The phone's local due computation and reminder scheduling
   must mirror this exactly, or a plan the household ended on the web keeps reminding on
   the phone. `ScheduledSlots.Governing` and `PlanEndTests` are the reference.
2. **Two new recurrence patterns** (PR #54): `DayOfMonth` and `EveryNMonths`, with the
   month's last day as the clamp. The phone's `pattern` type, its SQLite plan table
   (`day_of_month`, `interval_months`) and `upcomingInstants` need them; Expo's date
   triggers cover monthly instants the same way `EveryNDays` is handled today.
3. **`conflicts` on Today rows** (PR #55, ADR 0016). An array, empty when nothing
   matched; the phone should render it in red with the same attribution line and never
   block. Until it does, the field is additive and the contract test is unaffected.
4. **Ended and restarted plans** (PR #52). `effectiveTo` set in the past means ended;
   a later version with a future `effectiveFrom` means restarted. The phone already
   stores both dates; the rule in (1) is what makes them mean the right thing.
5. **Fields the phone has no surface for yet**: box labels, coverage, the expected end
   date, do-not-take-with tags on the definition. Inventory and refill settings are not
   on the phone; nothing to do until they are.

## Still missing from V1's point of view

Reports, export and counting have no mobile surface (noted in ADR 0015), and the
physical-device acceptance rows 28 and 29 are `DEFERRED`, not done. They stay that way
until a release channel exists.

## The order

0. **Owner: the release channel.** An Expo account, EAS Build, and internal distribution
   for Android first. Nothing below reaches a phone without it, which is the fact that
   deferred mobile in the first place.
1. **Schema v3**: the two pattern columns and `conflicts`; the on-device migration check
   in CI proves the upgrade from v2.
2. **Due-day rule alignment** with unit tests mirroring `PlanEndTests` and
   `MonthlyRecurrenceTests`, so a day the web shows and a day the phone reminds are the
   same day.
3. **Today**: render `conflicts`; extend `MobileContractTests` to pin it once the phone
   reads it.
4. **Reminders** for the monthly patterns and for ended plans (none after `effectiveTo`).
5. **Physical-device acceptance** (rows 28, 29): reboot, permission revocation, time-zone
   change, DST, Doze — on the owner's Android phone.
6. **Version**: `mobile-v1.0.x` at parity with the server line (ADR 0017), then the
   reports, export and counting surfaces as their own slices.
