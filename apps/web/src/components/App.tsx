'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError, api } from '@/lib/api';
import { LOCALES, LocaleProvider, dictionaries, errorKey, type MessageKey } from '@/lib/i18n';
import { useStoredLocale } from '@/lib/locale-store';
import type { Session, Workspace } from '@/lib/types';
import { Counting } from './Counting';
import { History } from './History';
import { Inventory } from './Inventory';
import { People } from './People';
import { Plans } from './Plans';
import { Reports } from './Reports';
import { SignIn } from './SignIn';
import { Today } from './Today';
import { Button, Notice, Spinner } from './ui';

const TABS: { id: string; label: MessageKey }[] = [
  { id: 'today', label: 'today' },
  { id: 'inventory', label: 'inventory' },
  { id: 'counting', label: 'counting' },
  { id: 'plans', label: 'plans' },
  { id: 'people', label: 'people' },
  { id: 'history', label: 'history' },
  { id: 'reports', label: 'reports' },
];

/**
 * The application shell: locale, session, household, and navigation.
 *
 * Everything runs in the browser against the same-origin authenticated API, which is
 * what lets the site ship as a static export with no server of its own.
 */
export function App() {
  const [locale, chooseLocale] = useStoredLocale();
  const [session, setSession] = useState<Session | null>(null);
  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [tab, setTab] = useState('today');
  const [error, setError] = useState<string | null>(null);
  const [checked, setChecked] = useState(false);

  const t = useMemo(() => (key: MessageKey) => dictionaries[locale][key], [locale]);

  // Updating the document language is exactly what an effect is for: synchronising
  // an external system with React state.
  useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);

  const loadSession = useCallback(
    () =>
      api
        .session()
        .then((value) => {
          setSession(value);
          setError(null);
        })
        .catch((caught: unknown) => {
          if (caught instanceof ApiError && caught.status === 401) {
            setSession(null);
          } else {
            setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
          }
        })
        .finally(() => setChecked(true)),
    [t],
  );

  useEffect(() => {
    loadSession();
  }, [loadSession]);

  const household = session?.households[0]?.id ?? null;

  const loadWorkspace = useCallback(() => {
    if (!household) {
      return Promise.resolve();
    }

    return api
      .workspace(household)
      .then((value) => {
        setWorkspace(value);
        setError(null);
      })
      .catch((caught: unknown) =>
        setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork')),
      );
  }, [household, t]);

  useEffect(() => {
    loadWorkspace();
  }, [loadWorkspace]);

  const context = useMemo(() => ({ locale, t, setLocale: chooseLocale }), [locale, t, chooseLocale]);

  return (
    <LocaleProvider value={context}>
      <div className="mx-auto min-h-screen w-full max-w-5xl px-4 pb-16 pt-6 sm:px-6">
        <header className="flex flex-wrap items-center justify-between gap-3">
          <p className="flex items-center gap-2 font-extrabold tracking-tight">
            <span
              aria-hidden
              className="grid size-9 place-items-center rounded-xl bg-accent text-surface-raised"
            >
              ℞
            </span>
            {t('appName')}
          </p>

          {/*
            * Wraps, because it did not. At twice the root font size on a 375px screen the
            * locale group and the sign-out button together measured 391px and pushed the
            * document to 423px, so every screen scrolled sideways for exactly the reader
            * who had turned the text up to read it. The <header> above already wrapped;
            * this row inside it did not, which is why the overflow survived.
            */}
          <div className="flex flex-wrap items-center justify-end gap-2">
            <div role="group" aria-label={t('appName')} className="flex gap-1">
              {LOCALES.map((option) => (
                <Button
                  key={option}
                  variant={option === locale ? 'primary' : 'secondary'}
                  aria-pressed={option === locale}
                  onClick={() => chooseLocale(option)}
                  className="px-3 py-1 text-sm"
                >
                  {option.toUpperCase()}
                </Button>
              ))}
            </div>

            {session ? (
              <Button
                variant="secondary"
                onClick={async () => {
                  await api.signOut().catch(() => {});
                  setSession(null);
                  setWorkspace(null);
                }}
              >
                {t('signOut')}
              </Button>
            ) : null}
          </div>
        </header>

        <main>
          {!checked ? (
            <Spinner label={t('loading')} />
          ) : !session ? (
            <SignIn
              onSignedIn={() => {
                setChecked(false);
                loadSession();
              }}
            />
          ) : !household ? (
            <Notice tone="danger">{t('errorForbidden')}</Notice>
          ) : (
            <>
              <nav aria-label={t('appName')} className="my-6 flex gap-2 overflow-x-auto border-b border-line pb-3">
                {TABS.map((entry) => (
                  <Button
                    key={entry.id}
                    variant={tab === entry.id ? 'primary' : 'secondary'}
                    aria-current={tab === entry.id ? 'page' : undefined}
                    onClick={() => setTab(entry.id)}
                    className="whitespace-nowrap"
                  >
                    {t(entry.label)}
                  </Button>
                ))}
              </nav>

              {error ? <Notice tone="danger">{error}</Notice> : null}

              {!workspace ? (
                <Spinner label={t('loading')} />
              ) : tab === 'today' ? (
                <Today household={household} workspace={workspace} onChanged={() => loadWorkspace()} />
              ) : tab === 'inventory' ? (
                <Inventory household={household} workspace={workspace} onChanged={() => loadWorkspace()} />
              ) : tab === 'counting' ? (
                <Counting household={household} workspace={workspace} onChanged={() => loadWorkspace()} />
              ) : tab === 'plans' ? (
                <Plans household={household} workspace={workspace} onChanged={() => loadWorkspace()} />
              ) : tab === 'people' ? (
                <People household={household} workspace={workspace} onChanged={() => loadWorkspace()} />
              ) : tab === 'reports' ? (
                <Reports household={household} workspace={workspace} />
              ) : (
                <History household={household} workspace={workspace} onChanged={() => loadWorkspace()} />
              )}
            </>
          )}
        </main>

        <footer className="mt-12 border-t border-line pt-6 text-sm text-ink-faint">
          <p>{t('safetyNotice')}</p>
        </footer>
      </div>
    </LocaleProvider>
  );
}
