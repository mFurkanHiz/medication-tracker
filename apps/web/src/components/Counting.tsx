'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError, api, type CountLineInput } from '@/lib/api';
import { errorKey, useLocale } from '@/lib/i18n';
import { formatQuantity, parseQuantity } from '@/lib/quantity';
import type { CountSession, MedicationDefinition, Workspace } from '@/lib/types';
import { Advanced, Badge, Button, Card, EmptyState, Field, Input, Notice, Spinner, Textarea } from './ui';

/** What the user typed, keyed by medication or by `medication:package`. */
type Entries = Record<string, string>;

const packageKey = (definitionId: string, packageId: string) => `${definitionId}:${packageId}`;

/**
 * Counting what is physically there, and correcting a count that turned out wrong.
 *
 * The everyday path is one number per medication. Counting individual boxes is real
 * reconciliation work and lives under an advanced disclosure, because a household that
 * just wants to say "there are twelve left" should not have to open the cupboard and
 * account for each box.
 *
 * Nothing here edits an accepted count. A correction is appended as a revision, so the
 * original stays readable exactly as it was accepted — acceptance row 18.
 */
export function Counting({ household, workspace, onChanged }: {
  household: string;
  workspace: Workspace;
  onChanged: () => void;
}) {
  const { t, locale } = useLocale();
  const [entries, setEntries] = useState<Entries>({});
  const [byPackage, setByPackage] = useState<Record<string, boolean>>({});
  const [note, setNote] = useState('');
  const [revising, setRevising] = useState<CountSession | null>(null);
  const [sessions, setSessions] = useState<CountSession[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [accepted, setAccepted] = useState(false);

  const load = useCallback(
    () =>
      api
        .countSessions(household)
        .then((value) => {
          setSessions(value.sessions);
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

  // An archived medication is not something the household still keeps on a shelf.
  const countable = useMemo(
    () => workspace.medications.filter((medication) => !medication.isArchived),
    [workspace.medications],
  );

  const medicationName = (id: string) =>
    workspace.medications.find((medication) => medication.id === id)?.name ?? '—';

  const moment = (value: string) =>
    new Date(value).toLocaleString(locale === 'tr' ? 'tr-TR' : 'en-GB', {
      dateStyle: 'medium',
      timeStyle: 'short',
    });

  const reset = () => {
    setEntries({});
    setByPackage({});
    setNote('');
    setRevising(null);
  };

  /**
   * Turns what was typed into exact lines, refusing the whole submission if any single
   * entry cannot be read. A count that silently dropped one unreadable row would write
   * a reconciliation the user did not agree to.
   */
  const buildLines = (): CountLineInput[] | 'invalid' => {
    const lines: CountLineInput[] = [];

    for (const [key, raw] of Object.entries(entries)) {
      if (raw.trim() === '') {
        continue;
      }

      const parsed = parseQuantity(raw);
      if (!parsed || parsed.numerator < 0) {
        return 'invalid';
      }

      const [definitionId, packageId] = key.split(':');
      lines.push({
        medicationDefinitionId: definitionId,
        observedNumerator: parsed.numerator,
        observedDenominator: parsed.denominator,
        ...(packageId ? { packageId } : {}),
      });
    }

    return lines;
  };

  const submit = async () => {
    const lines = buildLines();

    if (lines === 'invalid') {
      setError(t('countingInvalidAmount'));
      return;
    }

    if (lines.length === 0) {
      setError(t('countingNothingEntered'));
      return;
    }

    setBusy(true);
    setError(null);
    setAccepted(false);

    // Committed before the request so a retry after a dropped response replays the
    // same command rather than recording the count twice.
    const idempotencyKey = crypto.randomUUID();
    const trimmed = note.trim() === '' ? undefined : note.trim();

    try {
      if (revising) {
        await api.reviseCount(household, revising.id, idempotencyKey, lines, trimmed);
      } else {
        await api.countStock(household, idempotencyKey, lines, trimmed);
      }

      reset();
      setAccepted(true);
      await load();
      onChanged();
    } catch (caught: unknown) {
      setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));

      // A stale revision means somebody else already corrected this count. Reloading
      // puts the newest link in front of the user instead of leaving them retrying.
      if (caught instanceof ApiError && caught.code === 'stale_revision') {
        setRevising(null);
        await load();
      }
    } finally {
      setBusy(false);
    }
  };

  const startCorrection = (session: CountSession) => {
    setRevising(session);
    setAccepted(false);
    setError(null);
    setNote('');

    // Prefilled with what was recorded, because a correction is usually a small change
    // to it rather than a fresh count.
    const prefilled: Entries = {};
    const packaged: Record<string, boolean> = {};

    for (const line of session.lines) {
      if (line.packageId) {
        prefilled[packageKey(line.medicationDefinitionId, line.packageId)] = line.observed.display;
        packaged[line.medicationDefinitionId] = true;
      } else {
        prefilled[line.medicationDefinitionId] = line.observed.display;
      }
    }

    setEntries(prefilled);
    setByPackage(packaged);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  if (countable.length === 0) {
    return <EmptyState title={t('countingEmpty')} hint={t('countingEmptyHint')} />;
  }

  return (
    <div className="flex flex-col gap-8">
      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-bold">{t('countingTitle')}</h2>
        <p className="text-sm text-ink-muted">{t('countingDescription')}</p>

        {revising ? (
          <Notice tone="warning">
            {t('countingCorrecting')} · {moment(revising.acceptedAt)} —{' '}
            {t('countingCorrectingHint')}
          </Notice>
        ) : null}

        {error ? <Notice tone="danger">{error}</Notice> : null}
        {accepted ? <Notice tone="positive">{t('countingAccepted')}</Notice> : null}

        <ul className="flex list-none flex-col gap-2 p-0">
          {countable.map((medication) => (
            <CountRow
              key={medication.id}
              medication={medication}
              entries={entries}
              onEntry={(key, value) => setEntries((current) => ({ ...current, [key]: value }))}
              byPackage={byPackage[medication.id] === true}
              onByPackage={(value) => {
                setByPackage((current) => ({ ...current, [medication.id]: value }));

                // Switching mode clears the other mode's entries so a hidden number
                // cannot be submitted.
                setEntries((current) => {
                  const next = { ...current };
                  delete next[medication.id];
                  for (const entry of medication.packages) {
                    delete next[packageKey(medication.id, entry.view.id)];
                  }
                  return next;
                });
              }}
            />
          ))}
        </ul>

        <Field label={t('countingNote')} optional={t('countingNoteOptional')}>
          {({ id }) => (
            <Textarea
              id={id}
              rows={2}
              value={note}
              onChange={(event) => setNote(event.target.value)}
            />
          )}
        </Field>

        <div className="flex flex-wrap gap-2">
          <Button onClick={submit} disabled={busy}>
            {busy ? t('countingSubmitting') : t('countingSubmit')}
          </Button>
          {revising ? (
            <Button variant="secondary" onClick={reset} disabled={busy}>
              {t('countingCancelCorrection')}
            </Button>
          ) : null}
        </div>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-bold">{t('countingHistory')}</h2>

        {sessions === null ? (
          <Spinner label={t('loading')} />
        ) : sessions.length === 0 ? (
          <EmptyState title={t('countingHistoryEmpty')} />
        ) : (
          <ul className="flex list-none flex-col gap-2 p-0">
            {sessions.map((session) => (
              <Card as="li" key={session.id} className="flex flex-col gap-3">
                <div className="flex flex-wrap items-center gap-2">
                  <time className="text-sm font-semibold">{moment(session.acceptedAt)}</time>
                  {session.revisionNumber > 1 ? (
                    <Badge tone="accent">
                      {t('countingRevision')} · {session.revisionNumber}. {t('countingRevisionNumber')}
                    </Badge>
                  ) : null}
                  {!session.isRevisable ? (
                    <Badge tone="quiet">{t('countingSuperseded')}</Badge>
                  ) : null}

                  <span className="flex-1" />

                  {session.isRevisable ? (
                    <Button variant="quiet" onClick={() => startCorrection(session)}>
                      {t('countingCorrect')}
                    </Button>
                  ) : null}
                </div>

                <ul className="flex list-none flex-col gap-1 p-0 text-sm">
                  {session.lines.map((line) => (
                    <li key={line.id} className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                      <span className="font-semibold">
                        {medicationName(line.medicationDefinitionId)}
                      </span>
                      {line.packageLabel !== null ? (
                        <span className="text-ink-muted">
                          {t('packageOrdinal')} {line.packageLabel}
                        </span>
                      ) : null}
                      <span className="text-ink-muted">
                        {formatQuantity(line.before)} → {formatQuantity(line.observed)}
                      </span>
                      <Delta line={line} />
                    </li>
                  ))}
                </ul>
              </Card>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

/**
 * A count that matched is still a count.
 *
 * It is shown as "matched" rather than hidden or rendered as a bare zero, because
 * "we checked this and it was right" is information the household and the audit trail
 * both want.
 */
function Delta({ line }: { line: { adjustment: { numerator: number; denominator: number; display: string } } }) {
  const { t } = useLocale();

  if (line.adjustment.numerator === 0) {
    return <Badge tone="positive">{t('countingMatched')}</Badge>;
  }

  const positive = line.adjustment.numerator > 0;

  return (
    <span className={`font-bold ${positive ? 'text-positive' : 'text-warning'}`}>
      {t('countingDelta')}: {positive ? '+' : ''}
      {formatQuantity(line.adjustment)}
    </span>
  );
}

/** One medication: a single number by default, or one per box under advanced. */
function CountRow({ medication, entries, onEntry, byPackage, onByPackage }: {
  medication: MedicationDefinition;
  entries: Entries;
  onEntry: (key: string, value: string) => void;
  byPackage: boolean;
  onByPackage: (value: boolean) => void;
}) {
  const { t } = useLocale();

  // A retired box holds no stock to reconcile.
  const available = medication.packages.filter(
    (entry) => entry.view.state === 'Sealed' || entry.view.state === 'Opened',
  );

  return (
    <Card as="li" className="flex flex-col gap-3">
      {/* Top-aligned: the field carries a hint underneath, so aligning on the bottom
          edge would push the medication's name down away from its own card. */}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-[min(12rem,100%)] flex-1">
          <p className="font-semibold">
            {medication.name}
            {medication.strength ? (
              <span className="text-ink-muted"> · {medication.strength}</span>
            ) : null}
          </p>
          <p className="text-sm text-ink-muted">
            {t('countingExpected')}: <strong>{formatQuantity(medication.total)}</strong>
          </p>
        </div>

        {!byPackage ? (
          <Field label={t('countingObserved')} hint={t('countingObservedHint')} className="w-40">
            {({ id, describedBy }) => (
              <Input
                id={id}
                aria-describedby={describedBy}
                inputMode="decimal"
                value={entries[medication.id] ?? ''}
                onChange={(event) => onEntry(medication.id, event.target.value)}
              />
            )}
          </Field>
        ) : null}
      </div>

      {available.length === 0 ? null : byPackage ? (
        // Once the user is counting boxes, the boxes are the task — not an advanced
        // detail to go looking for. Keeping them inside a closed disclosure would also
        // hide the values a correction prefills, leaving a card with no visible input.
        <div className="flex flex-col gap-3">
          <ul className="flex list-none flex-wrap gap-3 p-0">
            {available.map((entry) => (
              <li key={entry.view.id}>
                {/* Labelled by its own box, so three inputs are not three
                    identically-named fields to anyone reading the page aloud. */}
                <Field
                  label={`${t('packageOrdinal')} ${entry.view.ordinal}`}
                  hint={`${t('countingExpected')}: ${formatQuantity(entry.view.remaining)}`}
                  className="w-36"
                >
                  {({ id, describedBy }) => (
                    <Input
                      id={id}
                      aria-describedby={describedBy}
                      inputMode="decimal"
                      value={entries[packageKey(medication.id, entry.view.id)] ?? ''}
                      onChange={(event) =>
                        onEntry(packageKey(medication.id, entry.view.id), event.target.value)
                      }
                    />
                  )}
                </Field>
              </li>
            ))}
          </ul>

          <Button variant="secondary" aria-pressed onClick={() => onByPackage(false)}>
            {t('countingWholeMedication')}
          </Button>
        </div>
      ) : (
        // The disclosure keeps one stable label describing what is inside. Naming it
        // after the action would make the summary and the button below read the same
        // while doing different things.
        <Advanced label={t('countingAdvanced')}>
          <div className="flex flex-col gap-3">
            <p className="text-sm text-ink-muted">{t('countingByPackageHint')}</p>

            <Button
              variant="secondary"
              aria-pressed={false}
              onClick={() => onByPackage(true)}
            >
              {t('countingByPackage')}
            </Button>
          </div>
        </Advanced>
      )}
    </Card>
  );
}
