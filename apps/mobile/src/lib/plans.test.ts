import { describe, expect, it } from 'vitest';
import { dictionaries, type MessageKey } from './i18n';
import { describeSchedule, planStatus, type PlanShape } from './plans';
import { parseDay } from '../notifications/schedule';

const t = (key: MessageKey) => dictionaries.tr[key];
const TODAY = parseDay('2026-10-08');

function plan(overrides: Partial<PlanShape>): PlanShape {
  return {
    kind: 'Scheduled',
    pattern: 'Daily',
    weekdayMask: null,
    intervalDays: null,
    dayOfMonth: null,
    intervalMonths: null,
    effectiveFrom: null,
    effectiveTo: null,
    localTime: null,
    dayPeriod: null,
    isPaused: false,
    ...overrides,
  };
}

describe('a schedule in words, as the web says it', () => {
  it('daily, with the clock time', () => {
    expect(describeSchedule(plan({ localTime: '08:00:00' }), t)).toBe('Her gün · 08:00');
  });

  it('daily with a named period and no clock, the period is the time', () => {
    expect(describeSchedule(plan({ dayPeriod: 'Evening' }), t)).toBe('Her gün · Akşam');
  });

  it('selected weekdays, Monday first', () => {
    expect(describeSchedule(plan({ pattern: 'SelectedWeekdays', weekdayMask: 1 | 4 | 16, localTime: '20:00:00' }), t))
      .toBe('Pzt, Çar, Cum · 20:00');
  });

  it('every N days', () => {
    expect(describeSchedule(plan({ pattern: 'EveryNDays', intervalDays: 3, effectiveFrom: '2026-10-01' }), t))
      .toBe('Her 3 günde bir');
  });

  it('a day of the month', () => {
    expect(describeSchedule(plan({ pattern: 'DayOfMonth', dayOfMonth: 15, localTime: '09:30:00' }), t))
      .toBe('Her ayın 15. günü · 09:30');
  });

  it('every N months, anchored on the start day', () => {
    expect(describeSchedule(plan({ pattern: 'EveryNMonths', intervalMonths: 2, effectiveFrom: '2026-01-31' }), t))
      .toBe('Her 2 ayda bir, ayın 31. günü');
  });

  it('as needed stays "as needed", with the period as a preference', () => {
    expect(describeSchedule(plan({ kind: 'AsNeeded' }), t)).toBe('Gerektiğinde');
    expect(describeSchedule(plan({ kind: 'AsNeeded', dayPeriod: 'Evening' }), t)).toBe('Gerektiğinde · tercihen Akşam');
  });
});

describe('where a plan stands today', () => {
  it('is active with no dates', () => {
    expect(planStatus(plan({}), TODAY)).toBe('active');
  });

  it('is ended once its end day has passed, even when paused', () => {
    expect(planStatus(plan({ effectiveTo: '2026-10-07', isPaused: true }), TODAY)).toBe('ended');
    expect(planStatus(plan({ effectiveTo: '2026-10-08' }), TODAY)).toBe('active');
  });

  it('is paused while the household set it aside', () => {
    expect(planStatus(plan({ isPaused: true }), TODAY)).toBe('paused');
  });

  it('is upcoming before its start day', () => {
    expect(planStatus(plan({ effectiveFrom: '2026-10-09' }), TODAY)).toBe('upcoming');
    expect(planStatus(plan({ effectiveFrom: '2026-10-08' }), TODAY)).toBe('active');
  });
});
