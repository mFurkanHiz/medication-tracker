import type { SQLiteDatabase } from 'expo-sqlite';
import { api, type ApiConfig, type TodayResponse, type WorkspaceResponse } from '../lib/api';
import type { Quantity } from '../lib/quantity';
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
  const [workspace, today] = await Promise.all([
    api.workspace(config, householdId),
    api.today(config, householdId, localDate),
  ]);

  await db.withTransactionAsync(async () => {
    await db.execAsync('DELETE FROM people; DELETE FROM medications; DELETE FROM packages; DELETE FROM plans; DELETE FROM due_doses;');

    await writePeople(db, workspace);
    await writeMedications(db, workspace);
    await writePlans(db, workspace);
    await writeDue(db, today, localDate);
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
         id, name, strength, unit, is_archived, total_numerator, total_denominator, package_count
       ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
      medication.id,
      medication.name,
      medication.strength,
      medication.unit,
      medication.isArchived ? 1 : 0,
      medication.total.numerator,
      medication.total.denominator,
      medication.packageCount,
    );

    for (const entry of medication.packages) {
      const view = entry.view;
      await db.runAsync(
        `INSERT INTO packages (
           id, medication_id, ordinal, state, remaining_numerator, remaining_denominator,
           capacity_numerator, capacity_denominator, holder_person_id, is_pinned
         ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
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
      );
    }
  }
}

async function writePlans(db: SQLiteDatabase, workspace: WorkspaceResponse): Promise<void> {
  for (const plan of workspace.plans) {
    await db.runAsync(
      `INSERT INTO plans (
         id, version_id, person_id, medication_id, dose_numerator, dose_denominator,
         kind, pattern, weekday_mask, interval_days, effective_from, effective_to,
         local_time, time_zone_id, day_period, is_paused
       ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
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
      plan.effectiveFrom,
      plan.effectiveTo,
      plan.localTime,
      plan.timeZoneId,
      plan.dayPeriod,
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
         has_enough_stock, recorded_outcome, recorded_administration_id
       ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
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
    );
  }
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
