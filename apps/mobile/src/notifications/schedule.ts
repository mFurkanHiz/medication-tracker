/**
 * The due-day rule, as the server has it, in a module the phone can test on a laptop.
 *
 * Nothing here imports Expo or React Native, so the code the reminder scheduler runs on
 * the device is the code Vitest runs in CI. The server's `RecurrenceRule` and the
 * governing-version rule in `ScheduledSlots` are the reference, and `schedule.test.ts`
 * mirrors `PlanEndTests` and `MonthlyRecurrenceTests` case by case: a day the web shows a
 * dose and a day the phone reminds must be the same day.
 *
 * All arithmetic is calendar arithmetic on year/month/day parts. Adding 24-hour spans
 * would drift by an hour across a daylight-saving transition and eventually move a plan
 * onto the wrong local day, so elapsed time is never used to advance a recurrence.
 *
 * The phone holds every version of a plan (the workspace lists them under the plan, the
 * latest also standing as the plan row), and `governedVersions` applies the server's
 * governing-version rule from `ScheduledSlots.Governing`: the highest-numbered version
 * that had started by a day governs it, while it has not ended. Each version is thereby
 * bounded by the next one's start, so an edit dated in the future leaves the previous
 * version reminding until the new one begins, and never both at once.
 */

export type RecurrencePattern =
  | 'Daily'
  | 'SelectedWeekdays'
  | 'EveryNDays'
  | 'DayOfMonth'
  | 'EveryNMonths';

export type DayParts = { year: number; month: number; day: number };

/** What decides whether a plan version asks for a dose on a given day. */
export type DueRule = {
  kind: 'Scheduled' | 'AsNeeded';
  pattern: RecurrencePattern;
  weekdayMask: number | null;
  intervalDays: number | null;
  dayOfMonth: number | null;
  intervalMonths: number | null;
  /** `YYYY-MM-DD`; a longer ISO value is tolerated and truncated to its date. */
  effectiveFrom: string | null;
  effectiveTo: string | null;
  /** Set aside by the household. A paused version promises no doses, so it asks for none. */
  isPaused: boolean;
};

/**
 * The server's governing-version rule, applied ahead of time to a plan's versions.
 *
 * `ScheduledSlots.Governing` gives a day to the highest-numbered version that had started
 * by then (a version with no start date has always started), and to nothing when that
 * version has ended. Expressed per version, that is a window: from its own start to the
 * day before the next version starts, or to its own end if that comes first. A version a
 * later one supersedes from the beginning — the later one has no start date, or starts no
 * later than this one — governs no day and is dropped. The last version keeps its own
 * dates, so it alone can be open-ended and get a repeating trigger.
 */
export function governedVersions<T extends DueRule & { versionNumber: number }>(versions: readonly T[]): T[] {
  const ordered = [...versions].sort((left, right) => left.versionNumber - right.versionNumber);
  const governed: T[] = [];

  for (let index = 0; index < ordered.length; index += 1) {
    const version = ordered[index];
    const later = ordered.slice(index + 1);

    if (later.length === 0) {
      governed.push(version);
      continue;
    }

    // A later version with no start date has always started, so this one never governs.
    if (later.some((candidate) => candidate.effectiveFrom === null)) {
      continue;
    }

    const nextStart = later
      .map((candidate) => parseDay(candidate.effectiveFrom!))
      .reduce((earliest, day) => (compareDays(day, earliest) < 0 ? day : earliest));
    const lastGovernedDay = addDays(nextStart, -1);

    if (version.effectiveFrom !== null && compareDays(parseDay(version.effectiveFrom), lastGovernedDay) > 0) {
      continue;
    }

    const ownEnd = version.effectiveTo === null ? null : parseDay(version.effectiveTo);
    const end = ownEnd !== null && compareDays(ownEnd, lastGovernedDay) < 0 ? ownEnd : lastGovernedDay;

    governed.push({ ...version, effectiveTo: formatDay(end) });
  }

  return governed;
}

/** How many occurrences to hold as exact-instant notifications, topped up on later reconciles. */
export const REMINDER_HORIZON = 14;

/**
 * How far ahead to look for those occurrences, whatever the pattern. A plan every twelve
 * months gets its next anniversary and nothing beyond; the reconcile after it fires
 * schedules the one after.
 */
export const MAX_SCAN_DAYS = 400;

export function parseDay(value: string): DayParts {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);
  return { year, month, day };
}

export function formatDay(parts: DayParts): string {
  return `${parts.year}-${`${parts.month}`.padStart(2, '0')}-${`${parts.day}`.padStart(2, '0')}`;
}

/** Days since the epoch, so two dates compare and subtract as integers. */
export function dayNumber(parts: DayParts): number {
  return Math.floor(Date.UTC(parts.year, parts.month - 1, parts.day) / 86_400_000);
}

export function compareDays(left: DayParts, right: DayParts): number {
  return dayNumber(left) - dayNumber(right);
}

export function addDays(parts: DayParts, days: number): DayParts {
  const shifted = new Date(Date.UTC(parts.year, parts.month - 1, parts.day + days));
  return { year: shifted.getUTCFullYear(), month: shifted.getUTCMonth() + 1, day: shifted.getUTCDate() };
}

export function daysInMonth(year: number, month: number): number {
  // Day 0 of the next month is the last day of this one.
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/**
 * The day a monthly plan lands on in a given month: the day it was written for, or the
 * month's last day when the month is shorter. A plan written for the 31st is not silently
 * skipped in February — the same clamp the server applies.
 */
export function clampToMonth(year: number, month: number, day: number): number {
  return Math.min(day, daysInMonth(year, month));
}

export function monthsBetween(start: DayParts, day: DayParts): number {
  return (day.year - start.year) * 12 + day.month - start.month;
}

/** Monday is 0 and Sunday is 6, the server's weekday-mask convention. */
export function weekdayIndex(parts: DayParts): number {
  return (new Date(Date.UTC(parts.year, parts.month - 1, parts.day)).getUTCDay() + 6) % 7;
}

/**
 * Whether this version should hold reminders at all. A paused version and an as-needed
 * plan both promise no particular dose, so neither is reminded.
 */
export function placesReminders(rule: DueRule): boolean {
  return rule.kind === 'Scheduled' && !rule.isPaused;
}

/**
 * Whether the version is due on `day`, in the plan's own local calendar.
 *
 * Mirrors `RecurrenceRule.IsDue`: the version's own dates bound it on both ends, both
 * inclusive; an as-needed plan is "available" every day it is in force; each scheduled
 * pattern decides its own days.
 */
export function isDueOn(rule: DueRule, day: DayParts): boolean {
  if (rule.effectiveFrom !== null && compareDays(day, parseDay(rule.effectiveFrom)) < 0) {
    return false;
  }

  if (rule.effectiveTo !== null && compareDays(day, parseDay(rule.effectiveTo)) > 0) {
    return false;
  }

  if (rule.kind === 'AsNeeded') {
    return true;
  }

  switch (rule.pattern) {
    case 'Daily':
      return true;

    case 'SelectedWeekdays':
      return rule.weekdayMask !== null && (rule.weekdayMask & (1 << weekdayIndex(day))) !== 0;

    case 'EveryNDays': {
      if (rule.effectiveFrom === null || rule.intervalDays === null || rule.intervalDays <= 0) {
        return false;
      }
      const elapsed = dayNumber(day) - dayNumber(parseDay(rule.effectiveFrom));
      return elapsed >= 0 && elapsed % rule.intervalDays === 0;
    }

    case 'DayOfMonth':
      return rule.dayOfMonth !== null && day.day === clampToMonth(day.year, day.month, rule.dayOfMonth);

    case 'EveryNMonths': {
      if (rule.effectiveFrom === null || rule.intervalMonths === null || rule.intervalMonths <= 0) {
        return false;
      }
      const start = parseDay(rule.effectiveFrom);
      const elapsed = monthsBetween(start, day);
      return elapsed >= 0
        && elapsed % rule.intervalMonths === 0
        && day.day === clampToMonth(day.year, day.month, start.day);
    }

    default:
      return false;
  }
}

/**
 * Due days from `from` inclusive, looking at most `maximumDays` calendar days ahead and
 * stopping at the version's end. Nothing for an as-needed plan, whose future use is
 * unknown and must not be forecast. Mirrors `RecurrenceRule.DueDays`.
 */
export function dueDaysFrom(rule: DueRule, from: DayParts, maximumDays: number): DayParts[] {
  const days: DayParts[] = [];

  if (rule.kind === 'AsNeeded') {
    return days;
  }

  const until = rule.effectiveTo === null ? null : parseDay(rule.effectiveTo);

  for (let offset = 0; offset < maximumDays; offset++) {
    const day = addDays(from, offset);

    if (until !== null && compareDays(day, until) > 0) {
      break;
    }

    if (isDueOn(rule, day)) {
      days.push(day);
    }
  }

  return days;
}

/** How many days ahead the pattern needs scanning to find {@link REMINDER_HORIZON} occurrences. */
export function scanDaysFor(rule: DueRule): number {
  switch (rule.pattern) {
    case 'Daily':
      return REMINDER_HORIZON;
    case 'SelectedWeekdays':
      return REMINDER_HORIZON * 7;
    case 'EveryNDays':
      return REMINDER_HORIZON * Math.max(rule.intervalDays ?? 1, 1);
    case 'DayOfMonth':
      return REMINDER_HORIZON * 31;
    case 'EveryNMonths':
      return REMINDER_HORIZON * 31 * Math.max(rule.intervalMonths ?? 1, 1);
    default:
      return REMINDER_HORIZON;
  }
}

/**
 * The next due days a version asks for, from `today` on: at most {@link REMINDER_HORIZON}
 * of them, within {@link MAX_SCAN_DAYS}, and none for a version that places no reminders.
 */
export function upcomingDueDays(rule: DueRule, today: DayParts, horizon = REMINDER_HORIZON): DayParts[] {
  if (!placesReminders(rule)) {
    return [];
  }

  return dueDaysFrom(rule, today, Math.min(scanDaysFor(rule), MAX_SCAN_DAYS)).slice(0, horizon);
}

/**
 * Whether a repeating OS trigger says exactly what the version says.
 *
 * Only for a daily or weekly version that has already started and has no end: a
 * repeating trigger never stops, so a version with an end date would keep reminding after
 * the household ended the plan on the web, and one starting next week would remind this
 * week. Those are scheduled as exact instants instead, which do stop.
 */
export function canRepeatOnDevice(rule: DueRule, today: DayParts): boolean {
  if (!placesReminders(rule)) {
    return false;
  }

  if (rule.pattern !== 'Daily' && rule.pattern !== 'SelectedWeekdays') {
    return false;
  }

  if (rule.effectiveTo !== null) {
    return false;
  }

  return rule.effectiveFrom === null || compareDays(parseDay(rule.effectiveFrom), today) <= 0;
}

// --------------------------------------------------------------- wall clocks ----

export function parseLocalTime(value: string): [number, number] {
  const [hour, minute] = value.split(':');
  return [Number(hour), Number(minute)];
}

/** The domain's Monday-first mask, as Expo's Sunday-first 1–7 weekday numbers. */
export function expoWeekdays(mask: number): number[] {
  const days: number[] = [];
  for (let bit = 0; bit < 7; bit++) {
    if ((mask & (1 << bit)) !== 0) {
      // bit 0 = Monday in the domain; Expo wants 1 = Sunday.
      days.push(((bit + 1) % 7) + 1);
    }
  }

  return days;
}

export type ZonedParts = DayParts & { hour: number; minute: number; second: number };

export function zonedParts(at: Date, zone: string): ZonedParts {
  try {
    const formatter = new Intl.DateTimeFormat('en-US', {
      timeZone: zone,
      hour12: false,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });

    const parts: Record<string, string> = {};
    for (const part of formatter.formatToParts(at)) {
      parts[part.type] = part.value;
    }

    return {
      year: Number(parts.year),
      month: Number(parts.month),
      day: Number(parts.day),
      // Some engines render midnight as hour 24.
      hour: Number(parts.hour) % 24,
      minute: Number(parts.minute),
      second: Number(parts.second),
    };
  } catch {
    // A runtime without full Intl time-zone data falls back to the device's own clock.
    // The reminder is then anchored to device-local time, which is right whenever the
    // phone is in the plan's zone and visibly wrong when it is not.
    return {
      year: at.getFullYear(),
      month: at.getMonth() + 1,
      day: at.getDate(),
      hour: at.getHours(),
      minute: at.getMinutes(),
      second: at.getSeconds(),
    };
  }
}

/** The calendar day it is in `zone` at the instant `at`. */
export function startOfDayParts(at: Date, zone: string): DayParts {
  const parts = zonedParts(at, zone);
  return { year: parts.year, month: parts.month, day: parts.day };
}

export function zoneOffsetMs(at: Date, zone: string): number {
  const parts = zonedParts(at, zone);
  const asUtc = Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute, parts.second);
  return asUtc - at.getTime();
}

/**
 * The instant at which a wall-clock time occurs in a named zone.
 *
 * Two passes: guess the instant as if the wall clock were UTC, read the zone's real
 * offset at that guess, then correct. A second pass settles the case where the
 * correction itself crosses a daylight-saving boundary. A wall-clock time that does not
 * exist on a spring-forward day lands an hour later, and one that exists twice on a
 * fall-back day lands on the standard-time occurrence — both as the server resolves them.
 */
export function instantForWallClock(day: DayParts, hour: number, minute: number, zone: string): Date {
  const naive = Date.UTC(day.year, day.month - 1, day.day, hour, minute);
  let instant = naive;

  for (let pass = 0; pass < 2; pass++) {
    instant = naive - zoneOffsetMs(new Date(instant), zone);
  }

  return new Date(instant);
}

export function resolveDeviceZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}
