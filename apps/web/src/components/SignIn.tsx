'use client';

import { useState } from 'react';
import { ApiError, api } from '@/lib/api';
import { errorKey, useLocale } from '@/lib/i18n';
import { Button, Card, Field, Input, Notice } from './ui';

/** Registration and sign-in. Password handling stays entirely on the server. */
export function SignIn({ onSignedIn }: { onSignedIn: () => void }) {
  const { t } = useLocale();
  const [mode, setMode] = useState<'signIn' | 'signUp'>('signIn');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    if (mode === 'signUp' && password !== confirm) {
      setError(t('errorPasswordMismatch'));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      if (mode === 'signUp') {
        await api.register(email.trim(), password, confirm);
      } else {
        await api.signIn(email.trim(), password);
      }
      onSignedIn();
    } catch (caught) {
      if (caught instanceof ApiError) {
        setError(t(caught.status === 401 ? 'errorInvalidCredentials' : errorKey(caught.code)));
      } else {
        setError(t('errorNetwork'));
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mx-auto grid max-w-5xl items-center gap-10 py-12 lg:grid-cols-[1.1fr_1fr]">
      <div>
        <p className="text-sm font-bold uppercase tracking-[0.16em] text-accent-ink">{t('appName')}</p>
        <h1 className="mt-4 text-4xl font-medium sm:text-5xl">{t('appDescription')}</h1>
        <p className="mt-4 max-w-prose text-ink-muted">{t('safetyNotice')}</p>
      </div>

      <Card>
        <h2 className="mb-4 text-xl font-bold">{mode === 'signUp' ? t('signUpTitle') : t('signInTitle')}</h2>

        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            void submit();
          }}
        >
          {error ? <Notice tone="danger">{error}</Notice> : null}

          <Field label={t('email')}>
            {({ id }) => (
              <Input
                id={id}
                type="email"
                autoComplete="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            )}
          </Field>

          <Field label={t('password')} hint={mode === 'signUp' ? t('passwordHint') : undefined}>
            {({ id, describedBy }) => (
              <Input
                id={id}
                aria-describedby={describedBy}
                type="password"
                autoComplete={mode === 'signUp' ? 'new-password' : 'current-password'}
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            )}
          </Field>

          {mode === 'signUp' ? (
            <Field label={t('confirmPassword')}>
              {({ id }) => (
                <Input
                  id={id}
                  type="password"
                  autoComplete="new-password"
                  required
                  value={confirm}
                  onChange={(e) => setConfirm(e.target.value)}
                />
              )}
            </Field>
          ) : null}

          <Button type="submit" disabled={busy}>
            {mode === 'signUp' ? t('signUp') : t('signIn')}
          </Button>

          <Button
            type="button"
            variant="quiet"
            onClick={() => {
              setMode(mode === 'signUp' ? 'signIn' : 'signUp');
              setError(null);
            }}
          >
            {mode === 'signUp' ? t('haveAccount') : t('needAccount')}
          </Button>
        </form>

        <p className="mt-4 text-sm text-ink-faint">{t('demoNotice')}</p>
      </Card>
    </div>
  );
}
