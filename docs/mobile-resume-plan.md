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
- **Release channel, decided by the owner on 2026-10-05:** the owner's Android phone over
  USB from the owner's own computer, no EAS Build (it has a quota). The runbook is
  `docs/mobile-device-install.md`; a cloud session cannot do that step, a session on the
  owner's computer does. No store listing and no internal distribution: the app reaches
  the phones somebody plugs into a computer with this repository on it.

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

0. **The release channel** — answered on 2026-10-05: USB from the owner's computer, per
   `docs/mobile-device-install.md`. The install itself is the first thing a session on
   that computer does; until it has run, nothing below has reached a phone.
1. **Schema v3** — done, PR #58: `day_of_month`, `interval_months`, `conflicts`; the
   on-device migration check upgrades frozen v1 and v2 fixtures and asserts the defaults.
2. **Due-day rule alignment** — done, PR #58: `src/notifications/schedule.ts` mirrors
   `RecurrenceRule` and the governing rule; `schedule.test.ts` takes its cases from
   `PlanEndTests` and `MonthlyRecurrenceTests` (20 tests, `pnpm test:mobile` in CI).
3. **Today renders `conflicts`** — done, PR #58, in red with the matched words and whose
   tag it was, never blocking; `MobileContractTests` pins `conflicts`, `dayOfMonth` and
   `intervalMonths`.
4. **Reminders** — done, PR #58: a repeating OS trigger only for a started, open-ended
   daily or weekly version; an end date, a future start, every-N-days and the monthly
   patterns are bounded runs of exact instants. A plan ended on the web no longer keeps
   reminding on the phone.
5. **Physical-device acceptance** (rows 28, 29): reboot, permission revocation, time-zone
   change, DST, Doze — on the owner's Android phone, after the install.
6. **Version**: `mobile-v1.0.1` at parity with the server's `v1.0.1` (ADR 0017, decided),
   then the reports, export and counting surfaces as their own slices.

## Known gap

The workspace sends each plan's latest version only. The server's rule gives a day to
the highest-numbered version that had started by then, so an edit dated in the future
leaves the *previous* version governing until the new one starts — and the phone does not
have the previous version. Its Today list is still right, because that comes from the
server; its local reminders for those in-between days are missing. Rare (a future-dated
edit), stated in the schedule module's header, and the fix is a server change: send the
version governing each of the next days, or every version.
