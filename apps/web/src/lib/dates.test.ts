import { describe, expect, it } from 'vitest';
import { addDaysIso, laterOf, todayIso } from './dates';

describe('todayIso', () => {
  it('reads the calendar date where the viewer is, not the UTC date', () => {
    // Half past midnight on the 5th, local time. In any zone east of UTC the ISO string
    // of this instant still says the 4th; the wall calendar says the 5th.
    expect(todayIso(new Date(2026, 9, 5, 0, 30))).toBe('2026-10-05');
  });

  it('pads single-digit months and days', () => {
    expect(todayIso(new Date(2026, 0, 7, 12))).toBe('2026-01-07');
  });
});

describe('addDaysIso', () => {
  it('crosses month and year ends in both directions', () => {
    expect(addDaysIso('2026-10-31', 1)).toBe('2026-11-01');
    expect(addDaysIso('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDaysIso('2026-03-01', -1)).toBe('2026-02-28');
  });
});

describe('laterOf', () => {
  it('keeps the first date unless the other is later', () => {
    expect(laterOf('2026-10-05', null)).toBe('2026-10-05');
    expect(laterOf('2026-10-05', '2026-09-01')).toBe('2026-10-05');
    expect(laterOf('2026-10-05', '2026-11-01')).toBe('2026-11-01');
  });
});
