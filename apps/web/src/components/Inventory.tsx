'use client';

import { useEffect, useState } from 'react';
import { ApiError, api, type AddStockInput, type MedicationDefinitionInput } from '@/lib/api';
import { cautionList, enumKey, errorKey, useLocale } from '@/lib/i18n';
import { addQuantities, formatQuantity, parseQuantity, type Quantity } from '@/lib/quantity';
import type {
  Forecast, MedicationDefinition, MedicationPackage, Person, TreatmentPlan, Workspace,
} from '@/lib/types';
import { unitLabel } from './Today';
import {
  Advanced, Badge, Button, Card, CautionPanel, Dialog, EmptyState, Field, Input, Notice, Select, Spinner,
  Textarea,
} from './ui';

const FORMS = [
  'Tablet', 'Capsule', 'OralLiquid', 'Drops', 'Sachet', 'Suppository', 'Injection',
  'Cream', 'Ointment', 'Gel', 'Patch', 'InhalerSpray', 'NasalSpray', 'EyeDrops', 'EarDrops', 'Other',
];

/**
 * Medications and their physical stock.
 *
 * The list shows a total and a package count. Individual boxes, their lots and expiry
 * dates, and every stock action live behind a disclosure, so the common case of
 * "how much do we have" stays a single line.
 */
export function Inventory({ household, workspace, onChanged }: {
  household: string;
  workspace: Workspace;
  onChanged: () => void;
}) {
  const { t } = useLocale();
  const [editing, setEditing] = useState<MedicationDefinition | null | 'new'>(null);
  const [addingStockTo, setAddingStockTo] = useState<MedicationDefinition | null>(null);
  const [refillFor, setRefillFor] = useState<MedicationDefinition | null>(null);
  const [error, setError] = useState<string | null>(null);

  const active = workspace.medications.filter((medication) => !medication.isArchived);
  const archived = workspace.medications.filter((medication) => medication.isArchived);

  return (
    <div className="flex flex-col gap-4">
      {error ? <Notice tone="danger">{error}</Notice> : null}

      <div className="flex flex-wrap justify-end gap-2">
        <Button onClick={() => setEditing('new')}>{t('addMedication')}</Button>
      </div>

      {active.length === 0 ? (
        <EmptyState
          title={t('inventoryEmpty')}
          hint={t('inventoryEmptyHint')}
          action={<Button onClick={() => setEditing('new')}>{t('addMedication')}</Button>}
        />
      ) : (
        <ul className="flex list-none flex-col gap-3 p-0">
          {active.map((medication) => (
            <MedicationRow
              key={medication.id}
              household={household}
              medication={medication}
              activePlanCount={
                workspace.plans.filter((plan) => plan.medicationDefinitionId === medication.id).length
              }
              pausedPlans={workspace.plans.filter(
                (plan) => plan.medicationDefinitionId === medication.id && plan.isPaused,
              )}
              people={workspace.people}
              onEdit={() => setEditing(medication)}
              onAddStock={() => setAddingStockTo(medication)}
              onRefill={() => setRefillFor(medication)}
              onChanged={onChanged}
              onError={setError}
            />
          ))}
        </ul>
      )}

      {archived.length > 0 ? (
        <Advanced label={`${t('archived')} (${archived.length})`}>
          <ul className="flex list-none flex-col gap-2 p-0">
            {archived.map((medication) => (
              <li key={medication.id} className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-semibold">{medication.name}</span>
                <Button
                  variant="secondary"
                  onClick={async () => {
                    try {
                      await api.restoreDefinition(household, medication.id);
                      onChanged();
                    } catch (caught) {
                      setError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
                    }
                  }}
                >
                  {t('restoreMedication')}
                </Button>
              </li>
            ))}
          </ul>
        </Advanced>
      ) : null}

      {editing ? (
        <DefinitionDialog
          household={household}
          definition={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            onChanged();
          }}
        />
      ) : null}

      {addingStockTo ? (
        <AddStockDialog
          household={household}
          definition={addingStockTo}
          people={workspace.people}
          onClose={() => setAddingStockTo(null)}
          onSaved={() => {
            setAddingStockTo(null);
            onChanged();
          }}
        />
      ) : null}

      {refillFor ? (
        <RefillDialog
          household={household}
          definition={refillFor}
          onClose={() => setRefillFor(null)}
          onSaved={() => {
            setRefillFor(null);
            onChanged();
          }}
        />
      ) : null}
    </div>
  );
}

function MedicationRow({
  household, medication, people, activePlanCount, pausedPlans,
  onEdit, onAddStock, onRefill, onChanged, onError,
}: {
  household: string;
  medication: MedicationDefinition;
  people: Person[];
  /** How many standing plans archiving would stop. The user is told before, not after. */
  activePlanCount: number;
  /** Plans for this medication the household has set aside, resumable from right here. */
  pausedPlans: TreatmentPlan[];
  onEdit: () => void;
  onAddStock: () => void;
  onRefill: () => void;
  onChanged: () => void;
  onError: (message: string) => void;
}) {
  const { t } = useLocale();
  const [forecast, setForecast] = useState<Forecast | null>(null);
  const [confirmingArchive, setConfirmingArchive] = useState(false);
  const [archiving, setArchiving] = useState(false);

  useEffect(() => {
    void api
      .forecast(household, medication.id)
      .then(setForecast)
      .catch(() => setForecast(null));
  }, [household, medication.id, medication.total.display]);

  const visible = medication.packages.filter(
    (entry) => entry.view.state === 'Sealed' || entry.view.state === 'Opened',
  );

  // Retired boxes are listed too, so the count alone would hide them: a household that
  // loses its only box reads "Kutuları göster (0)" and has no reason to open the panel —
  // which is exactly where the button that brings it back lives.
  const retired = medication.packages.length - visible.length;

  return (
    <Card as="li" className="flex flex-col gap-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-48 flex-1">
          <h3 className="text-lg font-bold">{medication.name}</h3>
          <p className="text-sm text-ink-muted">
            {[medication.strength, medication.brand].filter(Boolean).join(' · ') || unitLabel(medication.form)}
          </p>
        </div>

        {/* The headline number: everything the household has, and how many boxes. */}
        <div className="text-right">
          <p className="text-2xl font-bold">
            {formatQuantity(medication.total)}{' '}
            <span className="text-base font-normal text-ink-muted">{unitLabel(medication.unit)}</span>
          </p>
          <p className="text-sm text-ink-muted">
            {visible.length} {t('packagesLabel')}
          </p>
        </div>
      </div>

      {/* The owner asked for this plainly: "uyarı notları kolay görünebilir yerde
          konumlansın". So it sits on the medicine's own card, open, above everything
          else here — not behind a disclosure, and not below the stock figure. A note
          you have to go looking for is a note you read after the dose. */}
      <CautionPanel
        title={t('cautionNotes')}
        ownLabel={t('cautionNotesOwn')}
        notes={cautionList(medication.cautions, t)}
      />

      {forecast ? <ForecastBanner forecast={forecast} /> : null}

      {/* The owner asked to be able to start again "ilacın sayfasından" — from the
          medication's own page — and he is right that this is where you look. You go to
          the medicine when you pick it back up, not to a list of plans. The forecast
          banner stays above it: a paused plan is no reason to hide that the box is empty. */}
      {pausedPlans.length > 0 ? (
        <div className="flex flex-col gap-2 rounded-xl border border-line bg-surface-sunken px-3 py-3">
          <p className="text-sm text-ink-muted">{t('medicationHasPausedPlan')}</p>
          <div className="flex flex-wrap gap-2">
            {pausedPlans.map((plan) => {
              const person = people.find((candidate) => candidate.id === plan.personId);
              return (
                <Button
                  key={plan.id}
                  variant="secondary"
                  onClick={async () => {
                    try {
                      await api.setPlanPaused(household, plan.id, false);
                      onChanged();
                    } catch (caught) {
                      onError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
                    }
                  }}
                >
                  {t('resumePlan')}{person ? ` — ${person.name}` : ''}
                </Button>
              );
            })}
          </div>
        </div>
      ) : null}

      <div className="flex flex-wrap gap-2">
        <Button variant="secondary" onClick={onAddStock}>
          {t('addStock')}
        </Button>
        <Button variant="quiet" onClick={onEdit}>
          {t('editMedication')}
        </Button>
        <Button variant="quiet" onClick={onRefill}>
          {t('refillSettings')}
        </Button>
      </div>

      <Advanced
        label={
          retired > 0
            ? `${t('showPackages')} (${visible.length} · ${retired} ${t('retiredPackages')})`
            : `${t('showPackages')} (${visible.length})`
        }
      >
        <ul className="flex list-none flex-col gap-2 p-0">
          {medication.packages.map((entry) => (
            <PackageRow
              key={entry.view.id}
              household={household}
              pkg={entry.view}
              activeLoanId={entry.activeLoanId}
              people={people}
              onChanged={onChanged}
              onError={onError}
            />
          ))}
          {medication.packages.length === 0 ? (
            <li className="text-sm text-ink-muted">{t('none')}</li>
          ) : null}
        </ul>

        {medication.loose.numerator !== 0 ? (
          <p className="mt-3 rounded-lg bg-surface-sunken px-3 py-2 text-sm">
            {t('looseStock')}: <strong>{formatQuantity(medication.loose)}</strong>
          </p>
        ) : null}

        {/* Archiving used to fire on the first click, from inside the package panel, next
            to the per-box Lost/Disposed buttons — so it read as "archive this box" while it
            actually stopped every standing plan for the whole household, irreversibly. It
            now says what it will do, and names the medication, before it does it. */}
        <div className="mt-3 flex flex-wrap gap-2">
          <Button variant="danger" onClick={() => setConfirmingArchive(true)}>
            {t('archiveMedication')}
          </Button>
        </div>
      </Advanced>

      {confirmingArchive ? (
        <Dialog
          open
          onClose={() => setConfirmingArchive(false)}
          title={`${t('archiveMedication')} — ${medication.name}`}
          footer={
            <>
              <Button variant="secondary" onClick={() => setConfirmingArchive(false)}>
                {t('cancel')}
              </Button>
              <Button
                variant="danger"
                disabled={archiving}
                onClick={async () => {
                  setArchiving(true);
                  try {
                    await api.archiveDefinition(household, medication.id);
                    setConfirmingArchive(false);
                    onChanged();
                  } catch (caught) {
                    onError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
                  } finally {
                    setArchiving(false);
                  }
                }}
              >
                {t('archiveMedication')}
              </Button>
            </>
          }
        >
          <div className="flex flex-col gap-3 text-sm">
            <p>{t('archiveKeeps')}</p>

            {/* No longer a danger notice, because it is no longer destructive: archiving
                pauses the plans instead of deleting them, so the only thing the household
                loses is the doses it had already decided to stop taking. */}
            {activePlanCount > 0 ? (
              <Notice tone="warning">
                {t('archiveStopsPlansBefore')} {activePlanCount} {t('archiveStopsPlansAfter')}
              </Notice>
            ) : (
              <p className="text-ink-muted">{t('archiveNoPlans')}</p>
            )}

            <p className="text-ink-muted">{t('archiveReversible')}</p>
          </div>
        </Dialog>
      ) : null}
    </Card>
  );
}

function PackageRow({ household, pkg, activeLoanId, people, onChanged, onError }: {
  household: string;
  pkg: MedicationPackage;
  activeLoanId: string | null;
  people: Person[];
  onChanged: () => void;
  onError: (message: string) => void;
}) {
  const { t } = useLocale();
  const [busy, setBusy] = useState(false);
  const stateKey = enumKey(pkg.state);

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    try {
      await action();
      onChanged();
    } catch (caught) {
      onError(t(caught instanceof ApiError ? errorKey(caught.code) : 'errorNetwork'));
    } finally {
      setBusy(false);
    }
  }

  const holder = people.find((person) => person.id === pkg.holderPersonId);
  const owner = people.find((person) => person.id === pkg.ownerPersonId);

  return (
    <li className="rounded-xl border border-line bg-surface-raised p-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="font-semibold">
            {t('packageOrdinal')} {pkg.ordinal}
          </p>
          <p className="text-sm text-ink-muted">
            {formatQuantity(pkg.remaining)} / {formatQuantity(pkg.nominalCapacity)}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-1.5">
          {/* Emptiness is derived from the ledger, so it is shown rather than stored. */}
          {pkg.isEmpty && pkg.state === 'Opened' ? (
            <Badge tone="quiet">{t('empty')}</Badge>
          ) : stateKey ? (
            <Badge tone={pkg.state === 'Sealed' ? 'neutral' : 'accent'}>{t(stateKey)}</Badge>
          ) : null}
          {pkg.isPinned ? <Badge tone="positive">{t('pinned')}</Badge> : null}
          {activeLoanId ? <Badge tone="warning">{t('onLoan')}</Badge> : null}
        </div>
      </div>

      <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-sm text-ink-muted sm:grid-cols-3">
        {pkg.expiresOn ? <Detail label={t('expiresOn')} value={pkg.expiresOn} /> : null}
        {pkg.acquiredOn ? <Detail label={t('acquiredOn')} value={pkg.acquiredOn} /> : null}
        {pkg.lotNumber ? <Detail label={t('lotNumber')} value={pkg.lotNumber} /> : null}
        {pkg.storageLocation ? <Detail label={t('storageLocation')} value={pkg.storageLocation} /> : null}
        {owner ? <Detail label={t('owner')} value={owner.name} /> : null}
        {holder && holder.id !== owner?.id ? <Detail label={t('holder')} value={holder.name} /> : null}
      </dl>

      {pkg.state === 'Sealed' || pkg.state === 'Opened' ? (
        <div className="mt-3 flex flex-wrap gap-2">
          <Button
            variant="quiet"
            disabled={busy}
            onClick={() =>
              void run(() =>
                pkg.isPinned ? api.unpinPackage(household, pkg.id) : api.pinPackage(household, pkg.id),
              )
            }
          >
            {pkg.isPinned ? t('unpin') : t('pin')}
          </Button>

          {activeLoanId ? (
            <Button variant="quiet" disabled={busy} onClick={() => void run(() => api.returnLoan(household, activeLoanId))}>
              {t('returnLoan')}
            </Button>
          ) : pkg.ownerPersonId ? (
            // Lending moves custody only, so it is offered on a package that someone
            // owns and nobody else is currently holding.
            <LendControl
              people={people.filter((person) => !person.isArchived && person.id !== pkg.ownerPersonId)}
              busy={busy}
              onLend={(borrower) => void run(() => api.lendPackage(household, pkg.id, borrower))}
            />
          ) : null}

          <Button
            variant="danger"
            disabled={busy}
            onClick={() => void run(() => api.retirePackage(household, pkg.id, 'Lost'))}
          >
            {t('retireLost')}
          </Button>
          <Button
            variant="danger"
            disabled={busy}
            onClick={() => void run(() => api.retirePackage(household, pkg.id, 'Disposed'))}
          >
            {t('retireDisposed')}
          </Button>
        </div>
      ) : (
        // The owner found a box he had written off and there was nowhere to say so:
        // Reinstate() existed in the domain with no route and no button, so marking a
        // package lost was a one-way door. The hint is here because the arithmetic is
        // the part worth knowing — the amount comes back, and the loss stays on record.
        <div className="mt-3 flex flex-col gap-2">
          <Button variant="secondary" disabled={busy} onClick={() => void run(() => api.reinstatePackage(household, pkg.id))}>
            {t('reinstatePackage')}
          </Button>
          <p className="text-sm text-ink-muted">{t('reinstateHint')}</p>
        </div>
      )}
    </li>
  );
}

/** Picks a borrower and lends the package, without changing its stock or its owner. */
function LendControl({ people, busy, onLend }: {
  people: Person[];
  busy: boolean;
  onLend: (borrowerPersonId: string) => void;
}) {
  const { t } = useLocale();
  const [borrower, setBorrower] = useState('');

  if (people.length === 0) {
    return null;
  }

  return (
    <span className="inline-flex items-center gap-2">
      <label className="sr-only" htmlFor={`lend-${people[0].id}`}>
        {t('lend')}
      </label>
      <Select
        id={`lend-${people[0].id}`}
        value={borrower}
        onChange={(event) => setBorrower(event.target.value)}
        className="w-auto"
      >
        <option value="">{t('lend')}…</option>
        {people.map((person) => (
          <option key={person.id} value={person.id}>
            {person.name}
          </option>
        ))}
      </Select>
      <Button variant="quiet" disabled={busy || borrower === ''} onClick={() => onLend(borrower)}>
        {t('lend')}
      </Button>
    </span>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wide text-ink-faint">{label}</dt>
      <dd className="m-0">{value}</dd>
    </div>
  );
}

/** Low stock and the gap between running out and being allowed to refill. */
function ForecastBanner({ forecast }: { forecast: Forecast }) {
  const { t } = useLocale();

  if (forecast.hasRefillGap) {
    return (
      <Notice tone="danger">
        {t('refillGapWarning')}: {forecast.refillGapDays} {t('refillGapDetail')}
        {forecast.projectedDepletionOn ? ` · ${t('depletionOn')} ${forecast.projectedDepletionOn}` : ''}
      </Notice>
    );
  }

  if (forecast.lowStockReason === 'AlreadyDepleted') {
    return <Notice tone="danger">{t('alreadyDepleted')}</Notice>;
  }

  if (forecast.isLowStock) {
    return (
      <Notice>
        {t('lowStockWarning')}
        {forecast.daysOfStockRemaining !== null
          ? ` · ${forecast.daysOfStockRemaining} ${t('daysRemaining')}`
          : ''}
      </Notice>
    );
  }

  if (forecast.isForecastable && forecast.projectedDepletionOn) {
    return (
      <p className="text-sm text-ink-muted">
        {t('depletionOn')}: {forecast.projectedDepletionOn}
        {forecast.daysOfStockRemaining !== null
          ? ` (${forecast.daysOfStockRemaining} ${t('daysRemaining')})`
          : ''}
      </p>
    );
  }

  return null;
}

/** Defining what a medication *is*. Deliberately separate from adding its stock. */
function DefinitionDialog({ household, definition, onClose, onSaved }: {
  household: string;
  definition: MedicationDefinition | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useLocale();
  const [name, setName] = useState(definition?.name ?? '');
  const [form, setForm] = useState(definition?.form ?? 'Tablet');
  const [strength, setStrength] = useState(definition?.strength ?? '');
  const [brand, setBrand] = useState(definition?.brand ?? '');
  const [ingredients, setIngredients] = useState((definition?.activeIngredients ?? []).join(', '));
  const [capacity, setCapacity] = useState(
    definition?.defaultPackageCapacity ? formatQuantity(definition.defaultPackageCapacity) : '',
  );
  const [category, setCategory] = useState(definition?.category ?? '');
  const [tags, setTags] = useState((definition?.tags ?? []).join(', '));
  const [notes, setNotes] = useState(definition?.notes ?? '');
  const [doNotTakeWith, setDoNotTakeWith] = useState(definition?.cautions?.doNotTakeWith ?? '');
  const [foodsToAvoid, setFoodsToAvoid] = useState(definition?.cautions?.foodsToAvoid ?? '');
  const [thingsToDo, setThingsToDo] = useState(definition?.cautions?.thingsToDo ?? '');
  const [thingsToAvoid, setThingsToAvoid] = useState(definition?.cautions?.thingsToAvoid ?? '');
  const [warning, setWarning] = useState(definition?.cautions?.warning ?? '');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    if (name.trim() === '') {
      setError(t('errorGeneric'));
      return;
    }

    const parsedCapacity = capacity.trim() === '' ? null : parseQuantity(capacity);
    if (capacity.trim() !== '' && !parsedCapacity) {
      setError(t('errorGeneric'));
      return;
    }

    const input: MedicationDefinitionInput = {
      name: name.trim(),
      form,
      strength: strength.trim() || null,
      brand: brand.trim() || null,
      activeIngredients: splitList(ingredients),
      defaultPackageCapacityNumerator: parsedCapacity?.numerator ?? null,
      defaultPackageCapacityDenominator: parsedCapacity?.denominator ?? null,
      category: category.trim() || null,
      tags: splitList(tags),
      notes: notes.trim() || null,

      // Blank is sent as null, not as "". An empty string would come back and render as
      // a warning with nothing in it, which is how a warning block stops being read.
      cautionDoNotTakeWith: doNotTakeWith.trim() || null,
      cautionFoodsToAvoid: foodsToAvoid.trim() || null,
      cautionThingsToDo: thingsToDo.trim() || null,
      cautionThingsToAvoid: thingsToAvoid.trim() || null,
      cautionWarning: warning.trim() || null,
    };

    setBusy(true);
    setError(null);

    try {
      if (definition) {
        await api.updateDefinition(household, definition.id, input);
      } else {
        await api.createDefinition(household, input);
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
      title={definition ? t('editMedication') : t('addMedication')}
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

        <Field label={t('medicationName')}>
          {({ id }) => <Input id={id} value={name} onChange={(e) => setName(e.target.value)} autoFocus />}
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('form')}>
            {({ id }) => (
              <Select id={id} value={form} onChange={(e) => setForm(e.target.value)}>
                {FORMS.map((option) => (
                  <option key={option} value={option}>
                    {unitLabel(option)}
                  </option>
                ))}
              </Select>
            )}
          </Field>

          <Field label={t('strength')} optional={t('optional')}>
            {({ id }) => (
              <Input
                id={id}
                value={strength}
                onChange={(e) => setStrength(e.target.value)}
                placeholder={t('strengthPlaceholder')}
              />
            )}
          </Field>
        </div>

        <Field label={t('defaultPackageSize')} hint={t('defaultPackageSizeHint')} optional={t('optional')}>
          {({ id, describedBy }) => (
            <Input id={id} aria-describedby={describedBy} value={capacity} onChange={(e) => setCapacity(e.target.value)} />
          )}
        </Field>

        {/* Not filed under "advanced options": safety notes are not an advanced
            concern, they are the reason somebody opens this form a second time. Its own
            section keeps the everyday add-a-medicine flow down to a few fields while
            still being findable, and it opens itself when notes already exist so
            editing one never means hunting for it. */}
        <Advanced label={t('cautionNotes')} defaultOpen={definition?.cautions != null}>
          <div className="flex flex-col gap-4">
            <p className="text-sm text-ink-muted">{t('cautionNotesHint')}</p>

            <Field label={t('cautionDoNotTakeWith')} hint={t('cautionDoNotTakeWithHint')} optional={t('optional')}>
              {({ id, describedBy }) => (
                <Textarea
                  id={id}
                  aria-describedby={describedBy}
                  rows={2}
                  value={doNotTakeWith}
                  onChange={(e) => setDoNotTakeWith(e.target.value)}
                />
              )}
            </Field>
            <Field label={t('cautionFoodsToAvoid')} optional={t('optional')}>
              {({ id }) => (
                <Textarea
                  id={id}
                  rows={2}
                  value={foodsToAvoid}
                  onChange={(e) => setFoodsToAvoid(e.target.value)}
                />
              )}
            </Field>
            <Field label={t('cautionThingsToDo')} hint={t('cautionThingsToDoHint')} optional={t('optional')}>
              {({ id, describedBy }) => (
                <Textarea
                  id={id}
                  aria-describedby={describedBy}
                  rows={2}
                  value={thingsToDo}
                  onChange={(e) => setThingsToDo(e.target.value)}
                />
              )}
            </Field>
            <Field label={t('cautionThingsToAvoid')} hint={t('cautionThingsToAvoidHint')} optional={t('optional')}>
              {({ id, describedBy }) => (
                <Textarea
                  id={id}
                  aria-describedby={describedBy}
                  rows={2}
                  value={thingsToAvoid}
                  onChange={(e) => setThingsToAvoid(e.target.value)}
                />
              )}
            </Field>
            <Field label={t('cautionWarning')} optional={t('optional')}>
              {({ id }) => (
                <Textarea id={id} rows={2} value={warning} onChange={(e) => setWarning(e.target.value)} />
              )}
            </Field>
          </div>
        </Advanced>

        <Advanced label={t('advancedOptions')}>
          <div className="flex flex-col gap-4">
            <Field label={t('brand')} optional={t('optional')}>
              {({ id }) => <Input id={id} value={brand} onChange={(e) => setBrand(e.target.value)} />}
            </Field>
            <Field label={t('activeIngredients')} hint={t('activeIngredientsHint')} optional={t('optional')}>
              {({ id, describedBy }) => (
                <Input id={id} aria-describedby={describedBy} value={ingredients} onChange={(e) => setIngredients(e.target.value)} />
              )}
            </Field>
            <Field label={t('category')} optional={t('optional')}>
              {({ id }) => <Input id={id} value={category} onChange={(e) => setCategory(e.target.value)} />}
            </Field>
            <Field label={t('tags')} hint={t('activeIngredientsHint')} optional={t('optional')}>
              {({ id, describedBy }) => (
                <Input id={id} aria-describedby={describedBy} value={tags} onChange={(e) => setTags(e.target.value)} />
              )}
            </Field>
            <Field label={t('notes')} optional={t('optional')}>
              {({ id }) => <Input id={id} value={notes} onChange={(e) => setNotes(e.target.value)} />}
            </Field>
          </div>
        </Advanced>
      </div>
    </Dialog>
  );
}

/**
 * Adding physical stock.
 *
 * The everyday case is two fields: how many full boxes, and how much each holds.
 * Opened boxes, expiry, lot, owner and loose stock are advanced.
 */
function AddStockDialog({ household, definition, people, onClose, onSaved }: {
  household: string;
  definition: MedicationDefinition;
  people: Person[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useLocale();
  const [capacity, setCapacity] = useState(
    definition.defaultPackageCapacity ? formatQuantity(definition.defaultPackageCapacity) : '',
  );
  const [fullPackages, setFullPackages] = useState('1');
  const [opened, setOpened] = useState<string[]>([]);
  const [loose, setLoose] = useState('');
  const [owner, setOwner] = useState('');
  const [expiresOn, setExpiresOn] = useState('');
  const [acquiredOn, setAcquiredOn] = useState('');
  const [lotNumber, setLotNumber] = useState('');
  const [storageLocation, setStorageLocation] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const parsedCapacity = parseQuantity(capacity);
  const fullCount = Number.parseInt(fullPackages, 10);
  const packagesToCreate = (Number.isFinite(fullCount) ? Math.max(fullCount, 0) : 0) + opened.length;

  // What those boxes come to in units. "2 packages" does not answer "how many tablets",
  // which is the question somebody looking at a shelf is actually asking. Added as
  // fractions rather than floats, so half a box stays half a box.
  const previewTotal = ((): Quantity | null => {
    if (!parsedCapacity) {
      return null;
    }

    const parts: Quantity[] = [];
    const whole = Number.isFinite(fullCount) ? Math.max(fullCount, 0) : 0;
    for (let index = 0; index < whole; index += 1) {
      parts.push({ ...parsedCapacity, display: '' });
    }

    for (const value of opened) {
      const parsed = parseQuantity(value === '' ? '0' : value);
      if (!parsed) {
        return null;
      }
      parts.push({ ...parsed, display: '' });
    }

    if (loose.trim() !== '') {
      const parsed = parseQuantity(loose);
      if (!parsed) {
        return null;
      }
      parts.push({ ...parsed, display: '' });
    }

    const total = addQuantities(parts);
    return total.numerator > 0 ? { ...total, display: '' } : null;
  })();

  async function submit() {
    if (!parsedCapacity || parsedCapacity.numerator <= 0) {
      setError(t('errorGeneric'));
      return;
    }

    const openedPackages = [];
    for (const value of opened) {
      const parsed = parseQuantity(value === '' ? '0' : value);
      if (!parsed || parsed.numerator < 0) {
        setError(t('errorGeneric'));
        return;
      }
      openedPackages.push({ remainingNumerator: parsed.numerator, remainingDenominator: parsed.denominator });
    }

    const parsedLoose = loose.trim() === '' ? null : parseQuantity(loose);
    if (loose.trim() !== '' && (!parsedLoose || parsedLoose.numerator <= 0)) {
      setError(t('errorGeneric'));
      return;
    }

    if (packagesToCreate === 0 && !parsedLoose) {
      setError(t('errorGeneric'));
      return;
    }

    const input: AddStockInput = {
      capacityNumerator: parsedCapacity.numerator,
      capacityDenominator: parsedCapacity.denominator,
      fullPackages: Number.isFinite(fullCount) ? Math.max(fullCount, 0) : 0,
      openedPackages,
      looseNumerator: parsedLoose?.numerator ?? null,
      looseDenominator: parsedLoose?.denominator ?? null,
      ownerPersonId: owner === '' ? null : owner,
      expiresOn: expiresOn === '' ? null : expiresOn,
      acquiredOn: acquiredOn === '' ? null : acquiredOn,
      lotNumber: lotNumber.trim() || null,
      storageLocation: storageLocation.trim() || null,
    };

    setBusy(true);
    setError(null);

    try {
      await api.addStock(household, definition.id, input);
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
      title={`${t('addStock')} — ${definition.name}`}
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

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('fullPackageCount')}>
            {({ id }) => (
              <Input
                id={id}
                type="number"
                min={0}
                max={100}
                value={fullPackages}
                onChange={(e) => setFullPackages(e.target.value)}
                autoFocus
              />
            )}
          </Field>

          <Field label={`${t('packageCapacity')} (${unitLabel(definition.unit)})`}>
            {({ id }) => <Input id={id} value={capacity} onChange={(e) => setCapacity(e.target.value)} />}
          </Field>
        </div>

        {/* A part-used box was only reachable behind "Advanced options", so the form
            looked like it could add full boxes and nothing else. A half box is not an
            advanced case — it is the box you are holding. It belongs in the open. */}
        <div className="flex flex-col gap-2">
          {opened.map((value, index) => (
            <div key={index} className="flex items-end gap-2">
              <Field
                label={`${t('openedPackage')} ${index + 1} — ${t('remainingInPackage')} (${unitLabel(definition.unit)})`}
                className="flex-1"
              >
                {({ id }) => (
                  <Input
                    id={id}
                    value={value}
                    onChange={(e) =>
                      setOpened(opened.map((existing, i) => (i === index ? e.target.value : existing)))
                    }
                  />
                )}
              </Field>
              <Button
                variant="quiet"
                onClick={() => setOpened(opened.filter((_, i) => i !== index))}
                aria-label={t('removeRow')}
              >
                {t('removeRow')}
              </Button>
            </div>
          ))}
          <div>
            <Button variant="secondary" onClick={() => setOpened([...opened, ''])}>
              {t('addOpenedPackage')}
            </Button>
          </div>
        </div>

        {/* Each box is a real container, so the preview counts rows — and then says what
            that comes to, because "2 packages" does not answer "how many tablets". */}
        {packagesToCreate > 0 && parsedCapacity ? (
          <p className="rounded-lg bg-surface-sunken px-3 py-2 text-sm">
            {t('stockPreview')} <strong>{packagesToCreate}</strong> {t('stockPreviewPackages')}
            {previewTotal ? (
              <>
                {' — '}
                <strong>{formatQuantity(previewTotal)}</strong> {unitLabel(definition.unit)}
              </>
            ) : null}
          </p>
        ) : null}

        {/* Stock is added as often as stock arrives: this form can be opened again
            tomorrow for the box you found at the back of a drawer. Saying so costs one
            line and saves the question. */}
        <p className="text-sm text-ink-muted">{t('addStockAgainHint')}</p>

        <Advanced label={t('advancedOptions')}>
          <div className="flex flex-col gap-4">
            <Field label={t('assignTo')} optional={t('optional')}>
              {({ id }) => (
                <Select id={id} value={owner} onChange={(e) => setOwner(e.target.value)}>
                  <option value="">{t('unassigned')}</option>
                  {people
                    .filter((person) => !person.isArchived)
                    .map((person) => (
                      <option key={person.id} value={person.id}>
                        {person.name}
                      </option>
                    ))}
                </Select>
              )}
            </Field>

            <div className="grid gap-4 sm:grid-cols-2">
              <Field label={t('expiresOn')} optional={t('optional')}>
                {({ id }) => (
                  <Input id={id} type="date" value={expiresOn} onChange={(e) => setExpiresOn(e.target.value)} />
                )}
              </Field>
              <Field label={t('acquiredOn')} optional={t('optional')}>
                {({ id }) => (
                  <Input id={id} type="date" value={acquiredOn} onChange={(e) => setAcquiredOn(e.target.value)} />
                )}
              </Field>
              <Field label={t('lotNumber')} optional={t('optional')}>
                {({ id }) => <Input id={id} value={lotNumber} onChange={(e) => setLotNumber(e.target.value)} />}
              </Field>
              <Field label={t('storageLocation')} optional={t('optional')}>
                {({ id }) => (
                  <Input id={id} value={storageLocation} onChange={(e) => setStorageLocation(e.target.value)} />
                )}
              </Field>
            </div>

            <Field label={t('looseAmount')} optional={t('optional')}>
              {({ id }) => <Input id={id} value={loose} onChange={(e) => setLoose(e.target.value)} />}
            </Field>
          </div>
        </Advanced>
      </div>
    </Dialog>
  );
}

/** Low-stock settings and the official refill date, which are separate facts. */
function RefillDialog({ household, definition, onClose, onSaved }: {
  household: string;
  definition: MedicationDefinition;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useLocale();
  const policy = definition.refillPolicy;
  const [threshold, setThreshold] = useState(
    policy?.lowStockThreshold ? formatQuantity(policy.lowStockThreshold) : '',
  );
  const [days, setDays] = useState(policy?.lowStockDays?.toString() ?? '');
  const [refillOn, setRefillOn] = useState(policy?.nextEligibleRefillOn ?? '');
  const [note, setNote] = useState(policy?.note ?? '');
  const [forecast, setForecast] = useState<Forecast | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void api.forecast(household, definition.id).then(setForecast).catch(() => setForecast(null));
  }, [household, definition.id]);

  async function submit() {
    const parsedThreshold = threshold.trim() === '' ? null : parseQuantity(threshold);
    if (threshold.trim() !== '' && !parsedThreshold) {
      setError(t('errorGeneric'));
      return;
    }

    const parsedDays = days.trim() === '' ? null : Number.parseInt(days, 10);
    if (parsedDays !== null && (!Number.isFinite(parsedDays) || parsedDays < 0 || parsedDays > 365)) {
      setError(t('errorGeneric'));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await api.setRefillPolicy(household, definition.id, {
        lowStockThresholdNumerator: parsedThreshold?.numerator ?? null,
        lowStockThresholdDenominator: parsedThreshold?.denominator ?? null,
        lowStockDays: parsedDays,
        nextEligibleRefillOn: refillOn === '' ? null : refillOn,
        note: note.trim() || null,
      });
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
      title={`${t('refillSettings')} — ${definition.name}`}
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

        {forecast ? (
          forecast.isForecastable ? (
            <ForecastBanner forecast={forecast} />
          ) : (
            <p className="text-sm text-ink-muted">{t('notForecastable')}</p>
          )
        ) : (
          <Spinner label={t('loading')} />
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('lowStockThreshold')} optional={t('optional')}>
            {({ id }) => <Input id={id} value={threshold} onChange={(e) => setThreshold(e.target.value)} />}
          </Field>
          <Field label={t('lowStockDays')} optional={t('optional')}>
            {({ id }) => (
              <Input id={id} type="number" min={0} max={365} value={days} onChange={(e) => setDays(e.target.value)} />
            )}
          </Field>
        </div>

        <Field label={t('nextEligibleRefill')} hint={t('nextEligibleRefillHint')} optional={t('optional')}>
          {({ id, describedBy }) => (
            <Input
              id={id}
              aria-describedby={describedBy}
              type="date"
              value={refillOn}
              onChange={(e) => setRefillOn(e.target.value)}
            />
          )}
        </Field>

        <Field label={t('note')} optional={t('optional')}>
          {({ id }) => <Input id={id} value={note} onChange={(e) => setNote(e.target.value)} />}
        </Field>
      </div>
    </Dialog>
  );
}

function splitList(value: string): string[] {
  return value
    .split(',')
    .map((item) => item.trim())
    .filter((item) => item !== '');
}
