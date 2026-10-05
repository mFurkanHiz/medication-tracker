/**
 * Calendar dates as the viewer lives them, as `YYYY-MM-DD`.
 *
 * `toISOString().slice(0, 10)` is the UTC date, which for a viewer in Türkiye is still
 * yesterday until three in the morning. A plan "starting today" or "ending today" means
 * the day on their wall calendar, so everything here works in local time.
 */
export function todayIso(now: Date = new Date()): string {
  return toIso(now);
}

/** Shifts an ISO date by whole days, in local time. */
export function addDaysIso(iso: string, days: number): string {
  const [year, month, day] = iso.split('-').map(Number);
  return toIso(new Date(year, month - 1, day + days));
}

/** The later of two ISO dates; a missing second date yields the first. */
export function laterOf(iso: string, other: string | null | undefined): string {
  return other && other > iso ? other : iso;
}

function toIso(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}
