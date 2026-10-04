import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LOCALE_STORAGE_KEY } from '@/lib/i18n';
import { useStoredLocale } from '@/lib/locale-store';

/**
 * The reader's language choice.
 *
 * Worth testing not because the logic is hard but because the module's own comment makes
 * two promises a comment cannot keep: that a blocked `localStorage` falls back to the
 * default instead of throwing, and that a change in another tab arrives. Both are the
 * kind of claim that stays true until somebody edits the file.
 */

afterEach(() => {
  window.localStorage.clear();
});

describe('useStoredLocale', () => {
  it('starts in Turkish, which is what the prerendered HTML was built with', () => {
    const { result } = renderHook(() => useStoredLocale());

    // If this default ever disagrees with the one in getServerSnapshot, the first client
    // render and the served HTML disagree, and React replaces the whole tree.
    expect(result.current[0]).toBe('tr');
  });

  it('remembers a choice', () => {
    const { result } = renderHook(() => useStoredLocale());

    act(() => result.current[1]('en'));

    expect(result.current[0]).toBe('en');
    expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('en');
  });

  it('ignores a stored value that is not a language we have', () => {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, 'klingon');

    const { result } = renderHook(() => useStoredLocale());

    expect(result.current[0]).toBe('tr');
  });

  it('falls back to the default when reading storage throws', () => {
    // Private browsing and blocked site data both throw on access rather than returning
    // null. The module claims to handle it; this is the claim.
    //
    // Spied on the prototype rather than on `window.localStorage`, because jsdom hands out
    // `localStorage` through a Proxy: an own property defined on the instance is not what
    // the module's own `localStorage.getItem` resolves to, so an instance spy reports zero
    // calls while the code under test runs the real method. Measured, not guessed.
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError');
    });

    const { result } = renderHook(() => useStoredLocale());

    expect(result.current[0]).toBe('tr');
    expect(getItem).toHaveBeenCalled();
  });

  it('does not fail the interaction when writing storage throws', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('The quota has been exceeded.', 'QuotaExceededError');
    });

    const { result } = renderHook(() => useStoredLocale());

    // Not being able to remember the choice must not stop the choice being made: the
    // reader asked for English and should get English for this visit at least.
    expect(() => act(() => result.current[1]('en'))).not.toThrow();
  });

  it('picks up a change made in another tab', () => {
    const { result } = renderHook(() => useStoredLocale());

    act(() => {
      window.localStorage.setItem(LOCALE_STORAGE_KEY, 'en');
      window.dispatchEvent(new StorageEvent('storage', { key: LOCALE_STORAGE_KEY }));
    });

    expect(result.current[0]).toBe('en');
  });

  it('stops listening when the component goes away', () => {
    const remove = vi.spyOn(window, 'removeEventListener');
    const { unmount } = renderHook(() => useStoredLocale());

    unmount();

    // A subscription that outlives its component is a leak that only shows up after
    // enough navigation to be hard to attribute.
    expect(remove).toHaveBeenCalledWith('storage', expect.any(Function));
  });
});
