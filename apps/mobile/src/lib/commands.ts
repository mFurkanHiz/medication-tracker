import type {
  AddStockRequest,
  CountLineInput,
  CountRequest,
  CreateMedicationRequest,
  CreatePersonRequest,
  CreatePlanRequest,
  RecurrencePattern,
  RetirePackageRequest,
  SetPlanPausedRequest,
  UpdatePackageRequest,
} from './api';
import type { MessageKey, Translate } from './i18n';
import { describeSchedule, type PlanShape } from './plans';
import { compareQuantities, formatQuantity, parseQuantity, type Quantity } from './quantity';

/**
 * The commands the phone can queue besides a dose, and the pure parts of handling them:
 * what each one is called, how the add-stock form becomes a request, and in which order
 * a mixed queue of doses and commands is sent. No SQLite or network here, so it is
 * unit-tested.
 */

export type CommandKind =
  | 'plan.pause'
  | 'plan.end'
  | 'plan.restart'
  | 'stock.add'
  | 'package.pin'
  | 'package.unpin'
  | 'package.retire'
  | 'package.reinstate'
  | 'package.update'
  | 'package.assign'
  | 'package.lend'
  | 'loan.return'
  | 'inventory.count'
  | 'person.create'
  | 'medication.create'
  | 'plan.create';

export type CommandPayload =
  | { kind: 'plan.pause'; body: SetPlanPausedRequest }
  | { kind: 'plan.end'; body: { endsOn: string } }
  | { kind: 'plan.restart'; body: { startsOn: string } }
  | { kind: 'stock.add'; body: AddStockRequest }
  | { kind: 'package.pin'; body: Record<string, never> }
  | { kind: 'package.unpin'; body: Record<string, never> }
  | { kind: 'package.retire'; body: RetirePackageRequest }
  | { kind: 'package.reinstate'; body: Record<string, never> }
  | { kind: 'package.update'; body: UpdatePackageRequest }
  | { kind: 'package.assign'; body: { personId: string | null } }
  | { kind: 'package.lend'; body: { borrowerPersonId: string } }
  /** `packageId` rides along so the phone can show the box again once the loan is back. */
  | { kind: 'loan.return'; body: { packageId: string } }
  | { kind: 'inventory.count'; body: CountRequest }
  | { kind: 'person.create'; body: CreatePersonRequest }
  | { kind: 'medication.create'; body: CreateMedicationRequest }
  | { kind: 'plan.create'; body: CreatePlanRequest };

/** The kinds whose target is a box, for the queue's joins and the Stock screen's badges. */
export const PACKAGE_COMMAND_KINDS: readonly CommandKind[] = [
  'package.pin', 'package.unpin', 'package.retire', 'package.reinstate', 'package.update', 'package.assign', 'package.lend',
];

/**
 * The kinds whose key travels in the body as well as in the queue: the server cannot
 * tell a second send of these apart from a second decision on its own.
 */
export const KEYED_BODY_KINDS: readonly CommandKind[] = [
  'stock.add', 'inventory.count', 'person.create', 'medication.create', 'plan.create',
];

export function carriesKeyInBody(kind: string): boolean {
  return (KEYED_BODY_KINDS as readonly string[]).includes(kind);
}

/** The command's name in the reader's words: "Şimdilik ara ver", "Stok ekle: 2 kutu". */
export function describeCommand(kind: string, payload: unknown, t: Translate): string {
  const body = (payload ?? {}) as Record<string, unknown>;

  switch (kind as CommandKind) {
    case 'plan.pause':
      return t(body.isPaused ? 'pausePlan' : 'resumePlan');
    case 'plan.end':
      return `${t('endPlan')} · ${String(body.endsOn ?? '')}`;
    case 'plan.restart':
      return `${t('restartPlan')} · ${String(body.startsOn ?? '')}`;
    case 'package.pin':
      return t('makeActive');
    case 'package.unpin':
      return t('unpin');
    case 'package.retire':
      return t(body.state === 'Disposed' ? 'markDisposed' : 'markLost');
    case 'package.reinstate':
      return t('reinstatePackage');
    case 'package.update':
      return t('editPackage');
    case 'package.assign':
      return body.personId ? t('assignTo') : t('noOwner');
    case 'package.lend':
      return t('lend');
    case 'loan.return':
      return t('returnLoan');
    case 'stock.add':
      return `${t('addStock')}: ${describeStock(body as Partial<AddStockRequest>, t)}`;
    case 'inventory.count': {
      const lines = Array.isArray(body.lines) ? body.lines.length : 0;
      return `${t('counting')}: ${lines} ${t('countLines')}`;
    }
    // The name travels in the body, so a refused create still says who or what it was
    // once the provisional row is gone from the snapshot.
    case 'person.create':
      return `${t('addPerson')}: ${String(body.name ?? '')}`;
    case 'medication.create':
      return `${t('addMedication')}: ${String(body.name ?? '')}`;
    case 'plan.create':
      return `${t('addPlan')} · ${describeSchedule(planShapeOf(body), t)}`;
    default:
      return kind;
  }
}

/** The queued create's schedule as a plan shape, so it reads like the plan it will be. */
function planShapeOf(body: Record<string, unknown>): PlanShape {
  const text = (value: unknown) => (typeof value === 'string' ? value : null);
  const count = (value: unknown) => (typeof value === 'number' ? value : null);

  return {
    kind: body.kind === 'AsNeeded' ? 'AsNeeded' : 'Scheduled',
    pattern: (text(body.pattern) ?? 'Daily') as PlanShape['pattern'],
    weekdayMask: count(body.weekdayMask),
    intervalDays: count(body.intervalDays),
    dayOfMonth: count(body.dayOfMonth),
    intervalMonths: count(body.intervalMonths),
    effectiveFrom: text(body.effectiveFrom),
    effectiveTo: text(body.effectiveTo),
    localTime: text(body.localTime),
    dayPeriod: text(body.dayPeriod),
    isPaused: false,
  };
}

function describeStock(body: Partial<AddStockRequest>, t: Translate): string {
  const parts: string[] = [];

  if (body.fullPackages) {
    parts.push(`${body.fullPackages} ${t('sealedBoxes')}`);
  }

  for (const opened of body.openedPackages ?? []) {
    const remaining = formatQuantity({
      numerator: opened.remainingNumerator,
      denominator: opened.remainingDenominator ?? 1,
    });
    parts.push(`${t('openedBox')} (${remaining})`);
  }

  if (body.looseNumerator) {
    parts.push(`${formatQuantity({ numerator: body.looseNumerator, denominator: body.looseDenominator ?? 1 })} ${t('looseLabel').toLowerCase()}`);
  }

  return parts.join(' + ');
}

export type AddStockForm = {
  fullPackages: string;
  openedRemaining: string;
  capacity: string;
  loose: string;
};

export type AddStockOutcome =
  | { ok: true; body: Omit<AddStockRequest, 'idempotencyKey'> }
  | { ok: false; error: MessageKey };

/**
 * Turns what was typed into the server's request, refusing what the server would refuse
 * so the user hears it now rather than from a rejection after the next sync.
 *
 * Capacity is required unless the catalogue has a default the server will use.
 */
export function buildAddStock(form: AddStockForm, defaultCapacity: Quantity | null): AddStockOutcome {
  const fullPackages = form.fullPackages.trim() === '' ? 0 : Number(form.fullPackages.trim());
  if (!Number.isInteger(fullPackages) || fullPackages < 0 || fullPackages > 100) {
    return { ok: false, error: 'stockAddInvalid' };
  }

  const opened = form.openedRemaining.trim() === '' ? null : parseQuantity(form.openedRemaining);
  if (form.openedRemaining.trim() !== '' && (!opened || opened.numerator < 0)) {
    return { ok: false, error: 'stockAddInvalid' };
  }

  const loose = form.loose.trim() === '' ? null : parseQuantity(form.loose);
  if (form.loose.trim() !== '' && (!loose || loose.numerator <= 0)) {
    return { ok: false, error: 'stockAddInvalid' };
  }

  if (fullPackages === 0 && opened === null && loose === null) {
    return { ok: false, error: 'stockAddInvalid' };
  }

  const typedCapacity = form.capacity.trim() === '' ? null : parseQuantity(form.capacity);
  if (form.capacity.trim() !== '' && (!typedCapacity || typedCapacity.numerator <= 0)) {
    return { ok: false, error: 'capacityRequired' };
  }

  const capacity = typedCapacity ?? defaultCapacity;
  const needsCapacity = fullPackages > 0 || opened !== null;
  if (needsCapacity && capacity === null) {
    return { ok: false, error: 'capacityRequired' };
  }

  if (opened && capacity && compareQuantities(opened, capacity) > 0) {
    return { ok: false, error: 'remainingExceedsCapacity' };
  }

  return {
    ok: true,
    body: {
      ...(typedCapacity
        ? { capacityNumerator: typedCapacity.numerator, capacityDenominator: typedCapacity.denominator }
        : {}),
      fullPackages,
      openedPackages: opened
        ? [{ remainingNumerator: opened.numerator, remainingDenominator: opened.denominator }]
        : [],
      ...(loose ? { looseNumerator: loose.numerator, looseDenominator: loose.denominator } : {}),
    },
  };
}

export type Queued<T> = { createdAt: string; item: T };

/** ISO timestamps compared as instants, not as text: "…:02.500Z" sorts after "…:02Z". */
function isNotAfter(left: string, right: string): boolean {
  const a = Date.parse(left);
  const b = Date.parse(right);
  return Number.isNaN(a) || Number.isNaN(b) ? left <= right : a <= b;
}

/**
 * One send order for two queues. Creation time decides; at the same instant a dose
 * goes first, because it is the record a household member cares most about reaching
 * the server, and a command rarely depends on a dose.
 */
export function interleave<D, C>(
  doses: readonly Queued<D>[],
  commands: readonly Queued<C>[],
): ({ kind: 'dose'; item: D } | { kind: 'command'; item: C })[] {
  const merged: ({ kind: 'dose'; item: D } | { kind: 'command'; item: C })[] = [];
  let d = 0;
  let c = 0;

  while (d < doses.length || c < commands.length) {
    const dose = doses[d];
    const command = commands[c];

    if (dose && (!command || isNotAfter(dose.createdAt, command.createdAt))) {
      merged.push({ kind: 'dose', item: dose.item });
      d += 1;
    } else if (command) {
      merged.push({ kind: 'command', item: command.item });
      c += 1;
    }
  }

  return merged;
}

/** "Parol · Ayşe" for a plan, "Parol · Kutu 3" for a box, "Parol" for added stock. */
export function describeTarget(
  row: { medicationName: string | null; personName: string | null; packageLabel: string | null; packageOrdinal: number | null },
  t: Translate,
): string {
  const parts: string[] = [];
  if (row.medicationName) {
    parts.push(row.medicationName);
  }
  if (row.personName) {
    parts.push(row.personName);
  }
  if (row.packageLabel) {
    parts.push(row.packageLabel);
  } else if (row.packageOrdinal !== null) {
    parts.push(`${t('packageOrdinal')} ${row.packageOrdinal}`);
  }
  return parts.join(' · ');
}

export type CountOutcome =
  | { ok: true; lines: CountLineInput[] }
  | { ok: false; error: 'countingInvalidAmount' | 'countingNothingEntered' };

/**
 * What was typed on the counting sheet, as the server's lines. Keys are a medicine's id,
 * or `medicine:box` for one box. Blank rows are left alone; one unreadable row refuses
 * the whole count, because a count that quietly dropped a row would write a
 * reconciliation the household did not agree to — the web's rule, kept.
 */
export function buildCountLines(entries: Record<string, string>): CountOutcome {
  const lines: CountLineInput[] = [];

  for (const [key, raw] of Object.entries(entries)) {
    if (raw.trim() === '') {
      continue;
    }

    const parsed = parseQuantity(raw);
    if (!parsed || parsed.numerator < 0) {
      return { ok: false, error: 'countingInvalidAmount' };
    }

    const [medicationDefinitionId, packageId] = key.split(':');
    lines.push({
      medicationDefinitionId,
      observedNumerator: parsed.numerator,
      observedDenominator: parsed.denominator,
      ...(packageId ? { packageId } : {}),
    });
  }

  return lines.length === 0 ? { ok: false, error: 'countingNothingEntered' } : { ok: true, lines };
}

/** A calendar day typed as `YYYY-MM-DD` and real: 2026-02-30 is not a day. */
export function isValidDay(text: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text.trim());
  if (!match) {
    return false;
  }

  const [, year, month, day] = match.map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

/**
 * A refusal that says the command's goal is already reached: a box the server says is
 * not retired is back, a loan it says is already returned is home. The web shows these
 * as errors for a double click; the phone, replaying a command whose answer never
 * arrived, treats them as done rather than as a failure to show.
 */
export function alreadyDone(kind: string, code: string): boolean {
  return (
    (kind === 'package.reinstate' && code === 'package_not_retired') ||
    (kind === 'loan.return' && code === 'already_returned')
  );
}

/** The server's `PharmaceuticalForm` names, in the web's order. */
export const FORMS = [
  'Tablet', 'Capsule', 'OralLiquid', 'Drops', 'Sachet', 'Suppository', 'Injection',
  'Cream', 'Ointment', 'Gel', 'Patch', 'InhalerSpray', 'NasalSpray', 'EyeDrops', 'EarDrops', 'Other',
] as const;

export type PharmaceuticalForm = (typeof FORMS)[number];

export const FORM_KEYS: Record<PharmaceuticalForm, MessageKey> = {
  Tablet: 'formTablet',
  Capsule: 'formCapsule',
  OralLiquid: 'formOralLiquid',
  Drops: 'formDrops',
  Sachet: 'formSachet',
  Suppository: 'formSuppository',
  Injection: 'formInjection',
  Cream: 'formCream',
  Ointment: 'formOintment',
  Gel: 'formGel',
  Patch: 'formPatch',
  InhalerSpray: 'formInhalerSpray',
  NasalSpray: 'formNasalSpray',
  EyeDrops: 'formEyeDrops',
  EarDrops: 'formEarDrops',
  Other: 'formOther',
};

/**
 * The unit the server gives a medicine whose form is known and whose unit was not
 * named — its `FormDefaults.DefaultUnitFor`, kept in step by a contract test. The phone
 * shows it on the provisional row and never sends it, so the server's rule stays the
 * only rule.
 */
export function defaultUnitFor(form: string): string {
  switch (form) {
    case 'Tablet':
      return 'Tablet';
    case 'Capsule':
      return 'Capsule';
    case 'OralLiquid':
      return 'Milliliter';
    case 'Drops':
    case 'EyeDrops':
    case 'EarDrops':
      return 'Drop';
    case 'Sachet':
      return 'Sachet';
    case 'Suppository':
      return 'Suppository';
    case 'Injection':
      return 'Ampoule';
    case 'Cream':
    case 'Ointment':
    case 'Gel':
      return 'Gram';
    case 'Patch':
      return 'Patch';
    case 'InhalerSpray':
    case 'NasalSpray':
      return 'Puff';
    default:
      return 'Dose';
  }
}

export type PersonOutcome = { ok: true; body: { name: string } } | { ok: false; error: MessageKey };

/** A person's name as the server takes it: trimmed, present, at most 160 characters. */
export function buildPerson(name: string): PersonOutcome {
  const trimmed = name.trim();
  if (trimmed === '' || trimmed.length > 160) {
    return { ok: false, error: 'personNameRequired' };
  }

  return { ok: true, body: { name: trimmed } };
}

export type MedicationForm = {
  name: string;
  form: string;
  strength: string;
  capacity: string;
  coverage: string;
};

export type MedicationOutcome =
  | { ok: true; body: Omit<CreateMedicationRequest, 'id' | 'idempotencyKey'> }
  | { ok: false; error: MessageKey };

const COVERAGES: readonly string[] = ['Unspecified', 'InsuranceCovered', 'SelfPaid'];

/**
 * What the medication sheet asks, as the server's request: the web's everyday fields
 * with the same limits, the box size exact, blanks sent as null rather than "".
 */
export function buildMedication(form: MedicationForm): MedicationOutcome {
  const name = form.name.trim();
  if (name === '' || name.length > 200) {
    return { ok: false, error: 'medicationNameRequired' };
  }

  if (!(FORMS as readonly string[]).includes(form.form) || !COVERAGES.includes(form.coverage)) {
    return { ok: false, error: 'errorGeneric' };
  }

  const strength = form.strength.trim();
  if (strength.length > 100) {
    return { ok: false, error: 'errorGeneric' };
  }

  const capacity = form.capacity.trim() === '' ? null : parseQuantity(form.capacity);
  if (form.capacity.trim() !== '' && (!capacity || capacity.numerator <= 0)) {
    return { ok: false, error: 'invalidAmount' };
  }

  return {
    ok: true,
    body: {
      name,
      form: form.form,
      strength: strength === '' ? null : strength,
      defaultPackageCapacityNumerator: capacity?.numerator ?? null,
      defaultPackageCapacityDenominator: capacity?.denominator ?? null,
      coverage: form.coverage,
    },
  };
}

/** The web's schedule choices: the five patterns, and "as needed" standing in for a kind. */
export const SCHEDULES = ['Daily', 'SelectedWeekdays', 'EveryNDays', 'DayOfMonth', 'EveryNMonths', 'AsNeeded'] as const;

export type ScheduleChoice = (typeof SCHEDULES)[number];

export const SCHEDULE_KEYS: Record<ScheduleChoice, MessageKey> = {
  Daily: 'scheduleDaily',
  SelectedWeekdays: 'scheduleWeekdays',
  EveryNDays: 'scheduleInterval',
  DayOfMonth: 'patternDayOfMonth',
  EveryNMonths: 'patternEveryNMonths',
  AsNeeded: 'scheduleAsNeeded',
};

export const DAY_PERIODS = ['Morning', 'Noon', 'Afternoon', 'Evening', 'Night', 'Bedtime'] as const;

// "Aç karnına" and "tok karnına" lead, as on the web: the two a Turkish prescription
// gives most often, and opposites. The three meal-relative ones follow.
export const MEAL_RELATIONS = ['Fasting', 'FullStomach', 'BeforeFood', 'WithFood', 'AfterFood'] as const;

export type PlanForm = {
  personId: string;
  medicationId: string;
  dose: string;
  schedule: ScheduleChoice;
  weekdayMask: number;
  intervalDays: string;
  dayOfMonth: string;
  intervalMonths: string;
  /** `HH:MM`; read only by a schedule with no day period. */
  localTime: string;
  /** A `DayPeriod` name, or '' for an exact time (scheduled) or no preference (as needed). */
  dayPeriod: string;
  /** A `MealRelation` name, or '' for none. */
  mealRelation: string;
  effectiveFrom: string;
  effectiveTo: string;
};

export type PlanOutcome =
  | { ok: true; body: Omit<CreatePlanRequest, 'id' | 'idempotencyKey'> }
  | { ok: false; error: MessageKey };

/**
 * The plan sheet as the server's request, by the web form's rules and the server's: each
 * pattern owns exactly its fields; a schedule is pinned to a clock time or a part of the
 * day, never both; an as-needed plan is sent as daily with no clock and may keep a
 * preference; the two patterns anchored on the start need one. Refused here so the
 * person hears it now rather than from a rejection after the next sync.
 */
export function buildPlan(form: PlanForm, timeZoneId: string): PlanOutcome {
  if (form.personId === '' || form.medicationId === '') {
    return { ok: false, error: 'planNeedsPersonAndMedication' };
  }

  const dose = parseQuantity(form.dose);
  if (!dose || dose.numerator <= 0) {
    return { ok: false, error: 'invalidAmount' };
  }

  const scheduled = form.schedule !== 'AsNeeded';
  const pattern: RecurrencePattern = form.schedule === 'AsNeeded' ? 'Daily' : form.schedule;

  let weekdayMask: number | null = null;
  let intervalDays: number | null = null;
  let dayOfMonth: number | null = null;
  let intervalMonths: number | null = null;

  if (pattern === 'SelectedWeekdays') {
    if (form.weekdayMask < 1 || form.weekdayMask > 127) {
      return { ok: false, error: 'weekdaysRequired' };
    }
    weekdayMask = form.weekdayMask;
  } else if (pattern === 'EveryNDays') {
    intervalDays = wholeNumber(form.intervalDays, 1, 3650);
    if (intervalDays === null) {
      return { ok: false, error: 'intervalDaysInvalid' };
    }
  } else if (pattern === 'DayOfMonth') {
    dayOfMonth = wholeNumber(form.dayOfMonth, 1, 31);
    if (dayOfMonth === null) {
      return { ok: false, error: 'dayOfMonthInvalid' };
    }
  } else if (pattern === 'EveryNMonths') {
    intervalMonths = wholeNumber(form.intervalMonths, 1, 120);
    if (intervalMonths === null) {
      return { ok: false, error: 'intervalMonthsInvalid' };
    }
  }

  const effectiveFrom = form.effectiveFrom.trim() === '' ? null : form.effectiveFrom.trim();
  const effectiveTo = form.effectiveTo.trim() === '' ? null : form.effectiveTo.trim();
  if ((effectiveFrom !== null && !isValidDay(effectiveFrom)) || (effectiveTo !== null && !isValidDay(effectiveTo))) {
    return { ok: false, error: 'invalidDate' };
  }

  if (effectiveFrom !== null && effectiveTo !== null && effectiveTo < effectiveFrom) {
    return { ok: false, error: 'endBeforeStart' };
  }

  if ((pattern === 'EveryNDays' || pattern === 'EveryNMonths') && effectiveFrom === null) {
    return { ok: false, error: 'startRequired' };
  }

  const dayPeriod = form.dayPeriod === '' ? null : form.dayPeriod;
  const mealRelation = form.mealRelation === '' ? null : form.mealRelation;
  if (
    (dayPeriod !== null && !(DAY_PERIODS as readonly string[]).includes(dayPeriod)) ||
    (mealRelation !== null && !(MEAL_RELATIONS as readonly string[]).includes(mealRelation))
  ) {
    return { ok: false, error: 'errorGeneric' };
  }

  let localTime: string | null = null;
  if (scheduled && dayPeriod === null) {
    if (!isValidTime(form.localTime)) {
      return { ok: false, error: 'timeInvalid' };
    }
    localTime = `${form.localTime.trim()}:00`;
  }

  return {
    ok: true,
    body: {
      personId: form.personId,
      medicationDefinitionId: form.medicationId,
      doseNumerator: dose.numerator,
      doseDenominator: dose.denominator,
      timeZoneId,
      kind: scheduled ? 'Scheduled' : 'AsNeeded',
      pattern,
      weekdayMask,
      intervalDays,
      dayOfMonth,
      intervalMonths,
      effectiveFrom,
      effectiveTo,
      localTime,
      dayPeriod,
      mealRelation,
    },
  };
}

function wholeNumber(text: string, min: number, max: number): number | null {
  const trimmed = text.trim();
  if (!/^\d+$/.test(trimmed)) {
    return null;
  }

  const value = Number(trimmed);
  return value >= min && value <= max ? value : null;
}

/** A clock time typed as `HH:MM`, 24-hour. */
export function isValidTime(text: string): boolean {
  return /^([01]\d|2[0-3]):[0-5]\d$/.test(text.trim());
}
