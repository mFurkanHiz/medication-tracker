import { describe, expect, it } from 'vitest';
import { versionLabel } from './version';

describe('the version label', () => {
  it('shows the version and the short commit when the build knew it', () => {
    expect(versionLabel('1.0.1', '9bf217c9ea7760cdfeff0565194f0519ec923767')).toBe('v1.0.1 · 9bf217c');
  });

  it('shows only the version for a build without a commit', () => {
    expect(versionLabel('1.0.1', '')).toBe('v1.0.1');
    expect(versionLabel('1.0.1', '   ')).toBe('v1.0.1');
  });
});
