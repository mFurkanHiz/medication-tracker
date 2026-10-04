'use client';

import { useState } from 'react';
import { ApiError, api } from '@/lib/api';
import { errorKey, useLocale } from '@/lib/i18n';
import type { Workspace } from '@/lib/types';
import { Advanced, Badge, Button, Card, Dialog, EmptyState, Field, Input, Notice } from './ui';

/**
 * The people whose medication the household organises.
 *
 * Distinct from an account: a household can track medication for someone who never
 * signs in. Removing a person archives them, because packages, plans and recorded
 * doses still point at them.
 */
export function People({ household, workspace, onChanged }: {
  household: string;
  workspace: Workspace;
  onChanged: () => void;
}) {
  const { t } = useLocale();
  const [name, setName] = useState('');
  const [renaming, setRenaming] = useState<{ id: string; value: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmingArchive, setConfirmingArchive] = useState<{ id: string; name: string } | null>(null);

  const active = workspace.people.filter((person) => !person.isArchived);
  const archived = workspace.people.filter((person) => person.isArchived);

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      onChanged();
    } catch (caught) {
      setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      {error ? <Notice tone="danger">{error}</Notice> : null}

      <Card>
        <form
          className="flex flex-wrap items-end gap-3"
          onSubmit={(event) => {
            event.preventDefault();
            if (name.trim() === '') {
              return;
            }
            void run(async () => {
              await api.addPerson(household, name.trim());
              setName('');
            });
          }}
        >
          <Field label={t('personName')} className="min-w-48 flex-1">
            {({ id }) => <Input id={id} value={name} onChange={(e) => setName(e.target.value)} />}
          </Field>
          <Button type="submit" disabled={busy || name.trim() === ''}>
            {t('addPerson')}
          </Button>
        </form>
      </Card>

      {active.length === 0 ? (
        <EmptyState title={t('peopleEmpty')} hint={t('peopleEmptyHint')} />
      ) : (
        <ul className="flex list-none flex-col gap-2 p-0">
          {active.map((person) => (
            <Card as="li" key={person.id} className="flex flex-wrap items-center justify-between gap-3">
              {renaming?.id === person.id ? (
                <form
                  className="flex flex-1 flex-wrap items-end gap-2"
                  onSubmit={(event) => {
                    event.preventDefault();
                    const value = renaming.value.trim();
                    if (value === '') {
                      return;
                    }
                    void run(async () => {
                      await api.renamePerson(household, person.id, value);
                      setRenaming(null);
                    });
                  }}
                >
                  <Field label={t('personName')} className="min-w-40 flex-1">
                    {({ id }) => (
                      <Input
                        id={id}
                        value={renaming.value}
                        onChange={(e) => setRenaming({ id: person.id, value: e.target.value })}
                        autoFocus
                      />
                    )}
                  </Field>
                  <Button type="submit" disabled={busy}>
                    {t('save')}
                  </Button>
                  <Button type="button" variant="secondary" onClick={() => setRenaming(null)}>
                    {t('cancel')}
                  </Button>
                </form>
              ) : (
                <>
                  <p className="font-semibold">{person.name}</p>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      variant="quiet"
                      onClick={() => setRenaming({ id: person.id, value: person.name })}
                    >
                      {t('rename')}
                    </Button>
                    <Button
                      variant="danger"
                      disabled={busy}
                      onClick={() => setConfirmingArchive({ id: person.id, name: person.name })}
                    >
                      {t('archivePerson')}
                    </Button>
                  </div>
                </>
              )}
            </Card>
          ))}
        </ul>
      )}

      {/* Archiving a person used to fire on the first click, had no cascade at all, and
          could not be undone — their plans simply kept producing doses for ever. It now
          says what it does, and counts the plans it will set aside. */}
      {confirmingArchive ? (
        <Dialog
          open
          onClose={() => setConfirmingArchive(null)}
          title={`${t('archivePersonTitle')} — ${confirmingArchive.name}`}
          footer={
            <>
              <Button variant="secondary" onClick={() => setConfirmingArchive(null)}>
                {t('cancel')}
              </Button>
              <Button
                variant="danger"
                disabled={busy}
                onClick={async () => {
                  const target = confirmingArchive.id;
                  setConfirmingArchive(null);
                  await run(() => api.archivePerson(household, target));
                }}
              >
                {t('archivePerson')}
              </Button>
            </>
          }
        >
          <div className="flex flex-col gap-3 text-sm">
            <p>{t('archivePersonKeeps')}</p>

            {(() => {
              const theirs = workspace.plans.filter(
                (plan) => plan.personId === confirmingArchive.id && !plan.isPaused,
              ).length;

              return theirs > 0 ? (
                <Notice tone="warning">
                  {t('archivePersonPausesPlansBefore')} {theirs}{' '}
                  {t('archivePersonPausesPlansAfter')}
                </Notice>
              ) : (
                <p className="text-ink-muted">{t('archivePersonNoPlans')}</p>
              );
            })()}
          </div>
        </Dialog>
      ) : null}

      {archived.length > 0 ? (
        <Advanced label={`${t('archived')} (${archived.length})`}>
          <ul className="flex list-none flex-col gap-2 p-0">
            {archived.map((person) => (
              <li key={person.id} className="flex flex-wrap items-center gap-2">
                <span>{person.name}</span>
                <Badge tone="quiet">{t('archived')}</Badge>

                {/* Person.Restore() existed in the domain from the start and no route
                    ever called it, so this list was a dead end. */}
                <Button
                  variant="quiet"
                  disabled={busy}
                  onClick={() => void run(() => api.restorePerson(household, person.id))}
                >
                  {t('restorePerson')}
                </Button>
              </li>
            ))}
          </ul>
        </Advanced>
      ) : null}
    </div>
  );
}
