'use client';

import { useCallback, useEffect, useState } from 'react';
import { ApiError, api } from '@/lib/api';
import { enumKey, errorKey, useLocale } from '@/lib/i18n';
import { formatQuantity } from '@/lib/quantity';
import type { Activity, Workspace } from '@/lib/types';
import { CorrectionDialog } from './Today';
import { Badge, Button, Card, EmptyState, Notice, Spinner } from './ui';

/**
 * One chronological surface for everything that happened.
 *
 * Stock movements, recorded doses and stock-source corrections are separate kinds of
 * event, so they are shown as separate streams rather than flattened into a single
 * list that hides which is which.
 */
export function History({ household, workspace, onChanged }: {
  household: string;
  workspace: Workspace;
  onChanged: () => void;
}) {
  const { t, locale } = useLocale();
  const [activity, setActivity] = useState<Activity | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [correcting, setCorrecting] = useState<string | null>(null);

  const load = useCallback(
    () =>
      api
        .activity(household)
        .then((value) => {
          setActivity(value);
          setError(null);
        })
        .catch((caught: unknown) =>
          setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork')),
        ),
    [household, t],
  );

  useEffect(() => {
    load();
  }, [load]);

  if (!activity) {
    return error ? <Notice tone="danger">{error}</Notice> : <Spinner label={t('loading')} />;
  }

  const medicationName = (id: string) =>
    workspace.medications.find((medication) => medication.id === id)?.name ?? '—';
  const personName = (id: string) => workspace.people.find((person) => person.id === id)?.name ?? '—';

  const moment = (value: string) =>
    new Date(value).toLocaleString(locale === 'tr' ? 'tr-TR' : 'en-GB', {
      dateStyle: 'medium',
      timeStyle: 'short',
    });

  const packageLabel = (label: number | null) =>
    label === null ? t('looseStock') : `${t('packageOrdinal')} ${label}`;

  const empty =
    activity.inventory.length === 0 &&
    activity.administrations.length === 0 &&
    activity.allocationCorrections.length === 0;

  if (empty) {
    return <EmptyState title={t('historyEmpty')} />;
  }

  return (
    <div className="flex flex-col gap-6">
      {error ? <Notice tone="danger">{error}</Notice> : null}

      {activity.allocationCorrections.length > 0 ? (
        <section className="flex flex-col gap-2">
          <h2 className="text-lg font-bold">{t('historyCorrections')}</h2>
          <ul className="flex list-none flex-col gap-2 p-0">
            {activity.allocationCorrections.map((correction) => (
              <Card as="li" key={correction.id} className="flex flex-wrap items-center gap-3">
                <Badge tone="accent">{t('correctionApplied')}</Badge>
                <p className="flex-1 text-sm">
                  {packageLabel(correction.fromPackageLabel)} → {packageLabel(correction.toPackageLabel)} ·{' '}
                  {formatQuantity(correction.quantity)}
                  {correction.reason ? ` · ${correction.reason}` : ''}
                </p>
                <time className="text-sm text-ink-faint">{moment(correction.recordedAt)}</time>
              </Card>
            ))}
          </ul>
        </section>
      ) : null}

      <section className="flex flex-col gap-2">
        <h2 className="text-lg font-bold">{t('historyAdministrations')}</h2>
        <ul className="flex list-none flex-col gap-2 p-0">
          {activity.administrations.map((event) => {
            const lateness = event.latenessMinutes;
            return (
              <Card as="li" key={event.id} className="flex flex-wrap items-center gap-3">
                <div className="min-w-48 flex-1">
                  <p className="font-semibold">{medicationName(event.medicationDefinitionId)}</p>
                  <p className="text-sm text-ink-muted">
                    {personName(event.personId)}
                    {event.actualQuantity ? ` · ${formatQuantity(event.actualQuantity)}` : ''}
                  </p>
                </div>

                <div className="flex flex-wrap items-center gap-1.5">
                  <Badge tone={event.outcome === 'Skipped' ? 'quiet' : 'positive'}>
                    {event.outcome === 'Skipped' ? t('recordedSkipped') : t('recordedTaken')}
                  </Badge>
                  {event.stockSource === 'UntrackedExternal' ? (
                    <Badge tone="warning">{t('untrackedSource')}</Badge>
                  ) : null}
                  {/* Lateness is derived from the timestamps, never stored as a status. */}
                  {lateness !== null && Math.abs(lateness) >= 15 ? (
                    <Badge tone="quiet">
                      {Math.abs(lateness)} {lateness > 0 ? t('lateBy') : t('earlyBy')}
                    </Badge>
                  ) : null}
                </div>

                <time className="text-sm text-ink-faint">{moment(event.occurredAt)}</time>

                {event.stockSource === 'TrackedInventory' && event.outcome !== 'Skipped' ? (
                  <Button variant="quiet" onClick={() => setCorrecting(event.id)}>
                    {t('correctSource')}
                  </Button>
                ) : null}
              </Card>
            );
          })}
          {activity.administrations.length === 0 ? (
            <li className="text-sm text-ink-muted">{t('none')}</li>
          ) : null}
        </ul>
      </section>

      <section className="flex flex-col gap-2">
        <h2 className="text-lg font-bold">{t('historyInventory')}</h2>
        <ul className="flex list-none flex-col gap-2 p-0">
          {activity.inventory.map((entry) => {
            const kind = enumKey(entry.entryType);
            const positive = entry.quantity.numerator > 0;
            return (
              <Card as="li" key={entry.id} className="flex flex-wrap items-center gap-3">
                <div className="min-w-48 flex-1">
                  <p className="font-semibold">{medicationName(entry.medicationDefinitionId)}</p>
                  <p className="text-sm text-ink-muted">
                    {kind ? t(kind) : entry.entryType}
                    {entry.packageLabel !== null ? ` · ${packageLabel(entry.packageLabel)}` : ''}
                    {entry.reason ? ` · ${entry.reason}` : ''}
                  </p>
                </div>
                <p className={`font-bold ${positive ? 'text-positive' : 'text-accent-ink'}`}>
                  {positive ? '+' : ''}
                  {formatQuantity(entry.quantity)}
                </p>
                <time className="text-sm text-ink-faint">{moment(entry.recordedAt)}</time>
              </Card>
            );
          })}
          {activity.inventory.length === 0 ? <li className="text-sm text-ink-muted">{t('none')}</li> : null}
        </ul>
      </section>

      {correcting ? (
        <CorrectionDialog
          household={household}
          administrationId={correcting}
          onClose={() => setCorrecting(null)}
          onCorrected={() => {
            setCorrecting(null);
            load();
            onChanged();
          }}
        />
      ) : null}
    </div>
  );
}
