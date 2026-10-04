import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

/**
 * The web client's test runner.
 *
 * Vitest with React Testing Library is what the installed Next version's own guidance
 * recommends (`node_modules/next/dist/docs/01-app/02-guides/testing/vitest.md`), so the
 * dependency list is that document's rather than a guess.
 *
 * One deviation from it, on the installed Vite's own advice: that document adds
 * `vite-tsconfig-paths`, and this Vite prints "Vite now supports tsconfig paths
 * resolution natively" when the plugin is present. `resolve.tsconfigPaths` is the same
 * behaviour with one fewer dependency. It is load-bearing either way — it makes
 * `@/lib/...` resolve in a test exactly as it does in the application, and without it the
 * imports would have to differ inside tests, which is the kind of difference that lets a
 * test pass while the screen is broken.
 */
export default defineConfig({
  plugins: [react()],
  resolve: {
    tsconfigPaths: true,
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
    setupFiles: ['src/test/setup.ts'],

    // A mock left in place by one test is a failure attributed to the next one.
    restoreMocks: true,
  },
});
