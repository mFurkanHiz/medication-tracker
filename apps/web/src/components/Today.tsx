'use client';

import { useCallback, useEffect, useState } from 'react';
import { ApiError, api } from '@/lib/api';
import { useNow } from '@/lib/use-now';
import { cautionList, enumKey, errorKey, useLocale } from '@/lib/i18n';
import { formatQuantity, parseQuantity } from '@/lib/quantity';
import type { AllocationDetail, DoseSource, DueDose, Today as TodayModel, Workspace } from '@/lib/types';
import {
  Advanced, Badge, Button, Card, CautionPanel, Dialog, EmptyState, Field, Input, Notice, Select, Spinner,
} from './ui';

/**
 * The daily flow.
 *
 * The default path is one button: the dose comes from the plan and the package comes
 * from the server's consumption policy, so nothing about packages, ledgers or
 * allocations appears unless the user opens the details.
 */
export function Today({ household, workspace, onChanged }: {
  household: string;
  workspace: Workspace;
  onChanged: () => void;
}) {
  const { t } = useLocale();
  const [today, setToday] = useState<TodayModel | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [detail, setDetail] = useState<DueDose | null>(null);
  const [lastRecorded, setLastRecorded] = useState<{ id: string; label: string } | null>(null);
  const [correcting, setCorrecting] = useState<string | null>(null);

  // One clock for the screen rather than one interval per row.
  const now = useNow();

  const load = useCallback(
    () =>
      api
        .today(household)
        .then((value) => {
          setToday(value);
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

  async function record(dose: DueDose, outcome: 'Taken' | 'Skipped') {
    setBusy(dose.planVersionId);
    setError(null);

    try {
      const result = await api.recordDose(household, {
        planVersionId: dose.planVersionId,
        outcome,
        scheduledFor: dose.scheduledFor,
      });

      const drawn = result.allocations
        .map((allocation) =>
          allocation.packageLabel === null
            ? t('looseStock')
            : `${t('packageOrdinal')} ${allocation.packageLabel}`,
        )
        .join(', ');

      setLastRecorded({
        id: result.administrationEventId,
        label: outcome === 'Taken' && drawn ? `${t('takenFrom')} ${drawn}` : t('recordedSkipped'),
      });

      await load();
      onChanged();
    } catch (caught) {
      setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
    } finally {
      setBusy(null);
    }
  }

  if (!today) {
    return error ? <Notice tone="danger">{error}</Notice> : <Spinner label={t('loading')} />;
  }

  return (
    <div className="flex flex-col gap-4">
      {error ? <Notice tone="danger">{error}</Notice> : null}

      {lastRecorded ? (
        <Card className="flex flex-wrap items-center justify-between gap-3 border-positive/30 bg-positive-soft/50">
          <p className="text-sm font-semibold text-positive">{lastRecorded.label}</p>
          <Button variant="secondary" onClick={() => setCorrecting(lastRecorded.id)}>
            {t('correctSource')}
          </Button>
        </Card>
      ) : null}

      {today.due.length === 0 ? (
        <EmptyState title={t('todayEmpty')} hint={t('todayEmptyHint')} />
      ) : (
        <ul className="flex list-none flex-col gap-3 p-0">
          {today.due.map((dose) => (
            <DoseRow
              key={`${dose.planVersionId}-${dose.scheduledFor ?? 'prn'}`}
              dose={dose}
              workspace={workspace}
              now={now}
              busy={busy === dose.planVersionId}
              onTaken={() => void record(dose, 'Taken')}
              onSkipped={() => void record(dose, 'Skipped')}
              onDetails={() => setDetail(dose)}
            />
          ))}
        </ul>
      )}

      {detail ? (
        <AdvancedDoseDialog
          household={household}
          dose={detail}
          onClose={() => setDetail(null)}
          onRecorded={() => {
            setDetail(null);
            load();
            onChanged();
          }}
        />
      ) : null}

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

function DoseRow({ dose, workspace, now, busy, onTaken, onSkipped, onDetails }: {
  dose: DueDose;
  workspace: Workspace;
  /** Milliseconds since the epoch, or null before the screen's clock has started. */
  now: number | null;
  busy: boolean;
  onTaken: () => void;
  onSkipped: () => void;
  onDetails: () => void;
}) {
  const { locale, t } = useLocale();
  const medication = workspace.medications.find((m) => m.id === dose.medicationDefinitionId);
  const person = workspace.people.find((p) => p.id === dose.personId);
  const period = enumKey(dose.dayPeriod);
  const meal = enumKey(dose.mealRelation);
  const recorded = dose.recordedOutcome !== null;

  const clock = (value: string) =>
    new Date(value).toLocaleTimeString(locale === 'tr' ? 'tr-TR' : 'en-GB', { timeStyle: 'short' });

  // The household's own minimum gap. Whether it has passed is decided here rather than on
  // the server, because the answer changes every second and this is where it is read.
  const nextAllowed = dose.nextDoseAllowedFrom;
  const tooSoon =
    now !== null && !recorded && nextAllowed !== null && new Date(nextAllowed).getTime() > now;

  return (
    <Card as="li" className="flex flex-wrap items-center gap-4">
      {/* An as-needed dose always reads "Gerektiğinde" here, even when it carries a
          preferred part of the day. The big label is what the person reads first, and it
          must not turn a preference into an apparent appointment. The preference goes on
          the quiet line below, where it reads as the advice it is. */}
      <p className="min-w-[min(5rem,100%)] text-2xl font-bold text-accent-ink" aria-label={t('exactTime')}>
        {dose.kind === 'AsNeeded'
          ? t('asNeeded')
          : dose.localTime
            ? dose.localTime.slice(0, 5)
            : period
              ? t(period)
              : t('asNeeded')}
      </p>

      <div className="min-w-[min(12rem,100%)] flex-1">
        <h3 className="text-lg font-bold">{medication?.name ?? '—'}</h3>
        <p className="text-sm text-ink-muted">
          {person?.name ?? '—'} · {formatQuantity(dose.dose)} {medication ? unitLabel(medication.unit) : ''}
          {dose.kind === 'AsNeeded' && period ? ` · ${t('preferably')} ${t(period)}` : ''}
          {meal ? ` · ${t(meal)}` : ''}
        </p>
        <div className="mt-1 flex flex-wrap gap-1.5">
          {recorded ? (
            <Badge tone="positive">
              {dose.recordedOutcome === 'Skipped' ? t('recordedSkipped') : t('recordedTaken')}
            </Badge>
          ) : null}
          {!recorded && !dose.hasEnoughStock ? <Badge tone="danger">{t('notEnoughStock')}</Badge> : null}
        </div>
      </div>

      {/* Full width, so it breaks the row and lands between what the dose is and the
          buttons that record it. This is the one screen where somebody is holding the
          box, so the note has to be readable before the tap — not on another page.

          Shown on a recorded dose too. "Do not lie down for half an hour" is at its most
          useful in the minutes after the dose, so hiding it once the row is marked taken
          would remove it exactly when it starts to matter. */}
      {/* "En az ara (dakika)" used to promise a guard that nothing enforced. It now
          says something true: that this is sooner than the household's own note allows,
          and that the app is not going to stop them anyway.

          Not a block, on purpose. The gap is the household's own note, so acting on it is
          not the software reaching a clinical conclusion — but refusing to record a dose
          somebody actually took would make the ledger lie about the one thing it exists
          to remember. Both buttons stay live. */}
      {tooSoon && nextAllowed ? (
        <div className="w-full rounded-xl border border-line bg-warning-soft px-3 py-2">
          <p className="text-sm font-bold text-warning">{t('tooSoon')}</p>
          <p className="mt-0.5 text-sm font-medium text-ink">
            {dose.lastTakenAt ? `${t('lastTakenLabel')} ${clock(dose.lastTakenAt)} · ` : ''}
            {dose.minimumIntervalMinutes !== null
              ? `${t('minimumGapShort')} ${dose.minimumIntervalMinutes} ${t('minutesShort')} · `
              : ''}
            {t('earliestNextLabel')} {clock(nextAllowed)}
          </p>
          <p className="mt-0.5 text-xs text-ink-muted">{t('tooSoonStillRecordable')}</p>
        </div>
      ) : null}

      <div className="w-full">
        <CautionPanel
          title={t('cautionNotes')}
          ownLabel={t('cautionNotesOwn')}
          notes={cautionList(dose.cautions, t)}
        />
      </div>

      <div className="flex w-full flex-wrap gap-2 sm:w-auto">
        {recorded ? null : (
          <>
            <Button onClick={onTaken} disabled={busy}>
              {t('taken')}
            </Button>
            <Button variant="secondary" onClick={onSkipped} disabled={busy}>
              {t('skip')}
            </Button>
          </>
        )}
        <Button variant="quiet" onClick={onDetails}>
          {t('details')}
        </Button>
      </div>
    </Card>
  );
}

/**
 * The advanced path: a different amount, a different outcome, or a named stock source
 * including an untracked one.
 */
function AdvancedDoseDialog({ household, dose, onClose, onRecorded }: {
  household: string;
  dose: DueDose;
  onClose: () => void;
  onRecorded: () => void;
}) {
  const { t } = useLocale();
  const [source, setSource] = useState<DoseSource>('Automatic');
  const [packageId, setPackageId] = useState('');
  const [amount, setAmount] = useState(formatQuantity(dose.dose));
  const [outcome, setOutcome] = useState<'Taken' | 'PartialDose' | 'ExtraDose'>('Taken');
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [packages, setPackages] = useState<{ id: string; label: string }[]>([]);

  useEffect(() => {
    void api
      .workspace(household)
      .then((workspace) => {
        const medication = workspace.medications.find((m) => m.id === dose.medicationDefinitionId);
        setPackages(
          (medication?.packages ?? [])
            .filter((entry) => entry.view.state === 'Sealed' || entry.view.state === 'Opened')
            .map((entry) => ({
              id: entry.view.id,
              label: `${t('packageOrdinal')} ${entry.view.ordinal} — ${formatQuantity(entry.view.remaining)} / ${formatQuantity(entry.view.nominalCapacity)}`,
            })),
        );
      })
      .catch(() => setPackages([]));
  }, [household, dose.medicationDefinitionId, t]);

  async function submit() {
    const parsed = parseQuantity(amount);
    if (!parsed || parsed.numerator <= 0) {
      setError(t('errorGeneric'));
      return;
    }

    if (source === 'SpecificPackage' && packageId === '') {
      setError(t('errorPackageNotFound'));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await api.recordDose(household, {
        // An extra dose belongs to no scheduled slot, so it names person and
        // medication directly rather than claiming the plan's slot.
        ...(outcome === 'ExtraDose'
          ? { personId: dose.personId, medicationDefinitionId: dose.medicationDefinitionId }
          : { planVersionId: dose.planVersionId, scheduledFor: dose.scheduledFor }),
        outcome,
        source,
        packageId: source === 'SpecificPackage' ? packageId : null,
        actualQuantityNumerator: parsed.numerator,
        actualQuantityDenominator: parsed.denominator,
        note: note.trim() === '' ? null : note.trim(),
      });

      onRecorded();
    } catch (caught) {
      setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title={t('recordDose')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('cancel')}
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {t('recordDose')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {error ? <Notice tone="danger">{error}</Notice> : null}

        <Field label={t('amountTaken')} hint={t('amountTakenHint')}>
          {({ id, describedBy }) => (
            <Input id={id} aria-describedby={describedBy} value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="text" />
          )}
        </Field>

        <Field label={t('details')}>
          {({ id }) => (
            <Select id={id} value={outcome} onChange={(e) => setOutcome(e.target.value as typeof outcome)}>
              <option value="Taken">{t('taken')}</option>
              <option value="PartialDose">{t('partialDose')}</option>
              <option value="ExtraDose">{t('extraDose')}</option>
            </Select>
          )}
        </Field>

        <Field label={t('whichPackage')} hint={source === 'Automatic' ? t('sourceAutomaticHint') : undefined}>
          {({ id, describedBy }) => (
            <Select
              id={id}
              aria-describedby={describedBy}
              value={source}
              onChange={(e) => setSource(e.target.value as DoseSource)}
            >
              <option value="Automatic">{t('sourceAutomatic')}</option>
              <option value="SpecificPackage">{t('sourceSpecific')}</option>
              <option value="LooseStock">{t('sourceLoose')}</option>
              <option value="UntrackedExternal">{t('sourceUntracked')}</option>
            </Select>
          )}
        </Field>

        {source === 'SpecificPackage' ? (
          <Field label={t('sourceSpecific')}>
            {({ id }) => (
              <Select id={id} value={packageId} onChange={(e) => setPackageId(e.target.value)}>
                <option value="">—</option>
                {packages.map((entry) => (
                  <option key={entry.id} value={entry.id}>
                    {entry.label}
                  </option>
                ))}
              </Select>
            )}
          </Field>
        ) : null}

        {source === 'UntrackedExternal' ? <Notice>{t('sourceUntrackedHint')}</Notice> : null}

        <Field label={t('note')} optional={t('optional')}>
          {({ id }) => <Input id={id} value={note} onChange={(e) => setNote(e.target.value)} />}
        </Field>
      </div>
    </Dialog>
  );
}

/** "I actually used Box 2." */
export function CorrectionDialog({ household, administrationId, onClose, onCorrected }: {
  household: string;
  administrationId: string;
  onClose: () => void;
  onCorrected: () => void;
}) {
  const { t } = useLocale();
  const [detail, setDetail] = useState<AllocationDetail | null>(null);
  const [packages, setPackages] = useState<{ id: string; label: string }[]>([]);
  const [allocationId, setAllocationId] = useState('');
  const [target, setTarget] = useState<'SpecificPackage' | 'LooseStock'>('SpecificPackage');
  const [packageId, setPackageId] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void (async () => {
      try {
        const loaded = await api.allocations(household, administrationId);
        setDetail(loaded);
        setAllocationId(loaded.allocations.find((a) => a.isActive)?.id ?? '');

        const workspace = await api.workspace(household);
        const owning = workspace.medications.find((medication) =>
          medication.packages.some((entry) =>
            loaded.allocations.some((a) => a.packageId === entry.view.id),
          ),
        );

        setPackages(
          (owning?.packages ?? [])
            .filter((entry) => entry.view.state === 'Sealed' || entry.view.state === 'Opened')
            .map((entry) => ({
              id: entry.view.id,
              label: `${t('packageOrdinal')} ${entry.view.ordinal} — ${formatQuantity(entry.view.remaining)}`,
            })),
        );
      } catch (caught) {
        setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
      }
    })();
  }, [household, administrationId, t]);

  async function submit() {
    if (allocationId === '' || (target === 'SpecificPackage' && packageId === '')) {
      setError(t('errorPackageNotFound'));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await api.correctAllocation(
        household,
        administrationId,
        allocationId,
        target,
        target === 'SpecificPackage' ? packageId : null,
        reason.trim() === '' ? undefined : reason.trim(),
      );
      onCorrected();
    } catch (caught) {
      setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title={t('correctSource')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('cancel')}
          </Button>
          <Button onClick={() => void submit()} disabled={busy || !detail}>
            {t('applyCorrection')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <p className="text-sm text-ink-muted">{t('correctSourceHint')}</p>
        {error ? <Notice tone="danger">{error}</Notice> : null}

        {detail ? (
          <>
            <Field label={t('takenFrom')}>
              {({ id }) => (
                <Select id={id} value={allocationId} onChange={(e) => setAllocationId(e.target.value)}>
                  {detail.allocations
                    .filter((allocation) => allocation.isActive)
                    .map((allocation) => (
                      <option key={allocation.id} value={allocation.id}>
                        {allocation.packageLabel === null
                          ? t('looseStock')
                          : `${t('packageOrdinal')} ${allocation.packageLabel}`}
                        {' — '}
                        {formatQuantity(allocation.quantity)}
                      </option>
                    ))}
                </Select>
              )}
            </Field>

            <Field label={t('whichPackage')}>
              {({ id }) => (
                <Select id={id} value={target} onChange={(e) => setTarget(e.target.value as typeof target)}>
                  <option value="SpecificPackage">{t('sourceSpecific')}</option>
                  <option value="LooseStock">{t('sourceLoose')}</option>
                </Select>
              )}
            </Field>

            {target === 'SpecificPackage' ? (
              <Field label={t('sourceSpecific')}>
                {({ id }) => (
                  <Select id={id} value={packageId} onChange={(e) => setPackageId(e.target.value)}>
                    <option value="">—</option>
                    {packages.map((entry) => (
                      <option key={entry.id} value={entry.id}>
                        {entry.label}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
            ) : null}

            <Field label={t('correctionReason')} optional={t('optional')}>
              {({ id }) => <Input id={id} value={reason} onChange={(e) => setReason(e.target.value)} />}
            </Field>

            {detail.corrections.length > 0 || detail.allocations.some((a) => !a.isActive) ? (
              <Advanced label={t('historyCorrections')}>
                <ul className="flex list-none flex-col gap-2 p-0 text-sm">
                  {detail.corrections.map((correction) => (
                    <li key={correction.id} className="text-ink-muted">
                      {t('correctedFrom')}{' '}
                      {correction.fromPackageLabel === null
                        ? t('looseStock')
                        : `${t('packageOrdinal')} ${correction.fromPackageLabel}`}{' '}
                      →{' '}
                      {correction.toPackageLabel === null
                        ? t('looseStock')
                        : `${t('packageOrdinal')} ${correction.toPackageLabel}`}
                      {correction.reason ? ` · ${correction.reason}` : ''}
                    </li>
                  ))}
                </ul>
              </Advanced>
            ) : null}
          </>
        ) : (
          <Spinner label={t('loading')} />
        )}
      </div>
    </Dialog>
  );
}

/** Lowercases the API's unit name for inline use; the name itself is already a label. */
export function unitLabel(unit: string): string {
  return unit.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
}
