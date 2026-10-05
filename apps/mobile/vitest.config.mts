import { defineConfig } from 'vitest/config';

/**
 * The mobile client's test runner, for the logic that does not need a device: the due-day
 * rule and the wall-clock arithmetic behind reminders. Plain Node, no React Native, so a
 * wrong reminder day fails in CI rather than on somebody's phone.
 */
export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
    restoreMocks: true,
  },
});
