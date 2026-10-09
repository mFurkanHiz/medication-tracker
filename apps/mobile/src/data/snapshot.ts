import type { SQLiteDatabase } from 'expo-sqlite';
import {
  api,
  type ActivityResponse,
  type ApiConfig,
  type DoseConflict,
  type RecurrencePattern,
  type TodayResponse,
  type WorkspaceResponse,
} from '../lib/api';
import { ZERO, addQuantities, compareQuantities, formatQuantity, subtractQuantity, type Quantity } from '../lib/quantity';
import type { PlanShape } from '../lib/plans';
import type { ReminderPlan } from '../notifications/reminders';
import { applyQueuedEffects, listCommands, type CommandRow } from './command-queue';
import { META, writeMeta } from './database';

/**
 * The cached view of what the server last said, plus the overlay of what this device
 * knows and the server has not acknowledged yet.
 */

export type LocalDueDose = {
  planVersionId: string;
  planId: string;
  personId: string;
  personName: string;
  medicationId: string;
  medicationName: string;
  unit: string;
  dose: Quantity;
  kind: 'Scheduled' | 'AsNeeded';
  localTime: string | null;
  dayPeriod: string | null;
  mealRelation: string | null;
  scheduledFor: string | null;
  hasEnoughStock: boolean;
  /** The server's "do not take with" warnings for this row. Shown, never enforced. */
  conflicts: DoseConflict[];
  /** What the server recorded, if anything. */
  serverOutcome: string | null;
  /** What this device recorded and has not had acknowledged. */
  localOutcome: string | null;
  localDoseId: string | null;
  /** True when this device and the server disagree about the same slot. */
  isConflicted: boolean;
  /** True while the local record is still queued. */
  isPending: boolean;
};

export type MedicationStock = {
  id: string;
  name: string;
  strength: string | null;
  unit: string;
  total: Quantity;
  packageCount: number;
};

export type PackageOption = {
  id: string;
  medicationId: string;
  ordinal: number;
  state: string;
  remaining: Quantity;
  capacity: Quantity;
  isPinned: boolean;
};

/**
 * Replaces the cached snapshot with a fresh server read.
 *
 * Only snapshot tables are touched. `local_doses`, `outbox`, `rejections` and
 * `reminders` are left alone: they hold what the server has not confirmed, and a
 * refresh must never be able to lose them.
 */
export async function refreshSnapshot(
  db: SQLiteDatabase,
  config: ApiConfig,
  householdId: string,
  localDate: string,
  now: string,
): Promise<void> {
  const [workspace, today, activity] = await Promise.all([
    api.workspace(config, householdId),
    api.today(config, householdId, localDate),
    api.activity(config, householdId),
  ]);

  await db.withTransactionAsync(async () => {
    await db.execAsync(
      'DELETE FROM people; DELETE FROM medications; DELETE FROM packages; DELETE FROM plans; DELETE FROM due_doses; DELETE FROM activity_entries;',
    );

    await writePeople(db, workspace);
    await writeMedications(db, workspace);
    await writePlans(db, workspace);
    await writeDue(db, today, localDate);
    await writeActivity(db, activity);

    // What this device decided and the server has not heard yet goes back on top.
    await applyQueuedEffects(db);
  });

  await writeMeta(db, META.lastSyncedAt, now);
  await writeMeta(db, META.snapshotDate, localDate);
}

async function writePeople(db: SQLiteDatabase, workspace: WorkspaceResponse): Promise<void> {
  for (const person of workspace.people) {
    await db.runAsync(
      'INSERT INTO people (id, name, is_archived) VALUES (?, ?, ?)',
      person.id,
      person.name,
      person.isArchived ? 1 : 0,
    );
  }
}

async function writeMedications(db: SQLiteDatabase, workspace: WorkspaceResponse): Promise<void> {
  for (const medication of workspace.medications) {
    await db.runAsync(
      `INSERT INTO medications (
         id, name, strength, unit, is_archived, total_numerator, total_denominator, package_count, coverage,
         default_capacity_numerator, default_capacity_denominator
       ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      medication.id,
      medication.name,
      medication.strength,
      medication.unit,
      medication.isArchived ? 1 : 0,
      medication.total.numerator,
      medication.total.denominator,
      medication.packageCount,
      medication.coverage ?? null,
      medication.defaultPackageCapacity?.numerator ?? null,
      medication.defaultPackageCapacity?.denominator ?? null,
    );

    for (const entry of medication.packages) {
      const view = entry.view;
      await db.runAsync(
        `INSERT INTO packages (
           id, medication_id, ordinal, state, remaining_numerator, remaining_denominator,
           capacity_numerator, capacity_denominator, holder_person_id, is_pinned,
           label, expires_on, coverage
         ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
        view.id,
        medication.id,
        view.ordinal,
        view.state,
        view.remaining.numerator,
        view.remaining.denominator,
        view.nominalCapacity.numerator,
        view.nominalCapacity.denominator,
        view.holderPersonId,
        view.isPinned ? 1 : 0,
        view.label ?? null,
        view.expiresOn ?? null,
        view.coverage ?? null,
      );
    }
  }
}

async function writePlans(db: SQLiteDatabase, workspace: WorkspaceResponse): Promise<void> {
  for (const plan of workspace.plans) {
    await db.runAsync(
      `INSERT INTO plans (
         id, version_id, person_id, medication_id, dose_numerator, dose_denominator,
         kind, pattern, weekday_mask, interval_days, day_of_month, interval_months,
         effective_from, effective_to, local_time, time_zone_id, day_period, meal_relation, is_paused
       ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      plan.id,
      plan.versionId,
      plan.personId,
      plan.medicationDefinitionId,
      plan.dose.numerator,
      plan.dose.denominator,
      plan.kind,
      plan.pattern,
      plan.weekdayMask,
      plan.intervalDays,
      plan.dayOfMonth ?? null,
      plan.intervalMonths ?? null,
      plan.effectiveFrom,
      plan.effectiveTo,
      plan.localTime,
      plan.timeZoneId,
      plan.dayPeriod,
      plan.mealRelation ?? null,
      plan.isPaused ? 1 : 0,
    );
  }
}

async function writeDue(db: SQLiteDatabase, today: TodayResponse, localDate: string): Promise<void> {
  for (const dose of today.due) {
    await db.runAsync(
      `INSERT INTO due_doses (
         plan_version_id, scheduled_for, local_date, plan_id, person_id, medication_id,
         dose_numerator, dose_denominator, kind, local_time, day_period, meal_relation,
         has_enough_stock, recorded_outcome, recorded_administration_id, conflicts
       ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      dose.planVersionId,
      dose.scheduledFor,
      localDate,
      dose.planId,
      dose.personId,
      dose.medicationDefinitionId,
      dose.dose.numerator,
      dose.dose.denominator,
      dose.kind,
      dose.localTime,
      dose.dayPeriod,
      dose.mealRelation,
      dose.hasEnoughStock ? 1 : 0,
      dose.recordedOutcome,
      dose.recordedAdministrationId,
      JSON.stringify(dose.conflicts ?? []),
    );
  }
}

/**
 * The server's three activity streams, one table, each row prefixed by its stream so an
 * id can never collide across them and a correction can still find its dose.
 */
async function writeActivity(db: SQLiteDatabase, activity: ActivityResponse): Promise<void> {
  const insert = `INSERT INTO activity_entries (
     id, kind, occurred_at, recorded_at, person_id, medication_id, detail, stock_source,
     package_label, from_package_label, to_package_label, quantity_numerator,
     quantity_denominator, scheduled_for, lateness_minutes, reason, administration_event_id
   ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`;

  for (const entry of activity.inventory) {
    await db.runAsync(
      insert,
      `inventory:${entry.id}`, 'inventory', entry.occurredAt, entry.recordedAt, null,
      entry.medicationDefinitionId, entry.entryType, null, entry.packageLabel, null, null,
      entry.quantity.numerator, entry.quantity.denominator, null, null, entry.reason, null,
    );
  }

  for (const event of activity.administrations) {
    await db.runAsync(
      insert,
      `administration:${event.id}`, 'administration', event.occurredAt, event.recordedAt,
      event.personId, event.medicationDefinitionId, event.outcome, event.stockSource, null, null,
      null, event.actualQuantity?.numerator ?? null, event.actualQuantity?.denominator ?? null,
      event.scheduledFor, event.latenessMinutes, null, null,
    );
  }

  for (const correction of activity.allocationCorrections) {
    await db.runAsync(
      insert,
      `correction:${correction.id}`, 'correction', correction.recordedAt, correction.recordedAt,
      null, null, null, null, null, correction.fromPackageLabel, correction.toPackageLabel,
      correction.quantity.numerator, correction.quantity.denominator, null, null,
      correction.reason, correction.administrationEventId,
    );
  }
}

/** Every person's and medicine's name by id, archived ones included: a report may name them. */
export async function readNames(db: SQLiteDatabase): Promise<{ people: Map<string, string>; medications: Map<string, string> }> {
  const people = await db.getAllAsync<{ id: string; name: string }>('SELECT id, name FROM people');
  const medications = await db.getAllAsync<{ id: string; name: string }>('SELECT id, name FROM medications');

  return {
    people: new Map(people.map((row) => [row.id, row.name])),
    medications: new Map(medications.map((row) => [row.id, row.name])),
  };
}

export type PersonRow = {
  id: string;
  name: string;
  isArchived: boolean;
  planCount: number;
  pausedCount: number;
};

/** Everyone the household looks after, with their plan counts, names in order. */
export async function readPeople(db: SQLiteDatabase): Promise<PersonRow[]> {
  const rows = await db.getAllAsync<{
    id: string;
    name: string;
    isArchived: number;
    planCount: number;
    pausedCount: number;
  }>(
    `SELECT p.id, p.name, p.is_archived AS isArchived,
            count(pl.id) AS planCount,
            coalesce(sum(pl.is_paused), 0) AS pausedCount
       FROM people p
       LEFT JOIN plans pl ON pl.person_id = p.id
      GROUP BY p.id, p.name, p.is_archived
      ORDER BY p.is_archived, p.name`,
  );

  return rows.map((row) => ({
    id: row.id,
    name: row.name,
    isArchived: row.isArchived === 1,
    planCount: row.planCount,
    pausedCount: row.pausedCount,
  }));
}

export type HistoryInventory = {
  id: string;
  medicationName: string;
  entryType: string;
  packageLabel: number | null;
  quantity: Quantity;
  reason: string | null;
  recordedAt: string;
};

export type HistoryAdministration = {
  id: string;
  personName: string;
  medicationName: string;
  outcome: string;
  stockSource: string;
  quantity: Quantity | null;
  latenessMinutes: number | null;
  occurredAt: string;
};

export type HistoryCorrection = {
  id: string;
  /** Found through the corrected dose when the server's page still holds it. */
  medicationName: string | null;
  fromPackageLabel: number | null;
  toPackageLabel: number | null;
  quantity: Quantity;
  reason: string | null;
  recordedAt: string;
};

/** A dose this device recorded and the server has not acknowledged yet. */
export type HistoryPending = {
  id: string;
  personName: string;
  medicationName: string;
  outcome: string;
  quantity: Quantity | null;
  occurredAt: string;
};

export type History = {
  pending: HistoryPending[];
  /** Commands queued on this device and not yet acknowledged, oldest first. */
  commands: CommandRow[];
  corrections: HistoryCorrection[];
  administrations: HistoryAdministration[];
  inventory: HistoryInventory[];
};

/** The History screen: the cached server page, newest first, plus this device's queued doses. */
export async function readHistory(db: SQLiteDatabase): Promise<History> {
  const quantity = (numerator: number | null, denominator: number | null): Quantity | null =>
    numerator === null || denominator === null ? null : { numerator, denominator };

  const inventory = await db.getAllAsync<{
    id: string; medicationName: string | null; entryType: string; packageLabel: number | null;
    numerator: number; denominator: number; reason: string | null; recordedAt: string;
  }>(
    `SELECT a.id, m.name AS medicationName, a.detail AS entryType, a.package_label AS packageLabel,
            a.quantity_numerator AS numerator, a.quantity_denominator AS denominator,
            a.reason, a.recorded_at AS recordedAt
       FROM activity_entries a
       LEFT JOIN medications m ON m.id = a.medication_id
      WHERE a.kind = 'inventory'
      ORDER BY a.recorded_at DESC`,
  );

  const administrations = await db.getAllAsync<{
    id: string; personName: string | null; medicationName: string | null; outcome: string;
    stockSource: string | null; numerator: number | null; denominator: number | null;
    latenessMinutes: number | null; occurredAt: string;
  }>(
    `SELECT a.id, p.name AS personName, m.name AS medicationName, a.detail AS outcome,
            a.stock_source AS stockSource,
            a.quantity_numerator AS numerator, a.quantity_denominator AS denominator,
            a.lateness_minutes AS latenessMinutes, a.occurred_at AS occurredAt
       FROM activity_entries a
       LEFT JOIN people p      ON p.id = a.person_id
       LEFT JOIN medications m ON m.id = a.medication_id
      WHERE a.kind = 'administration'
      ORDER BY a.recorded_at DESC`,
  );

  const corrections = await db.getAllAsync<{
    id: string; medicationName: string | null; fromPackageLabel: number | null;
    toPackageLabel: number | null; numerator: number; denominator: number;
    reason: string | null; recordedAt: string;
  }>(
    `SELECT c.id, m.name AS medicationName,
            c.from_package_label AS fromPackageLabel, c.to_package_label AS toPackageLabel,
            c.quantity_numerator AS numerator, c.quantity_denominator AS denominator,
            c.reason, c.recorded_at AS recordedAt
       FROM activity_entries c
       LEFT JOIN activity_entries e ON e.id = 'administration:' || c.administration_event_id
       LEFT JOIN medications m      ON m.id = e.medication_id
      WHERE c.kind = 'correction'
      ORDER BY c.recorded_at DESC`,
  );

  // Queued, not yet acknowledged: the outbox still holds the command. A local dose the
  // server refused is shown on the Today screen as a rejection, not here.
  const pending = await db.getAllAsync<{
    id: string; personName: string | null; medicationName: string | null; outcome: string;
    numerator: number | null; denominator: number | null; occurredAt: string;
  }>(
    `SELECT l.id, p.name AS personName, m.name AS medicationName, l.outcome,
            l.quantity_numerator AS numerator, l.quantity_denominator AS denominator,
            l.occurred_at AS occurredAt
       FROM local_doses l
       JOIN outbox o ON o.local_dose_id = l.id
       LEFT JOIN people p      ON p.id = l.person_id
       LEFT JOIN medications m ON m.id = l.medication_id
      WHERE l.server_administration_id IS NULL
      ORDER BY l.occurred_at DESC`,
  );

  return {
    pending: pending.map((row) => ({
      id: row.id,
      personName: row.personName ?? '—',
      medicationName: row.medicationName ?? '—',
      outcome: row.outcome,
      quantity: quantity(row.numerator, row.denominator),
      occurredAt: row.occurredAt,
    })),
    commands: await listCommands(db, 'queued'),
    corrections: corrections.map((row) => ({
      id: row.id,
      medicationName: row.medicationName,
      fromPackageLabel: row.fromPackageLabel,
      toPackageLabel: row.toPackageLabel,
      quantity: { numerator: row.numerator, denominator: row.denominator },
      reason: row.reason,
      recordedAt: row.recordedAt,
    })),
    administrations: administrations.map((row) => ({
      id: row.id,
      personName: row.personName ?? '—',
      medicationName: row.medicationName ?? '—',
      outcome: row.outcome,
      stockSource: row.stockSource ?? '',
      quantity: quantity(row.numerator, row.denominator),
      latenessMinutes: row.latenessMinutes,
      occurredAt: row.occurredAt,
    })),
    inventory: inventory.map((row) => ({
      id: row.id,
      medicationName: row.medicationName ?? '—',
      entryType: row.entryType,
      packageLabel: row.packageLabel,
      quantity: { numerator: row.numerator, denominator: row.denominator },
      reason: row.reason,
      recordedAt: row.recordedAt,
    })),
  };
}

/**
 * The Today list, with local records overlaid on the cached server view.
 *
 * Where the two disagree about the same slot the row is marked conflicted rather than
 * one silently winning. A dose someone took is not a value to be clobbered by whichever
 * write arrived last.
 */
export async function readToday(db: SQLiteDatabase, localDate: string): Promise<LocalDueDose[]> {
  const rows = await db.getAllAsync<{
    planVersionId: string;
    planId: string;
    personId: string;
    personName: string | null;
    medicationId: string;
    medicationName: string | null;
    unit: string | null;
    doseNumerator: number;
    doseDenominator: number;
    kind: 'Scheduled' | 'AsNeeded';
    localTime: string | null;
    dayPeriod: string | null;
    mealRelation: string | null;
    scheduledFor: string | null;
    hasEnoughStock: number;
    conflicts: string;
    serverOutcome: string | null;
    localOutcome: string | null;
    localDoseId: string | null;
    acknowledged: string | null;
  }>(
    `SELECT d.plan_version_id AS planVersionId,
            d.plan_id         AS planId,
            d.person_id       AS personId,
            p.name            AS personName,
            d.medication_id   AS medicationId,
            m.name            AS medicationName,
            m.unit            AS unit,
            d.dose_numerator  AS doseNumerator,
            d.dose_denominator AS doseDenominator,
            d.kind            AS kind,
            d.local_time      AS localTime,
            d.day_period      AS dayPeriod,
            d.meal_relation   AS mealRelation,
            d.scheduled_for   AS scheduledFor,
            d.has_enough_stock AS hasEnoughStock,
            d.conflicts       AS conflicts,
            d.recorded_outcome AS serverOutcome,
            l.outcome         AS localOutcome,
            l.id              AS localDoseId,
            l.server_administration_id AS acknowledged
       FROM due_doses d
       LEFT JOIN people p      ON p.id = d.person_id
       LEFT JOIN medications m ON m.id = d.medication_id
       LEFT JOIN local_doses l ON l.plan_version_id = d.plan_version_id
                              AND (l.scheduled_for IS d.scheduled_for)
      WHERE d.local_date = ?
      ORDER BY coalesce(d.local_time, '99:99'), m.name`,
    localDate,
  );

  const queued = new Set(
    (await db.getAllAsync<{ localDoseId: string }>('SELECT local_dose_id AS localDoseId FROM outbox'))
      .map((row) => row.localDoseId),
  );

  return rows.map((row) => ({
    planVersionId: row.planVersionId,
    planId: row.planId,
    personId: row.personId,
    personName: row.personName ?? '—',
    medicationId: row.medicationId,
    medicationName: row.medicationName ?? '—',
    unit: row.unit ?? '',
    dose: { numerator: row.doseNumerator, denominator: row.doseDenominator },
    kind: row.kind,
    localTime: row.localTime,
    dayPeriod: row.dayPeriod,
    mealRelation: row.mealRelation,
    scheduledFor: row.scheduledFor,
    hasEnoughStock: row.hasEnoughStock === 1,
    conflicts: parseConflicts(row.conflicts),
    serverOutcome: row.serverOutcome,
    localOutcome: row.localOutcome,
    localDoseId: row.localDoseId,
    // Both sides recorded the slot and they do not agree. Surfaced, never resolved.
    isConflicted:
      row.serverOutcome !== null &&
      row.localOutcome !== null &&
      row.acknowledged === null &&
      row.serverOutcome !== row.localOutcome,
    isPending: row.localDoseId !== null && queued.has(row.localDoseId),
  }));
}

/** A warning the phone cannot read is dropped, never turned into a crash on the Today screen. */
function parseConflicts(value: string | null): DoseConflict[] {
  try {
    const parsed: unknown = JSON.parse(value ?? '[]');
    return Array.isArray(parsed) ? (parsed as DoseConflict[]) : [];
  } catch {
    return [];
  }
}

export async function readStock(db: SQLiteDatabase): Promise<MedicationStock[]> {
  const rows = await db.getAllAsync<{
    id: string;
    name: string;
    strength: string | null;
    unit: string;
    totalNumerator: number;
    totalDenominator: number;
    packageCount: number;
  }>(
    `SELECT id, name, strength, unit,
            total_numerator AS totalNumerator,
            total_denominator AS totalDenominator,
            package_count AS packageCount
       FROM medications
      WHERE is_archived = 0
      ORDER BY name`,
  );

  return rows.map((row) => ({
    id: row.id,
    name: row.name,
    strength: row.strength,
    unit: row.unit,
    total: { numerator: row.totalNumerator, denominator: row.totalDenominator },
    packageCount: row.packageCount,
  }));
}

export type StockPackage = {
  id: string;
  ordinal: number;
  label: string | null;
  state: string;
  remaining: Quantity;
  capacity: Quantity;
  expiresOn: string | null;
  holderName: string | null;
  isPinned: boolean;
  coverage: string | null;
};

export type StockRow = MedicationStock & {
  coverage: string | null;
  /** The catalogue's usual box size, offered when stock is added; null when never set. */
  defaultCapacity: Quantity | null;
  /** What is not inside any sealed or opened box: the total less those boxes. */
  loose: Quantity;
  packages: StockPackage[];
};

/**
 * The Stock screen's rows: every unarchived medication with its boxes, retired ones
 * last, and the loose amount worked out from the cached total exactly, never in floating
 * point.
 */
export async function readStockWithPackages(db: SQLiteDatabase): Promise<StockRow[]> {
  const medications = await db.getAllAsync<{
    id: string;
    name: string;
    strength: string | null;
    unit: string;
    totalNumerator: number;
    totalDenominator: number;
    packageCount: number;
    coverage: string | null;
    defaultCapacityNumerator: number | null;
    defaultCapacityDenominator: number | null;
  }>(
    `SELECT id, name, strength, unit,
            total_numerator AS totalNumerator,
            total_denominator AS totalDenominator,
            package_count AS packageCount,
            coverage,
            default_capacity_numerator AS defaultCapacityNumerator,
            default_capacity_denominator AS defaultCapacityDenominator
       FROM medications
      WHERE is_archived = 0
      ORDER BY name`,
  );

  const packages = await db.getAllAsync<{
    id: string;
    medicationId: string;
    ordinal: number;
    label: string | null;
    state: string;
    remainingNumerator: number;
    remainingDenominator: number;
    capacityNumerator: number;
    capacityDenominator: number;
    expiresOn: string | null;
    holderName: string | null;
    isPinned: number;
    coverage: string | null;
  }>(
    `SELECT pk.id, pk.medication_id AS medicationId, pk.ordinal, pk.label, pk.state,
            pk.remaining_numerator AS remainingNumerator,
            pk.remaining_denominator AS remainingDenominator,
            pk.capacity_numerator AS capacityNumerator,
            pk.capacity_denominator AS capacityDenominator,
            pk.expires_on AS expiresOn,
            p.name AS holderName,
            pk.is_pinned AS isPinned,
            pk.coverage
       FROM packages pk
       LEFT JOIN people p ON p.id = pk.holder_person_id
      ORDER BY CASE pk.state WHEN 'Opened' THEN 0 WHEN 'Sealed' THEN 1 ELSE 2 END, pk.ordinal`,
  );

  return medications.map((medication) => {
    const boxes: StockPackage[] = packages
      .filter((row) => row.medicationId === medication.id)
      .map((row) => ({
        id: row.id,
        ordinal: row.ordinal,
        label: row.label,
        state: row.state,
        remaining: { numerator: row.remainingNumerator, denominator: row.remainingDenominator },
        capacity: { numerator: row.capacityNumerator, denominator: row.capacityDenominator },
        expiresOn: row.expiresOn,
        holderName: row.holderName,
        isPinned: row.isPinned === 1,
        coverage: row.coverage,
      }));

    const total = { numerator: medication.totalNumerator, denominator: medication.totalDenominator };
    const boxed = addQuantities(
      boxes.filter((box) => box.state === 'Sealed' || box.state === 'Opened').map((box) => box.remaining),
    );
    const loose = subtractQuantity(total, boxed);

    return {
      id: medication.id,
      name: medication.name,
      strength: medication.strength,
      unit: medication.unit,
      total,
      packageCount: medication.packageCount,
      coverage: medication.coverage,
      defaultCapacity:
        medication.defaultCapacityNumerator !== null && medication.defaultCapacityDenominator !== null
          ? { numerator: medication.defaultCapacityNumerator, denominator: medication.defaultCapacityDenominator }
          : null,
      loose: compareQuantities(loose, ZERO) < 0 ? ZERO : loose,
      packages: boxes,
    };
  });
}

export type PlanRow = PlanShape & {
  id: string;
  versionId: string;
  personId: string;
  medicationId: string;
  personName: string;
  medicationName: string;
  unit: string;
  dose: Quantity;
  timeZoneId: string;
  mealRelation: string | null;
};

/** Every cached plan with the names the screen shows, people in alphabetical order. */
export async function readPlans(db: SQLiteDatabase): Promise<PlanRow[]> {
  const rows = await db.getAllAsync<{
    id: string;
    versionId: string;
    personId: string;
    medicationId: string;
    personName: string | null;
    medicationName: string | null;
    unit: string | null;
    doseNumerator: number;
    doseDenominator: number;
    kind: 'Scheduled' | 'AsNeeded';
    pattern: PlanShape['pattern'];
    weekdayMask: number | null;
    intervalDays: number | null;
    dayOfMonth: number | null;
    intervalMonths: number | null;
    effectiveFrom: string | null;
    effectiveTo: string | null;
    localTime: string | null;
    timeZoneId: string;
    dayPeriod: string | null;
    mealRelation: string | null;
    isPaused: number;
  }>(
    `SELECT pl.id, pl.version_id AS versionId,
            pl.person_id AS personId, pl.medication_id AS medicationId,
            p.name AS personName, m.name AS medicationName, m.unit AS unit,
            pl.dose_numerator AS doseNumerator, pl.dose_denominator AS doseDenominator,
            pl.kind, pl.pattern,
            pl.weekday_mask AS weekdayMask, pl.interval_days AS intervalDays,
            pl.day_of_month AS dayOfMonth, pl.interval_months AS intervalMonths,
            pl.effective_from AS effectiveFrom, pl.effective_to AS effectiveTo,
            pl.local_time AS localTime, pl.time_zone_id AS timeZoneId,
            pl.day_period AS dayPeriod, pl.meal_relation AS mealRelation,
            pl.is_paused AS isPaused
       FROM plans pl
       LEFT JOIN people p      ON p.id = pl.person_id
       LEFT JOIN medications m ON m.id = pl.medication_id
      ORDER BY p.name, m.name, coalesce(pl.local_time, '99:99')`,
  );

  return rows.map((row) => ({
    id: row.id,
    versionId: row.versionId,
    personId: row.personId,
    medicationId: row.medicationId,
    personName: row.personName ?? '—',
    medicationName: row.medicationName ?? '—',
    unit: row.unit ?? '',
    dose: { numerator: row.doseNumerator, denominator: row.doseDenominator },
    kind: row.kind,
    pattern: row.pattern,
    weekdayMask: row.weekdayMask,
    intervalDays: row.intervalDays,
    dayOfMonth: row.dayOfMonth,
    intervalMonths: row.intervalMonths,
    effectiveFrom: row.effectiveFrom,
    effectiveTo: row.effectiveTo,
    localTime: row.localTime,
    timeZoneId: row.timeZoneId,
    dayPeriod: row.dayPeriod,
    mealRelation: row.mealRelation,
    isPaused: row.isPaused === 1,
  }));
}

/** Packages a dose may be drawn from, for the advanced source picker. */
export async function readPackageOptions(
  db: SQLiteDatabase,
  medicationId: string,
): Promise<PackageOption[]> {
  const rows = await db.getAllAsync<{
    id: string;
    medicationId: string;
    ordinal: number;
    state: string;
    remainingNumerator: number;
    remainingDenominator: number;
    capacityNumerator: number;
    capacityDenominator: number;
    isPinned: number;
  }>(
    `SELECT id, medication_id AS medicationId, ordinal, state,
            remaining_numerator AS remainingNumerator,
            remaining_denominator AS remainingDenominator,
            capacity_numerator AS capacityNumerator,
            capacity_denominator AS capacityDenominator,
            is_pinned AS isPinned
       FROM packages
      WHERE medication_id = ? AND state IN ('Sealed', 'Opened')
      ORDER BY ordinal`,
    medicationId,
  );

  return rows.map((row) => ({
    id: row.id,
    medicationId: row.medicationId,
    ordinal: row.ordinal,
    state: row.state,
    remaining: { numerator: row.remainingNumerator, denominator: row.remainingDenominator },
    capacity: { numerator: row.capacityNumerator, denominator: row.capacityDenominator },
    isPinned: row.isPinned === 1,
  }));
}

/** Reads the cached plans in the shape the reminder scheduler needs. */
export async function readReminderPlans(db: SQLiteDatabase): Promise<ReminderPlan[]> {
  const rows = await db.getAllAsync<{
    planVersionId: string;
    medicationName: string | null;
    personName: string | null;
    doseNumerator: number;
    doseDenominator: number;
    kind: 'Scheduled' | 'AsNeeded';
    pattern: RecurrencePattern;
    weekdayMask: number | null;
    intervalDays: number | null;
    dayOfMonth: number | null;
    intervalMonths: number | null;
    effectiveFrom: string | null;
    effectiveTo: string | null;
    localTime: string | null;
    timeZoneId: string;
    isPaused: number;
  }>(
    `SELECT pl.version_id AS planVersionId,
            m.name        AS medicationName,
            p.name        AS personName,
            pl.dose_numerator AS doseNumerator,
            pl.dose_denominator AS doseDenominator,
            pl.kind, pl.pattern,
            pl.weekday_mask AS weekdayMask,
            pl.interval_days AS intervalDays,
            pl.day_of_month AS dayOfMonth,
            pl.interval_months AS intervalMonths,
            pl.effective_from AS effectiveFrom,
            pl.effective_to AS effectiveTo,
            pl.local_time AS localTime,
            pl.time_zone_id AS timeZoneId,
            pl.is_paused AS isPaused
       FROM plans pl
       LEFT JOIN medications m ON m.id = pl.medication_id
       LEFT JOIN people p      ON p.id = pl.person_id
      -- An archived person's plans were kept out of the server's Today list in the same
      -- slice that made archiving pause them. The phone filtered the medication but not
      -- the person, so a household member who had been archived could still be reminded
      -- by name from a snapshot taken before that cascade existed.
      WHERE (m.is_archived = 0 OR m.is_archived IS NULL)
        AND (p.is_archived = 0 OR p.is_archived IS NULL)`,
  );

  return rows.map((row) => ({
    planVersionId: row.planVersionId,
    medicationName: row.medicationName ?? '—',
    personName: row.personName ?? '—',
    doseLabel: formatQuantity({ numerator: row.doseNumerator, denominator: row.doseDenominator }),
    kind: row.kind,
    pattern: row.pattern,
    weekdayMask: row.weekdayMask,
    intervalDays: row.intervalDays,
    dayOfMonth: row.dayOfMonth,
    intervalMonths: row.intervalMonths,
    effectiveFrom: row.effectiveFrom,
    effectiveTo: row.effectiveTo,
    localTime: row.localTime,
    isPaused: row.isPaused === 1,
    timeZoneId: row.timeZoneId,
  }));
}
