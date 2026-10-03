'use client';

import { useState } from 'react';
import { ApiError, api, type PlanInput } from '@/lib/api';
import { enumKey, errorKey, useLocale, type MessageKey } from '@/lib/i18n';
import { formatQuantity, parseQuantity } from '@/lib/quantity';
import type { TreatmentPlan, Workspace } from '@/lib/types';
import { unitLabel } from './Today';
import { Advanced, Badge, Button, Card, Dialog, EmptyState, Field, Input, Notice, Select } from './ui';

const WEEKDAYS: { bit: number; key: MessageKey }[] = [
  { bit: 0, key: 'monday' },
  { bit: 1, key: 'tuesday' },
  { bit: 2, key: 'wednesday' },
  { bit: 3, key: 'thursday' },
  { bit: 4, key: 'friday' },
  { bit: 5, key: 'saturday' },
  { bit: 6, key: 'sunday' },
];

const DAY_PERIODS = ['Morning', 'Noon', 'Afternoon', 'Evening', 'Night', 'Bedtime'];
// "Aç karnına" and "tok karnına" are the two instructions a Turkish prescription gives
// most often, and they are opposites, so they lead. The three meal-relative ones follow.
const MEAL_RELATIONS = ['Fasting', 'FullStomach', 'BeforeFood', 'WithFood', 'AfterFood'];

/**
 * Who takes which medication, and how.
 *
 * A plan points at a medication definition rather than a package, so it survives every
 * box being replaced. Editing appends a version instead of rewriting the current one.
 */
export function Plans({ household, workspace, onChanged }: {
  household: string;
  workspace: Workspace;
  onChanged: () => void;
}) {
  const { t } = useLocale();
  const [editing, setEditing] = useState<TreatmentPlan | null | 'new'>(null);
  const [error, setError] = useState<string | null>(null);

  const canAdd = workspace.people.length > 0 && workspace.medications.some((m) => !m.isArchived);

  return (
    <div className="flex flex-col gap-4">
      {error ? <Notice tone="danger">{error}</Notice> : null}

      <div className="flex flex-wrap justify-end gap-2">
        <Button onClick={() => setEditing('new')} disabled={!canAdd}>
          {t('addPlan')}
        </Button>
      </div>

      {workspace.plans.length === 0 ? (
        <EmptyState title={t('plansEmpty')} hint={t('plansEmptyHint')} />
      ) : (
        <ul className="flex list-none flex-col gap-3 p-0">
          {workspace.plans.map((plan) => {
            const person = workspace.people.find((p) => p.id === plan.personId);
            const medication = workspace.medications.find((m) => m.id === plan.medicationDefinitionId);
            const period = enumKey(plan.dayPeriod);
            const meal = enumKey(plan.mealRelation);

            return (
              <Card as="li" key={plan.id} className="flex flex-col gap-3">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-48 flex-1">
                    <h3 className="text-lg font-bold">{medication?.name ?? '—'}</h3>
                    <p className="text-sm text-ink-muted">
                      {person?.name ?? '—'} · {formatQuantity(plan.dose)}{' '}
                      {medication ? unitLabel(medication.unit) : ''}
                    </p>
                    <p className="mt-1 text-sm">{describeSchedule(plan, t)}</p>
                  </div>

                  <div className="flex flex-wrap items-center gap-1.5">
                    {/* The same named period means an obligation on a scheduled plan and a
                        preference on an as-needed one. Say which, rather than letting the
                        badge imply a timetable the household never promised. */}
                    {period ? (
                      <Badge tone="accent">
                        {plan.kind === 'AsNeeded' ? `${t('preferably')} ${t(period)}` : t(period)}
                      </Badge>
                    ) : null}
                    {meal ? <Badge>{t(meal)}</Badge> : null}
                    <Badge tone="quiet">v{plan.versionNumber}</Badge>
                  </div>
                </div>

                <div className="flex flex-wrap gap-2">
                  <Button variant="secondary" onClick={() => setEditing(plan)}>
                    {t('editPlan')}
                  </Button>
                  <Button
                    variant="danger"
                    onClick={async () => {
                      try {
                        await api.deletePlan(household, plan.id);
                        onChanged();
                      } catch (caught) {
                        setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
                      }
                    }}
                  >
                    {t('deletePlan')}
                  </Button>
                </div>
              </Card>
            );
          })}
        </ul>
      )}

      {editing ? (
        <PlanDialog
          household={household}
          workspace={workspace}
          plan={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            onChanged();
          }}
        />
      ) : null}
    </div>
  );
}

function describeSchedule(plan: TreatmentPlan, t: (key: MessageKey) => string): string {
  if (plan.kind === 'AsNeeded') {
    return t('scheduleAsNeeded');
  }

  const time = plan.localTime ? ` · ${plan.localTime.slice(0, 5)}` : '';

  if (plan.pattern === 'SelectedWeekdays' && plan.weekdayMask !== null) {
    const days = WEEKDAYS.filter(({ bit }) => (plan.weekdayMask! & (1 << bit)) !== 0)
      .map(({ key }) => t(key))
      .join(', ');
    return `${days}${time}`;
  }

  if (plan.pattern === 'EveryNDays' && plan.intervalDays !== null) {
    return `${t('scheduleInterval')}: ${plan.intervalDays}${time}`;
  }

  return `${t('scheduleDaily')}${time}`;
}

function PlanDialog({ household, workspace, plan, onClose, onSaved }: {
  household: string;
  workspace: Workspace;
  plan: TreatmentPlan | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useLocale();
  const people = workspace.people.filter((person) => !person.isArchived);
  const medications = workspace.medications.filter((medication) => !medication.isArchived);

  const [personId, setPersonId] = useState(plan?.personId ?? people[0]?.id ?? '');
  const [medicationId, setMedicationId] = useState(plan?.medicationDefinitionId ?? medications[0]?.id ?? '');
  const [dose, setDose] = useState(plan ? formatQuantity(plan.dose) : '1');
  const [kind, setKind] = useState(plan?.kind ?? 'Scheduled');
  const [pattern, setPattern] = useState(plan?.pattern ?? 'Daily');
  const [weekdayMask, setWeekdayMask] = useState(plan?.weekdayMask ?? 0);
  const [intervalDays, setIntervalDays] = useState(plan?.intervalDays?.toString() ?? '2');
  const [localTime, setLocalTime] = useState(plan?.localTime?.slice(0, 5) ?? '08:00');
  const [dayPeriod, setDayPeriod] = useState(plan?.dayPeriod ?? '');
  const [mealRelation, setMealRelation] = useState(plan?.mealRelation ?? '');
  const [minimumInterval, setMinimumInterval] = useState(plan?.minimumIntervalMinutes?.toString() ?? '');
  const [effectiveFrom, setEffectiveFrom] = useState(
    plan?.effectiveFrom ?? new Date().toISOString().slice(0, 10),
  );
  const [effectiveTo, setEffectiveTo] = useState(plan?.effectiveTo ?? '');
  const [instructions, setInstructions] = useState(plan?.instructions ?? '');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    const parsedDose = parseQuantity(dose);
    if (!parsedDose || parsedDose.numerator <= 0 || personId === '' || medicationId === '') {
      setError(t('errorGeneric'));
      return;
    }

    if (pattern === 'SelectedWeekdays' && weekdayMask === 0) {
      setError(t('errorGeneric'));
      return;
    }

    const input: PlanInput = {
      personId,
      medicationDefinitionId: medicationId,
      doseNumerator: parsedDose.numerator,
      doseDenominator: parsedDose.denominator,
      // The plan's own local days and times are expressed in the viewer's zone.
      timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
      kind,
      pattern: kind === 'AsNeeded' ? 'Daily' : pattern,
      weekdayMask: kind === 'Scheduled' && pattern === 'SelectedWeekdays' ? weekdayMask : null,
      intervalDays:
        kind === 'Scheduled' && pattern === 'EveryNDays' ? Number.parseInt(intervalDays, 10) : null,
      effectiveFrom: effectiveFrom === '' ? null : effectiveFrom,
      effectiveTo: effectiveTo === '' ? null : effectiveTo,
      // An exact clock belongs to a schedule. An as-needed dose has no clock — but it can
      // still prefer a part of the day, and that preference is recorded, not discarded.
      localTime: kind === 'Scheduled' && dayPeriod === '' ? `${localTime}:00` : null,
      dayPeriod: dayPeriod === '' ? null : dayPeriod,
      mealRelation: mealRelation === '' ? null : mealRelation,
      minimumIntervalMinutes: minimumInterval === '' ? null : Number.parseInt(minimumInterval, 10),
      instructions: instructions.trim() || null,
    };

    setBusy(true);
    setError(null);

    try {
      if (plan) {
        await api.updatePlan(household, plan.id, input);
      } else {
        await api.createPlan(household, input);
      }
      onSaved();
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
      title={plan ? t('editPlan') : t('addPlan')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('cancel')}
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {t('save')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {error ? <Notice tone="danger">{error}</Notice> : null}
        {plan ? <Notice tone="positive">{t('planVersionNote')}</Notice> : null}

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('person')}>
            {({ id }) => (
              <Select id={id} value={personId} onChange={(e) => setPersonId(e.target.value)}>
                {people.map((person) => (
                  <option key={person.id} value={person.id}>
                    {person.name}
                  </option>
                ))}
              </Select>
            )}
          </Field>

          <Field label={t('medication')}>
            {({ id }) => (
              <Select id={id} value={medicationId} onChange={(e) => setMedicationId(e.target.value)}>
                {medications.map((medication) => (
                  <option key={medication.id} value={medication.id}>
                    {medication.name}
                  </option>
                ))}
              </Select>
            )}
          </Field>

          <Field label={t('dose')}>
            {({ id }) => <Input id={id} value={dose} onChange={(e) => setDose(e.target.value)} />}
          </Field>

          <Field label={t('schedule')}>
            {({ id }) => (
              <Select
                id={id}
                value={kind === 'AsNeeded' ? 'AsNeeded' : pattern}
                onChange={(e) => {
                  if (e.target.value === 'AsNeeded') {
                    setKind('AsNeeded');
                  } else {
                    setKind('Scheduled');
                    setPattern(e.target.value as typeof pattern);
                  }
                }}
              >
                <option value="Daily">{t('scheduleDaily')}</option>
                <option value="SelectedWeekdays">{t('scheduleWeekdays')}</option>
                <option value="EveryNDays">{t('scheduleInterval')}</option>
                <option value="AsNeeded">{t('scheduleAsNeeded')}</option>
              </Select>
            )}
          </Field>
        </div>

        {kind === 'Scheduled' && pattern === 'SelectedWeekdays' ? (
          <fieldset className="rounded-xl border border-line p-3">
            <legend className="px-1 text-sm font-semibold">{t('scheduleWeekdays')}</legend>
            <div className="flex flex-wrap gap-2">
              {WEEKDAYS.map(({ bit, key }) => {
                const selected = (weekdayMask & (1 << bit)) !== 0;
                return (
                  <label key={bit} className="flex items-center gap-1.5 text-sm">
                    <input
                      type="checkbox"
                      className="size-5 min-h-0"
                      checked={selected}
                      onChange={() => setWeekdayMask(weekdayMask ^ (1 << bit))}
                    />
                    {t(key)}
                  </label>
                );
              })}
            </div>
          </fieldset>
        ) : null}

        {kind === 'Scheduled' && pattern === 'EveryNDays' ? (
          <Field label={t('intervalDays')}>
            {({ id }) => (
              <Input
                id={id}
                type="number"
                min={1}
                max={3650}
                value={intervalDays}
                onChange={(e) => setIntervalDays(e.target.value)}
              />
            )}
          </Field>
        ) : null}

        {/* A dose is pinned to a clock time or to a named part of the day, never both —
            that is the domain rule for a SCHEDULE. An as-needed dose has no schedule, but
            that never meant it has nothing to say about timing: "take it on a full stomach,
            preferably in the evening" is ordinary medical advice, and the owner was right
            that the form refused to record it. The domain and the database never forbade
            it — only this form did. So both kinds ask about timing now; only the scheduled
            one treats the answer as an obligation. */}
        {kind === 'Scheduled' ? (
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('whenInDay')}>
              {({ id }) => (
                <Select id={id} value={dayPeriod} onChange={(e) => setDayPeriod(e.target.value)}>
                  <option value="">{t('useExactTime')}</option>
                  {DAY_PERIODS.map((period) => {
                    const key = enumKey(period);
                    return (
                      <option key={period} value={period}>
                        {key ? t(key) : period}
                      </option>
                    );
                  })}
                </Select>
              )}
            </Field>

            {dayPeriod === '' ? (
              <Field label={t('exactTime')}>
                {({ id }) => (
                  <Input
                    id={id}
                    type="time"
                    value={localTime}
                    onChange={(e) => setLocalTime(e.target.value)}
                  />
                )}
              </Field>
            ) : null}
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            {/* No clock here on purpose: a dose fixed to a time is a scheduled dose, and
                that is the other option in the list above. A preference is not a time. */}
            <Field label={t('preferredTime')} hint={t('preferredTimeHint')} optional={t('optional')}>
              {({ id, describedBy }) => (
                <Select
                  id={id}
                  aria-describedby={describedBy}
                  value={dayPeriod}
                  onChange={(e) => setDayPeriod(e.target.value)}
                >
                  <option value="">{t('noTimePreference')}</option>
                  {DAY_PERIODS.map((period) => {
                    const key = enumKey(period);
                    return (
                      <option key={period} value={period}>
                        {key ? t(key) : period}
                      </option>
                    );
                  })}
                </Select>
              )}
            </Field>

            <p className="rounded-lg bg-surface-sunken px-3 py-2 text-sm text-ink-muted">
              {t('asNeededExplainer')}
            </p>
          </div>
        )}

        {/* Food timing is guidance for both kinds, and it is the thing a person most often
            needs to remember while holding the box, so it does not belong behind a
            disclosure. It is recorded as written and never shifts a scheduled time. */}
        <Field label={t('mealRelation')} hint={t('mealRelationHint')} optional={t('optional')}>
          {({ id, describedBy }) => (
            <Select
              id={id}
              aria-describedby={describedBy}
              value={mealRelation}
              onChange={(e) => setMealRelation(e.target.value)}
            >
              <option value="">{t('none')}</option>
              {MEAL_RELATIONS.map((relation) => {
                const key = enumKey(relation);
                return (
                  <option key={relation} value={relation}>
                    {key ? t(key) : relation}
                  </option>
                );
              })}
            </Select>
          )}
        </Field>

        <Advanced label={t('advancedOptions')}>
          <div className="flex flex-col gap-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label={t('effectiveFrom')}>
                {({ id }) => (
                  <Input id={id} type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} />
                )}
              </Field>
              <Field label={t('effectiveTo')} optional={t('optional')}>
                {({ id }) => (
                  <Input id={id} type="date" value={effectiveTo} onChange={(e) => setEffectiveTo(e.target.value)} />
                )}
              </Field>
            </div>

            <Field label={t('minimumInterval')} optional={t('optional')}>
              {({ id }) => (
                <Input
                  id={id}
                  type="number"
                  min={0}
                  value={minimumInterval}
                  onChange={(e) => setMinimumInterval(e.target.value)}
                />
              )}
            </Field>

            <Field label={t('instructions')} optional={t('optional')}>
              {({ id }) => <Input id={id} value={instructions} onChange={(e) => setInstructions(e.target.value)} />}
            </Field>
          </div>
        </Advanced>
      </div>
    </Dialog>
  );
}
