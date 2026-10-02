import type { SQLiteDatabase } from 'expo-sqlite';
import { ApiError, NetworkError, type ApiConfig } from '../lib/api';
import { drainOutbox, pendingCount } from './outbox';
import { refreshSnapshot } from './snapshot';

/**
 * Coordinates the two halves of a sync: push what this device recorded, then pull what
 * the server now says.
 *
 * Order matters. Pushing first means the snapshot that follows already reflects this
 * device's own doses, so the Today screen does not briefly show a dose as unrecorded
 * immediately after it was sent.
 */

export type SyncOutcome = {
  pushed: number;
  rejected: number;
  pulled: boolean;
  /** The device could not reach the server. Everything stays queued. */
  offline: boolean;
  /** The session is no longer valid and the user must sign in again. */
  unauthenticated: boolean;
  pending: number;
};

/**
 * Serialises sync runs.
 *
 * `expo-sqlite`'s `withTransactionAsync` is a plain deferred BEGIN on the shared
 * connection — its own documentation says it "is not exclusive and can be interrupted
 * by other async queries". The alternative, `withExclusiveTransactionAsync`, opens a
 * second native connection, which loses per-connection PRAGMAs and deadlocks against
 * a read on the first because the library sets no busy timeout.
 *
 * So isolation is kept here instead: only one sync runs at a time, and a second
 * request joins the one in flight rather than starting another.
 */
let inFlight: Promise<SyncOutcome> | null = null;

export function syncNow(
  db: SQLiteDatabase,
  config: ApiConfig,
  householdId: string,
  localDate: string,
  now: string,
): Promise<SyncOutcome> {
  if (inFlight) {
    return inFlight;
  }

  inFlight = run(db, config, householdId, localDate, now).finally(() => {
    inFlight = null;
  });

  return inFlight;
}

async function run(
  db: SQLiteDatabase,
  config: ApiConfig,
  householdId: string,
  localDate: string,
  now: string,
): Promise<SyncOutcome> {
  let pushed = 0;
  let rejected = 0;

  try {
    const drained = await drainOutbox(db, config, householdId, now);
    pushed = drained.delivered;
    rejected = drained.rejected;

    if (drained.stoppedOffline) {
      return {
        pushed,
        rejected,
        pulled: false,
        offline: true,
        unauthenticated: false,
        pending: await pendingCount(db),
      };
    }

    await refreshSnapshot(db, config, householdId, localDate, now);

    return {
      pushed,
      rejected,
      pulled: true,
      offline: false,
      unauthenticated: false,
      pending: await pendingCount(db),
    };
  } catch (caught) {
    // A failed pull leaves the previous snapshot in place. Stale data the user can act
    // on beats an empty screen, and anything they record still queues.
    const offline = caught instanceof NetworkError;
    const unauthenticated = caught instanceof ApiError && caught.status === 401;

    if (!offline && !unauthenticated && !(caught instanceof ApiError)) {
      throw caught;
    }

    return {
      pushed,
      rejected,
      pulled: false,
      offline,
      unauthenticated,
      pending: await pendingCount(db),
    };
  }
}
