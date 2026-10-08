import { compareDays, parseDay, type DayParts, type RecurrencePattern } from '../notifications/schedule';
import { enumKey, type MessageKey, type Translate } from './i18n';

/**
 * How a plan reads on the Plans screen: its schedule in words and where it stands today.
 *
 * Pure, so it is unit-tested; the wording mirrors the web's `describeSchedule` so a
 * household member reads the same plan the same way on both screens.
 */

export type PlanShape = {
  kind: 'Scheduled' | 'AsNeeded';
  pattern: RecurrencePattern;
  weekdayMask: number | null;
  intervalDays: number | null;
  dayOfMonth: number | null;
  intervalMonths: number | null;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  localTime: string | null;
  dayPeriod: string | null;
  isPaused: boolean;
};

export type PlanStatus = 'active' | 'paused' | 'ended' | 'upcoming';

/** Monday first, matching the server's weekday-mask bits. */
export const WEEKDAY_KEYS: readonly MessageKey[] = [
  'weekdayMon', 'weekdayTue', 'weekdayWed', 'weekdayThu', 'weekdayFri', 'weekdaySat', 'weekdaySun',
];

export function weekdayNames(mask: number, t: Translate): string[] {
  return WEEKDAY_KEYS.filter((_, bit) => (mask & (1 << bit)) !== 0).map((key) => t(key));
}

export function describeSchedule(plan: PlanShape, t: Translate): string {
  const periodKey = enumKey(plan.dayPeriod);

  if (plan.kind === 'AsNeeded') {
    // A preference, never an appointment: the big word stays "as needed".
    return periodKey ? `${t('asNeeded')} · ${t('preferably')} ${t(periodKey)}` : t('asNeeded');
  }

  const when = plan.localTime ? plan.localTime.slice(0, 5) : periodKey ? t(periodKey) : null;
  const suffix = when ? ` · ${when}` : '';

  switch (plan.pattern) {
    case 'SelectedWeekdays':
      return plan.weekdayMask === null
        ? `${t('scheduleDaily')}${suffix}`
        : `${weekdayNames(plan.weekdayMask, t).join(', ')}${suffix}`;

    case 'EveryNDays':
      return `${t('scheduleEveryNDays').replace('{n}', String(plan.intervalDays ?? 1))}${suffix}`;

    case 'DayOfMonth':
      return `${t('scheduleDayOfMonth').replace('{d}', String(plan.dayOfMonth ?? 1))}${suffix}`;

    case 'EveryNMonths': {
      const anchorDay = plan.effectiveFrom ? parseDay(plan.effectiveFrom).day : 1;
      return `${t('scheduleEveryNMonths')
        .replace('{n}', String(plan.intervalMonths ?? 1))
        .replace('{d}', String(anchorDay))}${suffix}`;
    }

    default:
      return `${t('scheduleDaily')}${suffix}`;
  }
}

/**
 * Where the plan stands on `today`, in the plan's own calendar.
 *
 * Ended wins over paused: a version whose end date has passed is history whatever flag
 * it carries, which is also how the web sorts it under "Past plans".
 */
export function planStatus(plan: PlanShape, today: DayParts): PlanStatus {
  if (plan.effectiveTo !== null && compareDays(parseDay(plan.effectiveTo), today) < 0) {
    return 'ended';
  }

  if (plan.isPaused) {
    return 'paused';
  }

  if (plan.effectiveFrom !== null && compareDays(parseDay(plan.effectiveFrom), today) > 0) {
    return 'upcoming';
  }

  return 'active';
}

export const STATUS_KEYS: Record<PlanStatus, MessageKey> = {
  active: 'statusActive',
  paused: 'statusPaused',
  ended: 'statusEnded',
  upcoming: 'statusUpcoming',
};
