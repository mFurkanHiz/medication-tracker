/** Today in the device's own zone as `YYYY-MM-DD`, which is the day the server is asked about. */
export function localDate(now: Date = new Date()): string {
  const month = `${now.getMonth() + 1}`.padStart(2, '0');
  const day = `${now.getDate()}`.padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}

/** A `YYYY-MM-DD` value shown in the reader's locale, without a time-zone shift. */
export function formatLocalDate(value: string, locale: string): string {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);

  try {
    return new Date(year, month - 1, day).toLocaleDateString(locale === 'tr' ? 'tr-TR' : 'en-GB', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  } catch {
    return value.slice(0, 10);
  }
}
