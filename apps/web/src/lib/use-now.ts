'use client';

import { useCallback, useSyncExternalStore } from 'react';

/**
 * The current time, re-read on an interval.
 *
 * A dose row has to be able to say "too soon" and then stop saying it. Reading the clock
 * straight through during render is both impure and wrong: nothing would re-render when
 * the gap finally passed, so the warning would sit there until something else happened to
 * refresh the screen — and waiting out a six-hour gap is exactly when somebody leaves
 * this page open and looks again.
 *
 * Null while hydrating the prerendered HTML, so the first paint claims nothing. Starting
 * from zero would make every gap look expired for one frame and flash a warning that then
 * vanishes. Thirty seconds is ample for a gap measured in hours.
 *
 * Lives in `lib` rather than beside the screen that first needed it because it is a
 * clock, not a dose concern — and because its one load-bearing property (the snapshot
 * must be stable between ticks) is exactly the kind of thing that is only ever noticed
 * when it breaks, so it has a test.
 */
export function useNow(intervalMs = 30_000): number | null {
  const subscribe = useCallback(
    (onChange: () => void) => {
      const timer = window.setInterval(onChange, intervalMs);
      return () => window.clearInterval(timer);
    },
    [intervalMs],
  );

  // Quantised to the interval on purpose. useSyncExternalStore compares snapshots, so a
  // reading that changed on every call would re-render for ever. The cost is that `now`
  // can trail real time by up to one interval, which only ever makes the warning linger
  // a few seconds longer than strictly necessary — the safe direction for this.
  const tick = useSyncExternalStore(
    subscribe,
    () => Math.floor(Date.now() / intervalMs),
    () => null,
  );

  return tick === null ? null : tick * intervalMs;
}
