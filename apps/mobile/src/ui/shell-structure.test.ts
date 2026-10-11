import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

/**
 * expo-sqlite's SQLiteProvider is a React.memo whose comparator ignores `children`.
 * State held in App and passed down through it never re-renders the children, so the
 * tab bar never changed a tab on the owner's phone (2026-10-12) although every unit test
 * passed. This pins the structure that avoids it: the selected tab is state of a child
 * rendered inside the provider, never of App itself.
 */
describe('the signed-in shell', () => {
  const source = readFileSync(new URL('../../App.tsx', import.meta.url), 'utf8');

  const body = (name: string) => {
    const start = source.indexOf(`function ${name}(`);
    expect(start, `${name} is missing from App.tsx`).toBeGreaterThanOrEqual(0);
    const next = source.indexOf('\nfunction ', start + 1);
    const styles = source.indexOf('\nconst styles', start + 1);
    const ends = [next, styles].filter((index) => index > start);
    return source.slice(start, ends.length > 0 ? Math.min(...ends) : undefined);
  };

  it('keeps the selected tab out of App', () => {
    expect(body('App')).not.toMatch(/useState<Tab>/);
  });

  it('keeps the selected tab in a child that SQLiteProvider renders', () => {
    expect(body('Shell')).toMatch(/useState<Tab>/);
    expect(body('App')).toMatch(/<SQLiteProvider[\s\S]*<Shell\b[\s\S]*<\/SQLiteProvider>/);
  });
});
