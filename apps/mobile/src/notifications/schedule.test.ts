import { describe, expect, it } from 'vitest';
import {
  canRepeatOnDevice,
  dueDaysFrom,
  formatDay,
  instantForWallClock,
  isDueOn,
  parseDay,
  startOfDayParts,
  upcomingDueDays,
  type DueRule,
} from './schedule';

/**
 * Mirrors the server's PlanEndTests and MonthlyRecurrenceTests, case by case. A day the
 * web shows a dose and a day the phone reminds must be the same day; these are the days.
 */

const TODAY = parseDay('2026-10-05'); // a Monday

function rule(overrides: Partial<DueRule>): DueRule {
  return {
    kind: 'Scheduled',
    pattern: 'Daily',
    weekdayMask: null,
    intervalDays: null,
    dayOfMonth: null,
    intervalMonths: null,
    effectiveFrom: null,
    effectiveTo: null,
    isPaused: false,
    ...overrides,
  };
}

const due = (r: DueRule, day: string) => isDueOn(r, parseDay(day));
const days = (list: { year: number; month: number; day: number }[]) => list.map(formatDay);

describe('a plan on a day of the month', () => {
  const onThe31st = rule({ pattern: 'DayOfMonth', dayOfMonth: 31 });

  it('lands on the day it was written for', () => {
    expect(due(onThe31st, '2026-01-31')).toBe(true);
    expect(due(onThe31st, '2026-03-31')).toBe(true);
    expect(due(onThe31st, '2026-03-30')).toBe(false);
  });

  it('lands on the last day of a shorter month instead of being skipped', () => {
    expect(due(onThe31st, '2026-02-28')).toBe(true);
    expect(due(onThe31st, '2026-02-27')).toBe(false);
    expect(due(onThe31st, '2026-04-30')).toBe(true);
    expect(due(onThe31st, '2028-02-29')).toBe(true);
    expect(due(onThe31st, '2028-02-28')).toBe(false);
  });
});

describe('a plan every N months', () => {
  it('counts calendar months from its start day, clamped like the server', () => {
    const everyTwo = rule({ pattern: 'EveryNMonths', intervalMonths: 2, effectiveFrom: '2026-01-31' });

    expect(due(everyTwo, '2026-01-31')).toBe(true);
    expect(due(everyTwo, '2026-02-28')).toBe(false);
    expect(due(everyTwo, '2026-03-31')).toBe(true);
    expect(due(everyTwo, '2026-04-30')).toBe(false);
    expect(due(everyTwo, '2026-05-31')).toBe(true);
  });

  it('is not due before it started, and every third month after', () => {
    const everyThree = rule({ pattern: 'EveryNMonths', intervalMonths: 3, effectiveFrom: '2026-01-15' });

    expect(due(everyThree, '2025-10-15')).toBe(false);
    expect(due(everyThree, '2026-01-15')).toBe(true);
    expect(due(everyThree, '2026-04-15')).toBe(true);
    expect(due(everyThree, '2026-05-15')).toBe(false);
    expect(due(everyThree, '2026-07-15')).toBe(true);
  });

  it('schedules only the anniversaries that fall inside the scan window', () => {
    const yearly = rule({ pattern: 'EveryNMonths', intervalMonths: 12, effectiveFrom: '2026-10-05' });

    expect(days(upcomingDueDays(yearly, TODAY))).toEqual(['2026-10-05', '2027-10-05']);
  });
});

describe('the patterns the phone already knew', () => {
  it('counts every N days from the start date', () => {
    const everyThree = rule({ pattern: 'EveryNDays', intervalDays: 3, effectiveFrom: '2026-10-01' });

    expect(due(everyThree, '2026-09-28')).toBe(false);
    expect(due(everyThree, '2026-10-01')).toBe(true);
    expect(due(everyThree, '2026-10-02')).toBe(false);
    expect(due(everyThree, '2026-10-04')).toBe(true);
    expect(due(everyThree, '2026-10-07')).toBe(true);
  });

  it('reads the weekday mask with Monday as bit 0', () => {
    const mondays = rule({ pattern: 'SelectedWeekdays', weekdayMask: 1 });
    const sundays = rule({ pattern: 'SelectedWeekdays', weekdayMask: 1 << 6 });

    expect(due(mondays, '2026-10-05')).toBe(true);
    expect(due(mondays, '2026-10-06')).toBe(false);
    expect(due(sundays, '2026-10-04')).toBe(true);
  });
});

describe('a version bounded by its dates', () => {
  it('is due through its end day and on no day after it', () => {
    const ended = rule({ effectiveTo: '2026-10-10' });

    expect(due(ended, '2026-10-10')).toBe(true);
    expect(due(ended, '2026-10-11')).toBe(false);
    expect(days(dueDaysFrom(ended, TODAY, 30))).toEqual([
      '2026-10-05', '2026-10-06', '2026-10-07', '2026-10-08', '2026-10-09', '2026-10-10',
    ]);
  });

  it('asks for nothing before it starts', () => {
    const later = rule({ effectiveFrom: '2026-10-08' });

    expect(due(later, '2026-10-07')).toBe(false);
    expect(due(later, '2026-10-08')).toBe(true);
    expect(days(upcomingDueDays(later, TODAY))[0]).toBe('2026-10-08');
  });

  it('asks for nothing at all once ended, even on a daily pattern', () => {
    const endedLastWeek = rule({ effectiveTo: '2026-09-30' });

    expect(upcomingDueDays(endedLastWeek, TODAY)).toEqual([]);
  });
});

describe('what places no reminders', () => {
  it('a paused version', () => {
    expect(upcomingDueDays(rule({ isPaused: true }), TODAY)).toEqual([]);
  });

  it('an as-needed plan, which is available but never due', () => {
    const asNeeded = rule({ kind: 'AsNeeded' });

    expect(due(asNeeded, '2026-10-05')).toBe(true);
    expect(upcomingDueDays(asNeeded, TODAY)).toEqual([]);
  });
});

describe('the reminder horizon', () => {
  it('holds fourteen days of a daily plan', () => {
    const daily = upcomingDueDays(rule({}), TODAY);

    expect(daily).toHaveLength(14);
    expect(formatDay(daily[0])).toBe('2026-10-05');
    expect(formatDay(daily[13])).toBe('2026-10-18');
  });

  it('holds every monthly occurrence inside the scan window, on the clamped day', () => {
    const fifteenth = upcomingDueDays(rule({ pattern: 'DayOfMonth', dayOfMonth: 15 }), TODAY);

    expect(days(fifteenth).slice(0, 2)).toEqual(['2026-10-15', '2026-11-15']);
    expect(fifteenth).toHaveLength(13);
    expect(fifteenth.every((day) => day.day === 15)).toBe(true);
  });
});

describe('when a repeating OS trigger is exact', () => {
  it('only for a started, open-ended daily or weekly version', () => {
    expect(canRepeatOnDevice(rule({}), TODAY)).toBe(true);
    expect(canRepeatOnDevice(rule({ pattern: 'SelectedWeekdays', weekdayMask: 5 }), TODAY)).toBe(true);
    expect(canRepeatOnDevice(rule({ effectiveFrom: '2026-10-05' }), TODAY)).toBe(true);
  });

  it('never for a version with an end, a future start, a monthly pattern or a pause', () => {
    expect(canRepeatOnDevice(rule({ effectiveTo: '2026-12-31' }), TODAY)).toBe(false);
    expect(canRepeatOnDevice(rule({ effectiveFrom: '2026-10-06' }), TODAY)).toBe(false);
    expect(canRepeatOnDevice(rule({ pattern: 'DayOfMonth', dayOfMonth: 1 }), TODAY)).toBe(false);
    expect(canRepeatOnDevice(rule({ pattern: 'EveryNDays', intervalDays: 2, effectiveFrom: '2026-10-01' }), TODAY)).toBe(false);
    expect(canRepeatOnDevice(rule({ isPaused: true }), TODAY)).toBe(false);
  });
});

describe('wall clocks', () => {
  it('turns a local time into the instant the server would', () => {
    expect(instantForWallClock(parseDay('2026-01-15'), 8, 0, 'Europe/Istanbul').toISOString())
      .toBe('2026-01-15T05:00:00.000Z');
  });

  it('moves a time that does not exist on a spring-forward day an hour later', () => {
    // Berlin skips 02:00–03:00 on 2026-03-29; 02:30 becomes 03:30 CEST, as on the server.
    expect(instantForWallClock(parseDay('2026-03-29'), 2, 30, 'Europe/Berlin').toISOString())
      .toBe('2026-03-29T01:30:00.000Z');
  });

  it('picks the standard-time occurrence of a time that happens twice on a fall-back day', () => {
    // Berlin repeats 02:00–03:00 on 2026-10-25; the server resolves 02:30 as CET.
    expect(instantForWallClock(parseDay('2026-10-25'), 2, 30, 'Europe/Berlin').toISOString())
      .toBe('2026-10-25T01:30:00.000Z');
  });

  it('reads the calendar day in the plan zone, not in UTC', () => {
    expect(formatDay(startOfDayParts(new Date('2026-10-05T22:30:00Z'), 'Europe/Istanbul')))
      .toBe('2026-10-06');
  });
});
