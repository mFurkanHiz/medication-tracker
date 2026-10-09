import { localDate } from './local-date';
import type { MessageKey } from './i18n';

/**
 * The pure parts of the Reports screen: which days a period covers, how a ratio reads,
 * and the name the server gave the export file. No network here, so it is unit-tested.
 */

export type PeriodId = 'last7' | 'last30' | 'last90' | 'thisMonth';

/** The periods offered; a household should not have to type two dates. */
export const PERIODS: { id: PeriodId; label: MessageKey }[] = [
  { id: 'last7', label: 'reportPeriodLast7' },
  { id: 'last30', label: 'reportPeriodLast30' },
  { id: 'last90', label: 'reportPeriodLast90' },
  { id: 'thisMonth', label: 'reportPeriodThisMonth' },
];

const DAYS: Record<Exclude<PeriodId, 'thisMonth'>, number> = { last7: 7, last30: 30, last90: 90 };

/** Both ends inclusive, in the device's calendar: "last 7 days" is seven days, not eight. */
export function periodRange(id: PeriodId, today: Date): { from: string; to: string } {
  if (id === 'thisMonth') {
    return { from: localDate(new Date(today.getFullYear(), today.getMonth(), 1)), to: localDate(today) };
  }

  const start = new Date(today);
  start.setDate(start.getDate() - (DAYS[id] - 1));
  return { from: localDate(start), to: localDate(today) };
}

/** A whole percentage from the server's exact pair; null when nothing was scheduled. */
export function ratioPercent(ratio: { numerator: number; denominator: number } | null): number | null {
  if (!ratio || ratio.denominator <= 0) {
    return null;
  }

  return Math.round((ratio.numerator / ratio.denominator) * 100);
}

/**
 * The file name from a Content-Disposition header, the encoded form first because it
 * is the one allowed to carry any character; the fallback when the header says nothing.
 */
export function exportFilename(header: string | null, fallback: string): string {
  if (!header) {
    return fallback;
  }

  const encoded = /filename\*=(?:UTF-8|utf-8)''([^;]+)/.exec(header);
  if (encoded?.[1]) {
    try {
      return decodeURIComponent(encoded[1].trim());
    } catch {
      // Fall through to the plain name.
    }
  }

  const plain = /filename="?([^";]+)"?/.exec(header);
  return plain?.[1]?.trim() || fallback;
}
