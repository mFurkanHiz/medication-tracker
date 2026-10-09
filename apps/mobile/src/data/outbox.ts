import * as Crypto from 'expo-crypto';
import type { SQLiteDatabase } from 'expo-sqlite';
import {
  ApiError,
  NetworkError,
  api,
  type AddStockRequest,
  type ApiConfig,
  type CountRequest,
  type DoseSource,
  type RecordDoseRequest,
  type RetirePackageRequest,
  type SetPlanPausedRequest,
  type UpdatePackageRequest,
} from '../lib/api';
import { alreadyDone, interleave } from '../lib/commands';
import type { Quantity } from '../lib/quantity';
import { applyLocalEffect, queuedCommandCount, type CommandRow } from './command-queue';

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

/** Everything still waiting for the server: doses and the other commands alike. */
export async function pendingCount(db: SQLiteDatabase): Promise<number> {
  const row = await db.getFirstAsync<{ count: number }>('SELECT count(*) AS count FROM outbox');
  return (row?.count ?? 0) + (await queuedCommandCount(db));
}

export type CommandInput =
  | { kind: 'plan.pause'; targetId: string; medicationId: string | null; body: SetPlanPausedRequest }
  | { kind: 'stock.add'; targetId: string; medicationId: string; body: Omit<AddStockRequest, 'idempotencyKey'> }
  | { kind: 'package.pin'; targetId: string; medicationId: string; body: Record<string, never> }
  | { kind: 'package.retire'; targetId: string; medicationId: string; body: RetirePackageRequest }
  | { kind: 'plan.end'; targetId: string; medicationId: string | null; body: { endsOn: string } }
  | { kind: 'plan.restart'; targetId: string; medicationId: string | null; body: { startsOn: string } }
  | { kind: 'package.unpin'; targetId: string; medicationId: string; body: Record<string, never> }
  | { kind: 'package.reinstate'; targetId: string; medicationId: string; body: Record<string, never> }
  | { kind: 'package.update'; targetId: string; medicationId: string; body: UpdatePackageRequest }
  | { kind: 'package.assign'; targetId: string; medicationId: string; body: { personId: string | null } }
  | { kind: 'package.lend'; targetId: string; medicationId: string; body: { borrowerPersonId: string } }
  | { kind: 'loan.return'; targetId: string; medicationId: string; body: { packageId: string } }
  | { kind: 'inventory.count'; targetId: string; medicationId: null; body: Omit<CountRequest, 'idempotencyKey'> };

/**
 * Queues a command and applies what it will do to the cached snapshot, in one
 * transaction. The same rule as a dose: committed before any network call, carrying
 * the key the request will be retried with.
 */
export async function enqueueCommand(db: SQLiteDatabase, input: CommandInput, now: string): Promise<string> {
  const idempotencyKey = Crypto.randomUUID();
  // Added stock and a count are the commands the server cannot tell apart from a second
  // one on its own, so for them the key travels in the body as well.
  const payload = JSON.stringify(
    input.kind === 'stock.add' || input.kind === 'inventory.count' ? { ...input.body, idempotencyKey } : input.body,
  );

  await db.withTransactionAsync(async () => {
    await db.runAsync(
      'INSERT INTO commands (idempotency_key, kind, target_id, medication_id, payload, created_at) VALUES (?, ?, ?, ?, ?, ?)',
      idempotencyKey,
      input.kind,
      input.targetId,
      input.medicationId,
      payload,
      now,
    );
    await applyLocalEffect(db, { kind: input.kind, targetId: input.targetId, payload });
  });

  return idempotencyKey;
}

/** Drops a refused command the user has seen. Nothing happened on the server, so nothing is kept. */
export async function dismissCommand(db: SQLiteDatabase, idempotencyKey: string): Promise<void> {
  await db.runAsync('DELETE FROM commands WHERE idempotency_key = ? AND rejected_at IS NOT NULL', idempotencyKey);
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
 * Sends queued doses and commands in creation order, as one queue.
 *
 * Three outcomes, deliberately distinct:
 *  - **Delivered.** The server decided. A dose is marked acknowledged and its queue row
 *    removed, in one transaction; a command's row is removed.
 *  - **Refused** (a 4xx the server will give again). A dose leaves the queue and becomes
 *    a rejection the user is shown; a command stays, marked refused, until dismissed.
 *    Retrying either forever would be a queue that never drains and a user who is
 *    never told.
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
  const doses = await db.getAllAsync<QueuedDose>(
    `SELECT idempotency_key AS idempotencyKey, local_dose_id AS localDoseId, payload, created_at AS createdAt
       FROM outbox ORDER BY created_at`,
  );
  const commands = await db.getAllAsync<QueuedCommand>(
    `SELECT idempotency_key AS idempotencyKey, kind, target_id AS targetId, payload, created_at AS createdAt
       FROM commands WHERE rejected_at IS NULL ORDER BY created_at`,
  );

  const queue = interleave(
    doses.map((dose) => ({ createdAt: dose.createdAt, item: dose })),
    commands.map((command) => ({ createdAt: command.createdAt, item: command })),
  );

  let delivered = 0;
  let rejected = 0;

  for (const entry of queue) {
    const outcome =
      entry.kind === 'dose'
        ? await sendDose(db, config, householdId, entry.item, now)
        : await sendCommand(db, config, householdId, entry.item, now);

    if (outcome === 'delivered') {
      delivered += 1;
    } else if (outcome === 'rejected') {
      rejected += 1;
    } else if (outcome === 'offline') {
      return { delivered, rejected, stoppedOffline: true };
    } else {
      return { delivered, rejected, stoppedOffline: false };
    }
  }

  return { delivered, rejected, stoppedOffline: false };
}

type QueuedDose = { idempotencyKey: string; localDoseId: string; payload: string; createdAt: string };
type QueuedCommand = Pick<CommandRow, 'idempotencyKey' | 'kind' | 'targetId' | 'payload' | 'createdAt'>;
type SendOutcome = 'delivered' | 'rejected' | 'offline' | 'undecided';

async function sendDose(
  db: SQLiteDatabase,
  config: ApiConfig,
  householdId: string,
  row: QueuedDose,
  now: string,
): Promise<SendOutcome> {
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

    return 'delivered';
  } catch (caught) {
    if (caught instanceof NetworkError) {
      await noteAttempt(db, 'outbox', row.idempotencyKey, 'network', now);
      return 'offline';
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

      return 'rejected';
    }

    // A 5xx or anything else undecided: keep the command and stop here.
    await noteAttempt(db, 'outbox', row.idempotencyKey, caught instanceof ApiError ? `${caught.status}` : 'unknown', now);
    return 'undecided';
  }
}

async function sendCommand(
  db: SQLiteDatabase,
  config: ApiConfig,
  householdId: string,
  row: QueuedCommand,
  now: string,
): Promise<SendOutcome> {
  const body = JSON.parse(row.payload) as Record<string, unknown>;

  try {
    switch (row.kind) {
      case 'plan.pause':
        await api.setPlanPaused(config, householdId, row.targetId, body as SetPlanPausedRequest);
        break;
      case 'stock.add':
        await api.addStock(config, householdId, row.targetId, body as AddStockRequest);
        break;
      case 'package.pin':
        await api.pinPackage(config, householdId, row.targetId);
        break;
      case 'package.retire':
        await api.retirePackage(config, householdId, row.targetId, body as RetirePackageRequest);
        break;
      case 'inventory.count':
        await api.countStock(config, householdId, body as CountRequest);
        break;
      case 'plan.end':
        await api.endPlan(config, householdId, row.targetId, body as { endsOn: string });
        break;
      case 'plan.restart':
        await api.restartPlan(config, householdId, row.targetId, body as { startsOn: string });
        break;
      case 'package.unpin':
        await api.unpinPackage(config, householdId, row.targetId);
        break;
      case 'package.reinstate':
        await api.reinstatePackage(config, householdId, row.targetId);
        break;
      case 'package.update':
        await api.updatePackage(config, householdId, row.targetId, body as UpdatePackageRequest);
        break;
      case 'package.assign':
        await api.assignPackage(config, householdId, row.targetId, body as { personId: string | null });
        break;
      case 'package.lend':
        await api.lendPackage(config, householdId, row.targetId, body as { borrowerPersonId: string });
        break;
      case 'loan.return':
        await api.returnLoan(config, householdId, row.targetId);
        break;
      default:
        // A kind this build does not know cannot be sent; shown rather than retried forever.
        await markRefused(db, row.idempotencyKey, 'unknown_command', 0, now);
        return 'rejected';
    }

    await db.runAsync('DELETE FROM commands WHERE idempotency_key = ?', row.idempotencyKey);
    return 'delivered';
  } catch (caught) {
    if (caught instanceof NetworkError) {
      await noteAttempt(db, 'commands', row.idempotencyKey, 'network', now);
      return 'offline';
    }

    // A refusal that means the goal is already reached — a replay after a lost answer.
    if (caught instanceof ApiError && alreadyDone(row.kind, caught.code)) {
      await db.runAsync('DELETE FROM commands WHERE idempotency_key = ?', row.idempotencyKey);
      return 'delivered';
    }

    if (caught instanceof ApiError && caught.isFinal) {
      await markRefused(db, row.idempotencyKey, caught.code, caught.status, now);
      return 'rejected';
    }

    await noteAttempt(db, 'commands', row.idempotencyKey, caught instanceof ApiError ? `${caught.status}` : 'unknown', now);
    return 'undecided';
  }
}

/** The command stays, marked, so the screens can show what was refused and why. */
async function markRefused(
  db: SQLiteDatabase,
  idempotencyKey: string,
  code: string,
  status: number,
  now: string,
): Promise<void> {
  await db.runAsync(
    'UPDATE commands SET rejected_code = ?, rejected_status = ?, rejected_at = ? WHERE idempotency_key = ?',
    code,
    status,
    now,
    idempotencyKey,
  );
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
  table: 'outbox' | 'commands',
  idempotencyKey: string,
  error: string,
  now: string,
): Promise<void> {
  await db.runAsync(
    `UPDATE ${table} SET attempts = attempts + 1, last_attempt_at = ?, last_error = ? WHERE idempotency_key = ?`,
    now,
    error,
    idempotencyKey,
  );
}
