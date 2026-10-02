import * as Crypto from 'expo-crypto';
import type { SQLiteDatabase } from 'expo-sqlite';
import { ApiError, NetworkError, api, type ApiConfig, type DoseSource, type RecordDoseRequest } from '../lib/api';
import type { Quantity } from '../lib/quantity';

/**
 * The durable command queue.
 *
 * The ordering rule the whole offline story rests on: a dose is written to SQLite and
 * committed *before* any network call is attempted, together with the idempotency key
 * that request will carry. If the app is killed between the commit and the response,
 * the retry sends the same key and the server returns the original event instead of
 * consuming stock a second time.
 */

export type RecordedDoseInput = {
  planVersionId: string | null;
  personId: string;
  medicationDefinitionId: string;
  outcome: 'Taken' | 'Skipped' | 'PartialDose' | 'ExtraDose';
  source: DoseSource;
  packageId: string | null;
  quantity: Quantity | null;
  scheduledFor: string | null;
  occurredAt: string;
  note: string | null;
};

export type PendingDose = {
  idempotencyKey: string;
  localDoseId: string;
  planVersionId: string | null;
  scheduledFor: string | null;
  outcome: string;
  attempts: number;
  lastError: string | null;
};

export type Rejection = {
  idempotencyKey: string;
  localDoseId: string;
  code: string;
  status: number;
  recordedAt: string;
  planVersionId: string | null;
  scheduledFor: string | null;
  medicationDefinitionId: string;
  outcome: string;
};

/**
 * Records a dose locally and queues it, in one transaction.
 *
 * Returns the local identifier. The caller does not wait for the network: the record
 * already exists and the queue will deliver it.
 */
export async function enqueueDose(
  db: SQLiteDatabase,
  input: RecordedDoseInput,
  now: string,
): Promise<{ localDoseId: string; idempotencyKey: string }> {
  const localDoseId = Crypto.randomUUID();
  const idempotencyKey = Crypto.randomUUID();

  const payload: RecordDoseRequest = {
    // A scheduled dose claims its slot; an extra or unplanned one names the person and
    // medication instead, because it belongs to no slot.
    ...(input.planVersionId && input.outcome !== 'ExtraDose'
      ? { planVersionId: input.planVersionId, scheduledFor: input.scheduledFor }
      : { personId: input.personId, medicationDefinitionId: input.medicationDefinitionId }),
    outcome: input.outcome,
    source: input.source,
    packageId: input.source === 'SpecificPackage' ? input.packageId : null,
    actualQuantityNumerator: input.quantity?.numerator ?? null,
    actualQuantityDenominator: input.quantity?.denominator ?? null,
    occurredAt: input.occurredAt,
    idempotencyKey,
    note: input.note,
  };

  await db.withTransactionAsync(async () => {
    await db.runAsync(
      `INSERT INTO local_doses (
         id, plan_version_id, person_id, medication_id, outcome, source, package_id,
         quantity_numerator, quantity_denominator, scheduled_for, occurred_at, recorded_at
       ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      localDoseId,
      input.planVersionId,
      input.personId,
      input.medicationDefinitionId,
      input.outcome,
      input.source,
      input.packageId,
      input.quantity?.numerator ?? null,
      input.quantity?.denominator ?? null,
      input.scheduledFor,
      input.occurredAt,
      now,
    );

    await db.runAsync(
      'INSERT INTO outbox (idempotency_key, local_dose_id, payload, created_at) VALUES (?, ?, ?, ?)',
      idempotencyKey,
      localDoseId,
      JSON.stringify(payload),
      now,
    );
  });

  return { localDoseId, idempotencyKey };
}

export async function pendingDoses(db: SQLiteDatabase): Promise<PendingDose[]> {
  return db.getAllAsync<PendingDose>(
    `SELECT o.idempotency_key AS idempotencyKey,
            o.local_dose_id   AS localDoseId,
            d.plan_version_id AS planVersionId,
            d.scheduled_for   AS scheduledFor,
            d.outcome         AS outcome,
            o.attempts        AS attempts,
            o.last_error      AS lastError
       FROM outbox o
       JOIN local_doses d ON d.id = o.local_dose_id
      ORDER BY o.created_at`,
  );
}

export async function pendingCount(db: SQLiteDatabase): Promise<number> {
  const row = await db.getFirstAsync<{ count: number }>('SELECT count(*) AS count FROM outbox');
  return row?.count ?? 0;
}

export async function openRejections(db: SQLiteDatabase): Promise<Rejection[]> {
  return db.getAllAsync<Rejection>(
    `SELECT r.idempotency_key AS idempotencyKey,
            r.local_dose_id   AS localDoseId,
            r.code            AS code,
            r.status          AS status,
            r.recorded_at     AS recordedAt,
            d.plan_version_id AS planVersionId,
            d.scheduled_for   AS scheduledFor,
            d.medication_id   AS medicationDefinitionId,
            d.outcome         AS outcome
       FROM rejections r
       JOIN local_doses d ON d.id = r.local_dose_id
      WHERE r.resolved_at IS NULL
      ORDER BY r.recorded_at DESC`,
  );
}

export type DrainResult = {
  delivered: number;
  rejected: number;
  /** True when the queue stopped because the network is unreachable, not because it drained. */
  stoppedOffline: boolean;
};

/**
 * Sends queued commands in creation order.
 *
 * Three outcomes, deliberately distinct:
 *  - **Delivered.** The server decided. The local dose is marked acknowledged and the
 *    queue row is removed, in one transaction.
 *  - **Refused** (a 4xx the server will give again). The row leaves the queue and
 *    becomes a rejection the user is shown. Retrying it forever would be a queue that
 *    never drains and a user who is never told.
 *  - **Undecided** (no network, or a server fault). The row stays queued and draining
 *    stops, because the queue is ordered and a later command may depend on an earlier
 *    one having been applied.
 */
export async function drainOutbox(
  db: SQLiteDatabase,
  config: ApiConfig,
  householdId: string,
  now: string,
): Promise<DrainResult> {
  const queued = await db.getAllAsync<{ idempotencyKey: string; localDoseId: string; payload: string }>(
    `SELECT idempotency_key AS idempotencyKey, local_dose_id AS localDoseId, payload
       FROM outbox ORDER BY created_at`,
  );

  let delivered = 0;
  let rejected = 0;

  for (const row of queued) {
    const body = JSON.parse(row.payload) as RecordDoseRequest;

    try {
      const result = await api.recordDose(config, householdId, body);

      await db.withTransactionAsync(async () => {
        await db.runAsync(
          'UPDATE local_doses SET server_administration_id = ? WHERE id = ?',
          result.administrationEventId,
          row.localDoseId,
        );
        await db.runAsync('DELETE FROM outbox WHERE idempotency_key = ?', row.idempotencyKey);
      });

      delivered += 1;
    } catch (caught) {
      if (caught instanceof NetworkError) {
        await noteAttempt(db, row.idempotencyKey, 'network', now);
        return { delivered, rejected, stoppedOffline: true };
      }

      if (caught instanceof ApiError && caught.isFinal) {
        await db.withTransactionAsync(async () => {
          await db.runAsync(
            `INSERT INTO rejections (idempotency_key, local_dose_id, code, status, recorded_at)
             VALUES (?, ?, ?, ?, ?)
             ON CONFLICT(idempotency_key) DO UPDATE SET code = excluded.code, status = excluded.status`,
            row.idempotencyKey,
            row.localDoseId,
            caught.code,
            caught.status,
            now,
          );
          await db.runAsync('DELETE FROM outbox WHERE idempotency_key = ?', row.idempotencyKey);
        });

        rejected += 1;
        continue;
      }

      // A 5xx or anything else undecided: keep the command and stop here.
      await noteAttempt(
        db,
        row.idempotencyKey,
        caught instanceof ApiError ? `${caught.status}` : 'unknown',
        now,
      );

      return { delivered, rejected, stoppedOffline: false };
    }
  }

  return { delivered, rejected, stoppedOffline: false };
}

/**
 * Drops a local dose the user chose not to keep after the server refused it.
 *
 * The cascade removes its queue row; the rejection is marked resolved rather than
 * deleted, so the audit of what was refused survives.
 */
export async function discardRejected(
  db: SQLiteDatabase,
  idempotencyKey: string,
  now: string,
): Promise<void> {
  await db.withTransactionAsync(async () => {
    const row = await db.getFirstAsync<{ localDoseId: string }>(
      'SELECT local_dose_id AS localDoseId FROM rejections WHERE idempotency_key = ?',
      idempotencyKey,
    );

    await db.runAsync(
      'UPDATE rejections SET resolved_at = ? WHERE idempotency_key = ?',
      now,
      idempotencyKey,
    );

    if (row) {
      // The queue row is deleted explicitly rather than through ON DELETE CASCADE:
      // foreign_keys is a per-connection PRAGMA, so a cascade is not something this
      // code can assume is armed.
      await db.runAsync('DELETE FROM outbox WHERE local_dose_id = ?', row.localDoseId);
      await db.runAsync('DELETE FROM local_doses WHERE id = ?', row.localDoseId);
    }
  });
}

/** Keeps the local dose but stops presenting the refusal, for a conflict the user accepted. */
export async function acknowledgeRejection(
  db: SQLiteDatabase,
  idempotencyKey: string,
  now: string,
): Promise<void> {
  await db.runAsync(
    'UPDATE rejections SET resolved_at = ? WHERE idempotency_key = ?',
    now,
    idempotencyKey,
  );
}

async function noteAttempt(
  db: SQLiteDatabase,
  idempotencyKey: string,
  error: string,
  now: string,
): Promise<void> {
  await db.runAsync(
    'UPDATE outbox SET attempts = attempts + 1, last_attempt_at = ?, last_error = ? WHERE idempotency_key = ?',
    now,
    error,
    idempotencyKey,
  );
}
