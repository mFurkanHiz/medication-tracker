'use client';

import { useCallback, useSyncExternalStore } from 'react';
import { LOCALE_STORAGE_KEY, type Locale } from './i18n';

/**
 * The reader's locale, read as an external store rather than copied into state by an
 * effect.
 *
 * `localStorage` genuinely is an external system, so `useSyncExternalStore` is the
 * right primitive: there is no synchronous `setState` during render, the prerendered
 * HTML and the first client render agree because the server snapshot is the default
 * locale, and a change in another tab arrives through the `storage` event for free.
 */

const DEFAULT: Locale = 'tr';

const listeners = new Set<() => void>();

function notify() {
  for (const listener of listeners) {
    listener();
  }
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  window.addEventListener('storage', listener);

  return () => {
    listeners.delete(listener);
    window.removeEventListener('storage', listener);
  };
}

function getSnapshot(): Locale {
  try {
    const stored = window.localStorage.getItem(LOCALE_STORAGE_KEY);
    return stored === 'tr' || stored === 'en' ? stored : DEFAULT;
  } catch {
    // Private browsing and blocked site data both throw; the default still applies.
    return DEFAULT;
  }
}

/** The prerendered HTML is built with the default locale. */
function getServerSnapshot(): Locale {
  return DEFAULT;
}

export function useStoredLocale(): [Locale, (next: Locale) => void] {
  const locale = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  const setLocale = useCallback((next: Locale) => {
    try {
      window.localStorage.setItem(LOCALE_STORAGE_KEY, next);
    } catch {
      // Not being able to remember the choice is not worth failing the interaction.
    }

    // `storage` only fires in other tabs, so this tab is notified directly.
    notify();
  }, []);

  return [locale, setLocale];
}
