import type { SQLiteDatabase } from 'expo-sqlite';
import type { CountLineInput } from '../lib/api';
import { defaultUnitFor } from '../lib/commands';
import { ZERO, addQuantities, compareQuantities, subtractQuantity, type Quantity } from '../lib/quantity';

/**
 * The command queue's SQL: what is waiting, what was refused, and what each queued
 * command does to the cached snapshot in the meantime.
 *
 * A command is applied to the local snapshot the moment it is queued — the household
 * decided, and the phone should show that decision before the server has heard it. The
 * snapshot is replaced wholesale on every refresh, so the effects of everything still
 * queued are applied again after each one; a refused command is left out, which is how
 * its effect is undone.
 *
 * A create is the same rule the other way round: the person, medicine or plan is a
 * provisional row under the phone's own id until the server lists it, and a refused
 * create disappears with the next refresh.
 *
 * No expo-crypto here, so the module runs under node for the migration and query checks.
 */

export type CommandRow = {
  idempotencyKey: string;
  kind: string;
  targetId: string;
  medicationId: string | null;
  payload: string;
  createdAt: string;
  attempts: number;
  lastError: string | null;
  /** Set once the server refused the command; null while it is still queued. */
  rejectedCode: string | null;
  rejectedStatus: number | null;
  rejectedAt: string | null;
  medicationName: string | null;
  personName: string | null;
  packageLabel: string | null;
  packageOrdinal: number | null;
};

/** Queued or refused commands, oldest first, with the names the screens show. */
export async function listCommands(db: SQLiteDatabase, which: 'queued' | 'rejected'): Promise<CommandRow[]> {
  return db.getAllAsync<CommandRow>(
    `SELECT c.idempotency_key AS idempotencyKey, c.kind, c.target_id AS targetId,
            c.medication_id AS medicationId, c.payload, c.created_at AS createdAt,
            c.attempts, c.last_error AS lastError,
            c.rejected_code AS rejectedCode, c.rejected_status AS rejectedStatus, c.rejected_at AS rejectedAt,
            m.name AS medicationName, coalesce(pp.name, pc.name) AS personName,
            pk.label AS packageLabel, pk.ordinal AS packageOrdinal
       FROM commands c
       LEFT JOIN medications m ON m.id = c.medication_id
       LEFT JOIN plans pl      ON pl.id = c.target_id AND c.kind LIKE 'plan.%'
       LEFT JOIN people pp     ON pp.id = pl.person_id
       LEFT JOIN people pc     ON pc.id = c.target_id AND c.kind = 'person.create'
       LEFT JOIN packages pk   ON (pk.id = c.target_id AND c.kind LIKE 'package.%')
                               OR (pk.active_loan_id = c.target_id AND c.kind = 'loan.return')
      WHERE c.rejected_at IS ${which === 'queued' ? '' : 'NOT '}NULL
      ORDER BY c.created_at`,
  );
}

/** How many commands are still waiting for the server. */
export async function queuedCommandCount(db: SQLiteDatabase): Promise<number> {
  const row = await db.getFirstAsync<{ count: number }>(
    'SELECT count(*) AS count FROM commands WHERE rejected_at IS NULL',
  );
  return row?.count ?? 0;
}

/**
 * What the command will do, done to the snapshot now.
 *
 * Only what the phone can know is written: a pause flips the plan and drops its due
 * rows (the server would list none for a paused plan); a pin moves the badge; marking a
 * box lost or disposed empties it and takes its amount off the medicine's total, as the
 * server's ledger entry will. Added stock has no local shape until the server names the
 * boxes, so it waits as a pending card instead.
 */
export async function applyLocalEffect(db: SQLiteDatabase, command: Pick<CommandRow, 'kind' | 'targetId' | 'payload'>): Promise<void> {
  const body = JSON.parse(command.payload) as Record<string, unknown>;

  switch (command.kind) {
    case 'plan.pause': {
      const paused = body.isPaused ? 1 : 0;
      await db.runAsync('UPDATE plans SET is_paused = ? WHERE id = ?', paused, command.targetId);
      if (paused) {
        await db.runAsync('DELETE FROM due_doses WHERE plan_id = ?', command.targetId);
      }
      return;
    }

    case 'package.pin': {
      await db.runAsync(
        'UPDATE packages SET is_pinned = 0 WHERE medication_id = (SELECT medication_id FROM packages WHERE id = ?)',
        command.targetId,
      );
      await db.runAsync('UPDATE packages SET is_pinned = 1 WHERE id = ?', command.targetId);
      return;
    }

    case 'package.retire': {
      const box = await db.getFirstAsync<{
        medicationId: string;
        state: string;
        remainingNumerator: number;
        remainingDenominator: number;
      }>(
        `SELECT medication_id AS medicationId, state,
                remaining_numerator AS remainingNumerator, remaining_denominator AS remainingDenominator
           FROM packages WHERE id = ?`,
        command.targetId,
      );

      // Already retired in the snapshot: nothing left to take off.
      if (!box || (box.state !== 'Sealed' && box.state !== 'Opened')) {
        return;
      }

      const medication = await db.getFirstAsync<{
        totalNumerator: number;
        totalDenominator: number;
        packageCount: number;
      }>(
        `SELECT total_numerator AS totalNumerator, total_denominator AS totalDenominator,
                package_count AS packageCount
           FROM medications WHERE id = ?`,
        box.medicationId,
      );

      if (medication) {
        const total = subtractQuantity(
          { numerator: medication.totalNumerator, denominator: medication.totalDenominator },
          { numerator: box.remainingNumerator, denominator: box.remainingDenominator },
        );
        const clamped = compareQuantities(total, ZERO) < 0 ? ZERO : total;

        await db.runAsync(
          'UPDATE medications SET total_numerator = ?, total_denominator = ?, package_count = max(0, package_count - 1) WHERE id = ?',
          clamped.numerator,
          clamped.denominator,
          box.medicationId,
        );
      }

      await db.runAsync(
        `UPDATE packages SET state = ?, is_pinned = 0, remaining_numerator = 0, remaining_denominator = 1
          WHERE id = ?`,
        String(body.state ?? 'Lost'),
        command.targetId,
      );
      return;
    }

    case 'plan.end': {
      const endsOn = String(body.endsOn ?? '');
      await db.runAsync('UPDATE plans SET effective_to = ? WHERE id = ?', endsOn, command.targetId);
      // Days after the last day are no longer owed; the last day itself still is.
      await db.runAsync('DELETE FROM due_doses WHERE plan_id = ? AND local_date > ?', command.targetId, endsOn);
      return;
    }

    case 'plan.restart': {
      // On the server a restart appends a version that starts on the day, and the ended
      // version keeps governing until then, so the days in between still ask for nothing.
      // The same here: the latest version goes to the history (already there when the
      // server listed it) and the plan row becomes the new one, under a pending id until
      // the server names it.
      const current = await db.getFirstAsync<{
        versionId: string;
        versionNumber: number;
        doseNumerator: number;
        doseDenominator: number;
        kind: string;
        pattern: string;
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
        `SELECT version_id AS versionId, version_number AS versionNumber,
                dose_numerator AS doseNumerator, dose_denominator AS doseDenominator, kind, pattern,
                weekday_mask AS weekdayMask, interval_days AS intervalDays,
                day_of_month AS dayOfMonth, interval_months AS intervalMonths,
                effective_from AS effectiveFrom, effective_to AS effectiveTo,
                local_time AS localTime, time_zone_id AS timeZoneId, is_paused AS isPaused
           FROM plans WHERE id = ?`,
        command.targetId,
      );

      if (!current) {
        return;
      }

      await db.runAsync(
        `INSERT OR IGNORE INTO plan_versions (
           version_id, plan_id, version_number, dose_numerator, dose_denominator, kind, pattern,
           weekday_mask, interval_days, day_of_month, interval_months, effective_from, effective_to,
           local_time, time_zone_id, is_paused
         ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
        current.versionId,
        command.targetId,
        current.versionNumber,
        current.doseNumerator,
        current.doseDenominator,
        current.kind,
        current.pattern,
        current.weekdayMask,
        current.intervalDays,
        current.dayOfMonth,
        current.intervalMonths,
        current.effectiveFrom,
        current.effectiveTo,
        current.localTime,
        current.timeZoneId,
        current.isPaused,
      );
      await db.runAsync(
        `UPDATE plans SET version_id = ?, version_number = version_number + 1,
                          effective_from = ?, effective_to = NULL, is_paused = 0
          WHERE id = ?`,
        `pending:${command.targetId}`,
        String(body.startsOn ?? ''),
        command.targetId,
      );
      return;
    }

    case 'package.unpin': {
      await db.runAsync('UPDATE packages SET is_pinned = 0 WHERE id = ?', command.targetId);
      return;
    }

    case 'package.reinstate': {
      const box = await db.getFirstAsync<{
        medicationId: string;
        ordinal: number;
        state: string;
        capacityNumerator: number;
        capacityDenominator: number;
      }>(
        `SELECT medication_id AS medicationId, ordinal, state,
                capacity_numerator AS capacityNumerator, capacity_denominator AS capacityDenominator
           FROM packages WHERE id = ?`,
        command.targetId,
      );

      if (!box || box.state === 'Sealed' || box.state === 'Opened') {
        return;
      }

      // What the retirement took out, from the cached ledger: the latest loss or disposal
      // of this box. The server puts exactly that back; so does the phone, when it knows it.
      const taken = await db.getFirstAsync<{ numerator: number; denominator: number }>(
        `SELECT quantity_numerator AS numerator, quantity_denominator AS denominator
           FROM activity_entries
          WHERE kind = 'inventory' AND medication_id = ? AND package_label = ? AND detail IN ('Loss', 'Dispose')
          ORDER BY recorded_at DESC LIMIT 1`,
        box.medicationId,
        box.ordinal,
      );
      const restored: Quantity =
        taken && taken.numerator < 0 ? { numerator: -taken.numerator, denominator: taken.denominator } : ZERO;
      const capacity: Quantity = { numerator: box.capacityNumerator, denominator: box.capacityDenominator };

      await db.runAsync(
        'UPDATE packages SET state = ?, remaining_numerator = ?, remaining_denominator = ? WHERE id = ?',
        compareQuantities(restored, capacity) < 0 ? 'Opened' : 'Sealed',
        restored.numerator,
        restored.denominator,
        command.targetId,
      );

      const medication = await db.getFirstAsync<{ totalNumerator: number; totalDenominator: number }>(
        'SELECT total_numerator AS totalNumerator, total_denominator AS totalDenominator FROM medications WHERE id = ?',
        box.medicationId,
      );

      if (medication) {
        const total = addQuantities([
          { numerator: medication.totalNumerator, denominator: medication.totalDenominator },
          restored,
        ]);
        await db.runAsync(
          'UPDATE medications SET total_numerator = ?, total_denominator = ?, package_count = package_count + 1 WHERE id = ?',
          total.numerator,
          total.denominator,
          box.medicationId,
        );
      }
      return;
    }

    case 'package.update': {
      const text = (value: unknown) => (typeof value === 'string' && value.trim() !== '' ? value : null);
      await db.runAsync(
        `UPDATE packages SET label = ?, coverage = ?, expires_on = ?, acquired_on = ?, lot_number = ?,
                             barcode = ?, source = ?, storage_location = ?, note = ?
          WHERE id = ?`,
        text(body.label),
        text(body.coverage),
        text(body.expiresOn),
        text(body.acquiredOn),
        text(body.lotNumber),
        text(body.barcode),
        text(body.source),
        text(body.storageLocation),
        text(body.note),
        command.targetId,
      );
      return;
    }

    case 'package.assign': {
      const personId = typeof body.personId === 'string' ? body.personId : null;
      await db.runAsync(
        'UPDATE packages SET owner_person_id = ?, holder_person_id = ? WHERE id = ?',
        personId,
        personId,
        command.targetId,
      );
      return;
    }

    case 'package.lend': {
      // The loan's id is the server's to give; until it has, the box shows as on loan
      // and cannot be returned from the phone.
      await db.runAsync(
        "UPDATE packages SET holder_person_id = ?, active_loan_id = 'pending' WHERE id = ?",
        String(body.borrowerPersonId ?? ''),
        command.targetId,
      );
      return;
    }

    case 'loan.return': {
      await db.runAsync(
        'UPDATE packages SET holder_person_id = owner_person_id, active_loan_id = NULL WHERE id = ?',
        String(body.packageId ?? ''),
      );
      return;
    }

    case 'inventory.count': {
      // The server reconciles each line to what was found: a box line sets that box and
      // moves the medicine's total by the difference; a whole-medicine line sets the
      // total (the ledger entry lands on the loose stock). Done here the same way.
      const lines = Array.isArray(body.lines) ? (body.lines as CountLineInput[]) : [];

      for (const line of lines) {
        const observed: Quantity = { numerator: line.observedNumerator, denominator: line.observedDenominator || 1 };

        if (line.packageId) {
          const box = await db.getFirstAsync<{ medicationId: string; remainingNumerator: number; remainingDenominator: number }>(
            `SELECT medication_id AS medicationId, remaining_numerator AS remainingNumerator,
                    remaining_denominator AS remainingDenominator
               FROM packages WHERE id = ?`,
            line.packageId,
          );

          if (!box) {
            continue;
          }

          const medication = await db.getFirstAsync<{ totalNumerator: number; totalDenominator: number }>(
            'SELECT total_numerator AS totalNumerator, total_denominator AS totalDenominator FROM medications WHERE id = ?',
            box.medicationId,
          );

          if (medication) {
            const difference = subtractQuantity(observed, {
              numerator: box.remainingNumerator,
              denominator: box.remainingDenominator,
            });
            const total = addQuantities([
              { numerator: medication.totalNumerator, denominator: medication.totalDenominator },
              difference,
            ]);
            const clamped = compareQuantities(total, ZERO) < 0 ? ZERO : total;

            await db.runAsync(
              'UPDATE medications SET total_numerator = ?, total_denominator = ? WHERE id = ?',
              clamped.numerator,
              clamped.denominator,
              box.medicationId,
            );
          }

          await db.runAsync(
            'UPDATE packages SET remaining_numerator = ?, remaining_denominator = ? WHERE id = ?',
            observed.numerator,
            observed.denominator,
            line.packageId,
          );
        } else {
          await db.runAsync(
            'UPDATE medications SET total_numerator = ?, total_denominator = ? WHERE id = ?',
            observed.numerator,
            observed.denominator,
            line.medicationDefinitionId,
          );
        }
      }
      return;
    }

    // The creates: a provisional row under the phone's own id. OR IGNORE, because a
    // refresh after a send whose answer was lost already lists the row under that id.
    case 'person.create': {
      await db.runAsync(
        'INSERT OR IGNORE INTO people (id, name, is_archived) VALUES (?, ?, 0)',
        command.targetId,
        String(body.name ?? ''),
      );
      return;
    }

    case 'medication.create': {
      const text = (value: unknown) => (typeof value === 'string' && value !== '' ? value : null);
      const count = (value: unknown) => (typeof value === 'number' ? value : null);
      await db.runAsync(
        `INSERT OR IGNORE INTO medications (
           id, name, strength, unit, is_archived, total_numerator, total_denominator, package_count, coverage,
           default_capacity_numerator, default_capacity_denominator
         ) VALUES (?, ?, ?, ?, 0, 0, 1, 0, ?, ?, ?)`,
        command.targetId,
        String(body.name ?? ''),
        text(body.strength),
        text(body.unit) ?? defaultUnitFor(String(body.form ?? '')),
        text(body.coverage),
        count(body.defaultPackageCapacityNumerator),
        count(body.defaultPackageCapacityDenominator),
      );
      return;
    }

    case 'plan.create': {
      const text = (value: unknown) => (typeof value === 'string' && value !== '' ? value : null);
      const count = (value: unknown) => (typeof value === 'number' ? value : null);
      // The version id is the server's to give; marked until it has, so nothing reads
      // the provisional plan as a version a dose could be recorded against.
      await db.runAsync(
        `INSERT OR IGNORE INTO plans (
           id, version_id, person_id, medication_id, dose_numerator, dose_denominator,
           kind, pattern, weekday_mask, interval_days, day_of_month, interval_months,
           effective_from, effective_to, local_time, time_zone_id, day_period, meal_relation, is_paused
         ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0)`,
        command.targetId,
        `pending:${command.targetId}`,
        String(body.personId ?? ''),
        String(body.medicationDefinitionId ?? ''),
        count(body.doseNumerator) ?? 1,
        count(body.doseDenominator) ?? 1,
        body.kind === 'AsNeeded' ? 'AsNeeded' : 'Scheduled',
        text(body.pattern) ?? 'Daily',
        count(body.weekdayMask),
        count(body.intervalDays),
        count(body.dayOfMonth),
        count(body.intervalMonths),
        text(body.effectiveFrom),
        text(body.effectiveTo),
        text(body.localTime),
        text(body.timeZoneId) ?? 'UTC',
        text(body.dayPeriod),
        text(body.mealRelation),
      );
      return;
    }

    default:
      return;
  }
}

/** Re-applies every queued command's effect, in order, over a freshly written snapshot. */
export async function applyQueuedEffects(db: SQLiteDatabase): Promise<void> {
  const queued = await db.getAllAsync<Pick<CommandRow, 'kind' | 'targetId' | 'payload'>>(
    `SELECT kind, target_id AS targetId, payload FROM commands WHERE rejected_at IS NULL ORDER BY created_at`,
  );

  for (const command of queued) {
    await applyLocalEffect(db, command);
  }
}
