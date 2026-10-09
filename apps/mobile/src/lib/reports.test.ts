import { describe, expect, it } from 'vitest';
import { exportFilename, periodRange, ratioPercent } from './reports';

describe('periodRange', () => {
  const today = new Date(2026, 9, 8); // 8 October 2026, local

  it('counts the period inclusively, ending today', () => {
    expect(periodRange('last7', today)).toEqual({ from: '2026-10-02', to: '2026-10-08' });
    expect(periodRange('last30', today)).toEqual({ from: '2026-09-09', to: '2026-10-08' });
    expect(periodRange('last90', today)).toEqual({ from: '2026-07-11', to: '2026-10-08' });
  });

  it('starts this month on its first day', () => {
    expect(periodRange('thisMonth', today)).toEqual({ from: '2026-10-01', to: '2026-10-08' });
    expect(periodRange('thisMonth', new Date(2026, 0, 1))).toEqual({ from: '2026-01-01', to: '2026-01-01' });
  });
});

describe('ratioPercent', () => {
  it('rounds the exact pair to a whole percentage', () => {
    expect(ratioPercent({ numerator: 3, denominator: 4 })).toBe(75);
    expect(ratioPercent({ numerator: 2, denominator: 3 })).toBe(67);
    expect(ratioPercent({ numerator: 0, denominator: 5 })).toBe(0);
  });

  it('has nothing to say when nothing was scheduled', () => {
    expect(ratioPercent(null)).toBeNull();
    expect(ratioPercent({ numerator: 0, denominator: 0 })).toBeNull();
  });
});

describe('exportFilename', () => {
  it("reads the server's name, encoded form first", () => {
    expect(
      exportFilename(
        "attachment; filename=medication-tracker-export-2026-10-08.json; filename*=UTF-8''medication-tracker-export-2026-10-08.json",
        'fallback.json',
      ),
    ).toBe('medication-tracker-export-2026-10-08.json');
    expect(exportFilename('attachment; filename="plain name.json"', 'fallback.json')).toBe('plain name.json');
    expect(exportFilename("attachment; filename*=UTF-8''ev%20d%C3%B6k%C3%BCm%C3%BC.json", 'fallback.json')).toBe('ev dökümü.json');
  });

  it('falls back when the header is missing or says nothing', () => {
    expect(exportFilename(null, 'fallback.json')).toBe('fallback.json');
    expect(exportFilename('inline', 'fallback.json')).toBe('fallback.json');
  });
});
