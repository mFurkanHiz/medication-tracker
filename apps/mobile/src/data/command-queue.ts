import type { SQLiteDatabase } from 'expo-sqlite';
import { ZERO, compareQuantities, subtractQuantity } from '../lib/quantity';

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
            m.name AS medicationName, pp.name AS personName,
            pk.label AS packageLabel, pk.ordinal AS packageOrdinal
       FROM commands c
       LEFT JOIN medications m ON m.id = c.medication_id
       LEFT JOIN plans pl      ON pl.id = c.target_id AND c.kind = 'plan.pause'
       LEFT JOIN people pp     ON pp.id = pl.person_id
       LEFT JOIN packages pk   ON pk.id = c.target_id AND c.kind IN ('package.pin', 'package.retire')
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
