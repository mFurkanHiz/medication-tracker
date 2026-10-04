import { act, renderHook } from '@testing-library/react';
import { createElement } from 'react';
import { renderToString } from 'react-dom/server';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useNow } from '@/lib/use-now';

/**
 * The screen's clock.
 *
 * This hook exists because the first version of the "too soon" notice read `Date.now()`
 * during render, which the React compiler's purity rule rejected — correctly, since a
 * warning that nothing re-renders for is a warning that never clears. The second version
 * put the time in state from an effect and tripped the set-state-in-effect rule. This is
 * the third, and its one load-bearing property is the one that is only ever noticed when
 * it breaks: the snapshot must be identical between ticks, or `useSyncExternalStore`
 * re-renders for ever.
 */

// A moment that divides evenly by every interval used below, so the arithmetic in the
// assertions is exact rather than approximately right. Checked, because the first value
// chosen here divided by 1 000 and not by 30 000, and the quantisation test then failed
// by exactly the ten seconds that mistake is worth.
const ALIGNED = 1_700_000_010_000;

if (ALIGNED % 30_000 !== 0 || ALIGNED % 1_000 !== 0) {
  throw new Error('ALIGNED must be a multiple of every interval the tests use.');
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(ALIGNED);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('useNow', () => {
  it('reads the clock quantised to the interval', () => {
    vi.setSystemTime(ALIGNED + 17_250);

    const { result } = renderHook(() => useNow(30_000));

    // 17.25 seconds into a 30-second interval reads as the start of that interval. The
    // value trails real time by up to one interval, which only ever makes a "too soon"
    // notice linger a little longer — the safe direction.
    expect(result.current).toBe(ALIGNED);
  });

  it('returns the same snapshot on every render within an interval', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    const { result, rerender } = renderHook(() => useNow(1_000));

    const first = result.current;
    rerender();
    rerender();

    // React reports an unstable snapshot through console.error ("The result of
    // getSnapshot should be cached") before it gives up and throws. No error and an
    // identical value is the proof that the quantisation is doing its job.
    expect(result.current).toBe(first);
    expect(error).not.toHaveBeenCalled();
  });

  it('does not move until a whole interval has passed', () => {
    const { result } = renderHook(() => useNow(1_000));

    act(() => {
      vi.advanceTimersByTime(999);
    });

    expect(result.current).toBe(ALIGNED);
  });

  it('moves forward by exactly one interval when the interval elapses', () => {
    const { result } = renderHook(() => useNow(1_000));

    act(() => {
      vi.advanceTimersByTime(1_000);
    });

    expect(result.current).toBe(ALIGNED + 1_000);

    act(() => {
      vi.advanceTimersByTime(3_000);
    });

    expect(result.current).toBe(ALIGNED + 4_000);
  });

  it('claims nothing in the server-rendered HTML', () => {
    // The prerendered page must not say "too soon" or "fine" about anything, because it
    // does not know what time it is where the reader is. Null is the honest answer there;
    // the first client render replaces it.
    function Probe() {
      return createElement('span', null, String(useNow()));
    }

    expect(renderToString(createElement(Probe))).toContain('null');
  });

  it('stops the timer when the component goes away', () => {
    const clearInterval = vi.spyOn(window, 'clearInterval');
    const { unmount } = renderHook(() => useNow(1_000));

    unmount();

    // A timer that outlives its screen keeps calling a subscriber nothing listens to, and
    // on a page somebody navigates around for hours those add up.
    expect(clearInterval).toHaveBeenCalled();
  });
});
