'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError, api } from '@/lib/api';
import { errorKey, useLocale, type MessageKey } from '@/lib/i18n';
import { formatQuantity } from '@/lib/quantity';
import type { AdherenceReport, AdherenceTally, InventoryReport, Workspace } from '@/lib/types';
import { Badge, Button, Card, EmptyState, Field, Notice, Select, Spinner } from './ui';

/** The periods offered by default. A household should not have to type two dates. */
const PERIODS: { id: string; label: MessageKey; days: number }[] = [
  { id: 'last7', label: 'reportPeriodLast7', days: 7 },
  { id: 'last30', label: 'reportPeriodLast30', days: 30 },
  { id: 'last90', label: 'reportPeriodLast90', days: 90 },
];

/** A local calendar date as the API's `yyyy-MM-dd`, without crossing into UTC. */
function isoDate(date: Date): string {
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

function periodRange(id: string): { from: string; to: string } {
  const today = new Date();

  if (id === 'thisMonth') {
    return {
      from: isoDate(new Date(today.getFullYear(), today.getMonth(), 1)),
      to: isoDate(today),
    };
  }

  const days = PERIODS.find((period) => period.id === id)?.days ?? 30;
  const start = new Date(today);

  // Inclusive of both ends, so "last 7 days" is seven days and not eight.
  start.setDate(start.getDate() - (days - 1));

  return { from: isoDate(start), to: isoDate(today) };
}

/**
 * Reports: what the household planned and recorded, and what it has left.
 *
 * Both halves are reads. Nothing on this screen changes anything, and nothing on it
 * interprets the numbers — there is no score, no target, and no advice, because the
 * product organises medication and does not practise medicine.
 */
export function Reports({ household, workspace }: { household: string; workspace: Workspace }) {
  const { t, locale } = useLocale();
  const [period, setPeriod] = useState('last30');
  const [adherence, setAdherence] = useState<AdherenceReport | null>(null);
  const [inventory, setInventory] = useState<InventoryReport | null>(null);
  const [error, setError] = useState<string | null>(null);

  // The browser's zone, so a local day on this screen is the user's day.
  const timeZoneId = useMemo(
    () => Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
    [],
  );

  const load = useCallback(() => {
    const { from, to } = periodRange(period);

    return Promise.all([
      api.adherenceReport(household, from, to, timeZoneId),
      api.inventoryReport(household),
    ])
      .then(([doses, stock]) => {
        setAdherence(doses);
        setInventory(stock);
        setError(null);
      })
      .catch((caught: unknown) =>
        setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork')),
      );
  }, [household, period, timeZoneId, t]);

  useEffect(() => {
    load();
  }, [load]);

  const medicationName = (id: string) =>
    workspace.medications.find((medication) => medication.id === id)?.name ?? '—';
  const personName = (id: string) =>
    workspace.people.find((person) => person.id === id)?.name ?? '—';

  const day = (value: string) =>
    new Date(`${value}T00:00:00`).toLocaleDateString(locale === 'tr' ? 'tr-TR' : 'en-GB', {
      dateStyle: 'medium',
    });

  if (!adherence || !inventory) {
    return error ? <Notice tone="danger">{error}</Notice> : <Spinner label={t('loading')} />;
  }

  return (
    <div className="flex flex-col gap-8">
      {error ? <Notice tone="danger">{error}</Notice> : null}

      <section className="flex flex-col gap-3">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <h2 className="text-lg font-bold">{t('reportsAdherence')}</h2>

          <Field label={t('reportPeriod')} className="min-w-52">
            {({ id }) => (
              <Select id={id} value={period} onChange={(event) => setPeriod(event.target.value)}>
                {PERIODS.map((option) => (
                  <option key={option.id} value={option.id}>
                    {t(option.label)}
                  </option>
                ))}
                <option value="thisMonth">{t('reportPeriodThisMonth')}</option>
              </Select>
            )}
          </Field>
        </div>

        <p className="text-sm text-ink-muted">
          {day(adherence.from)} – {day(adherence.to)}
        </p>

        {adherence.unknownTimeZoneIds.length > 0 ? (
          <Notice tone="warning">{t('reportUnknownTimeZone')}</Notice>
        ) : null}

        {adherence.rows.length === 0 ? (
          <EmptyState title={t('reportEmpty')} hint={t('reportEmptyHint')} />
        ) : (
          <>
            <ul className="flex list-none flex-col gap-2 p-0">
              {adherence.rows.map((row) => (
                <Card
                  as="li"
                  key={`${row.personId}-${row.medicationDefinitionId}`}
                  className="flex flex-col gap-3"
                >
                  <div className="flex flex-wrap items-baseline justify-between gap-2">
                    <div>
                      <p className="font-semibold">{medicationName(row.medicationDefinitionId)}</p>
                      <p className="text-sm text-ink-muted">{personName(row.personId)}</p>
                    </div>
                    <Ratio tally={row.tally} />
                  </div>
                  <Counts tally={row.tally} />
                </Card>
              ))}
            </ul>

            {adherence.rows.length > 1 ? (
              <Card className="flex flex-col gap-3 bg-surface-sunken/50">
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <p className="font-semibold">{t('reportHouseholdTotal')}</p>
                  <Ratio tally={adherence.total} />
                </div>
                <Counts tally={adherence.total} />
              </Card>
            ) : null}
          </>
        )}

        {/* Said plainly, because a number next to a medication invites being read as a
            verdict on the person taking it. */}
        <p className="text-sm text-ink-faint">{t('reportDescriptiveNotice')}</p>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-bold">{t('reportsInventory')}</h2>

        {inventory.lowStockCount > 0 || inventory.refillGapCount > 0 ? (
          <div className="flex flex-wrap gap-2">
            {inventory.lowStockCount > 0 ? (
              <Badge tone="warning">
                {inventory.lowStockCount} {t('reportLowStockSummary')}
              </Badge>
            ) : null}
            {inventory.refillGapCount > 0 ? (
              <Badge tone="danger">
                {inventory.refillGapCount} {t('reportRefillGapSummary')}
              </Badge>
            ) : null}
          </div>
        ) : null}

        {inventory.rows.length === 0 ? (
          <EmptyState title={t('inventoryEmpty')} />
        ) : (
          <ul className="flex list-none flex-col gap-2 p-0">
            {inventory.rows.map((row) => (
              <Card as="li" key={row.medicationDefinitionId} className="flex flex-wrap items-center gap-3">
                <div className="min-w-48 flex-1">
                  <p className="font-semibold">
                    {row.name}
                    {row.strength ? <span className="text-ink-muted"> · {row.strength}</span> : null}
                  </p>
                  <p className="text-sm text-ink-muted">
                    {t('reportStockRemaining')}: <strong>{formatQuantity(row.total)}</strong> ·{' '}
                    {row.packageCount} {t('packagesLabel')}
                  </p>
                </div>

                <div className="flex flex-wrap items-center gap-1.5">
                  {row.isArchived ? <Badge tone="quiet">{t('archived')}</Badge> : null}
                  {row.isLowStock ? <Badge tone="warning">{t('reportLowStock')}</Badge> : null}
                  {row.hasRefillGap ? (
                    <Badge tone="danger">
                      {t('reportRefillGap')}: {row.refillGapDays} {t('reportRefillGapDays')}
                    </Badge>
                  ) : null}
                </div>

                <p className="text-sm text-ink-faint">
                  {/* An as-needed-only medication is reported as unforecastable rather
                      than given an invented date. */}
                  {!row.isForecastable || !row.projectedDepletionOn
                    ? t('reportNotForecastable')
                    : `${t('reportDepletion')}: ${day(row.projectedDepletionOn)}${
                        row.daysOfStockRemaining === null
                          ? ''
                          : ` · ${row.daysOfStockRemaining} ${t('reportDaysLeft')}`
                      }`}
                </p>
              </Card>
            ))}
          </ul>
        )}
      </section>

      <Export household={household} />
    </div>
  );
}

/**
 * The share of planned doses that were answered with medication.
 *
 * Rendered from the exact pair the server sends, and absent rather than nought when
 * nothing was scheduled: an as-needed medication has no adherence to report.
 */
function Ratio({ tally }: { tally: AdherenceTally }) {
  const { t } = useLocale();

  if (!tally.onScheduleRatio) {
    return <Badge tone="quiet">{t('reportNoScheduledDoses')}</Badge>;
  }

  const { numerator, denominator } = tally.onScheduleRatio;

  return (
    <p className="text-sm font-semibold">
      <span className="text-xl font-extrabold">{Math.round((numerator / denominator) * 100)}%</span>{' '}
      <span className="text-ink-muted">
        ({numerator}/{denominator})
      </span>
    </p>
  );
}

/** The counts behind the ratio, so the number is always explainable. */
function Counts({ tally }: { tally: AdherenceTally }) {
  const { t } = useLocale();

  const entries: { label: MessageKey; value: number; tone?: 'warning' | 'danger' }[] = [
    { label: 'reportPlanned', value: tally.scheduledDoses },
    { label: 'reportTakenOnSchedule', value: tally.onScheduleDoses },
    { label: 'reportMissed', value: tally.missedDoses, tone: 'warning' },
    { label: 'reportSkipped', value: tally.skipped },
    { label: 'reportPartial', value: tally.partialDoses },
    { label: 'reportExtra', value: tally.extraDoses },
  ];

  return (
    <dl className="m-0 grid grid-cols-2 gap-x-4 gap-y-2 text-sm sm:grid-cols-3">
      {entries.map((entry) => (
        <div key={entry.label} className="flex items-baseline justify-between gap-2 border-b border-line pb-1">
          <dt className="text-ink-muted">{t(entry.label)}</dt>
          <dd
            className={`m-0 font-bold ${
              entry.value > 0 && entry.tone === 'warning' ? 'text-warning' : ''
            }`}
          >
            {entry.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

/**
 * Hands the household its own data back as a file.
 *
 * The blob is fetched rather than linked so a refusal becomes a translated message, and
 * the object URL is revoked once the click has been dispatched so a long session does
 * not accumulate copies of every export.
 */
function Export({ household }: { household: string }) {
  const { t } = useLocale();
  const [state, setState] = useState<'idle' | 'working' | 'done'>('idle');
  const [error, setError] = useState<string | null>(null);

  const download = async () => {
    setState('working');
    setError(null);

    try {
      const { blob, filename } = await api.exportHousehold(household);
      const url = URL.createObjectURL(blob);

      try {
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        link.click();
      } finally {
        URL.revokeObjectURL(url);
      }

      setState('done');
    } catch (caught: unknown) {
      setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
      setState('idle');
    }
  };

  return (
    <Card className="flex flex-col gap-3">
      <h2 className="text-lg font-bold">{t('exportTitle')}</h2>
      <p className="text-sm text-ink-muted">{t('exportDescription')}</p>

      <div className="flex flex-wrap items-center gap-3">
        <Button onClick={download} disabled={state === 'working'}>
          {state === 'working' ? t('exportPreparing') : t('exportButton')}
        </Button>
        {state === 'done' ? <Notice tone="positive">{t('exportDone')}</Notice> : null}
      </div>

      {error ? <Notice tone="danger">{error}</Notice> : null}
      <p className="text-sm text-ink-faint">{t('exportExcludesNotice')}</p>
    </Card>
  );
}
