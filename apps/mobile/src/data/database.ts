import type { SQLiteDatabase } from 'expo-sqlite';

/**
 * The on-device database.
 *
 * Two kinds of table live here and they are not interchangeable:
 *
 *  - **Snapshot tables** (`people`, `medications`, `packages`, `plans`, `due_doses`)
 *    are a cache of what the server last said. They are replaced wholesale on every
 *    refresh and nothing is ever lost by discarding them.
 *  - **Local tables** (`local_doses`, `outbox`, `rejections`, `reminders`) hold what
 *    this device knows and the server may not. They are authoritative until the
 *    server acknowledges them and are never dropped by a refresh.
 */

/** Bumped only for a change that needs a migration; see {@link migrateDatabase}. */
export const SCHEMA_VERSION = 1;

/**
 * The database file name.
 *
 * Deliberately distinct from the superseded `household-<id>.db`. That file's schema
 * belongs to the old domain and its queued commands address endpoints that no longer
 * exist, so they can never be delivered. Rather than destructively migrate or silently
 * delete a user's records, the old file is left untouched on disk and the rebuilt
 * client starts from a new one.
 */
export function databaseNameFor(householdId: string): string {
  return `household-${householdId}-packagefirst.db`;
}

export async function migrateDatabase(db: SQLiteDatabase): Promise<void> {
  // WAL keeps a read during a sync write from blocking the Today screen.
  await db.execAsync('PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;');

  const row = await db.getFirstAsync<{ user_version: number }>('PRAGMA user_version');
  const version = row?.user_version ?? 0;

  if (version >= SCHEMA_VERSION) {
    return;
  }

  if (version === 0) {
    await db.execAsync(CREATE_SCHEMA);
  }

  // Future versions append their ALTERs here, guarded on `version`.

  await db.execAsync(`PRAGMA user_version = ${SCHEMA_VERSION};`);
}

const CREATE_SCHEMA = `
-- ------------------------------------------------------------------ snapshot ----
-- Replaced wholesale on every refresh. Holds nothing the server does not already have.

CREATE TABLE snapshot_meta (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);

CREATE TABLE people (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  is_archived INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE medications (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  strength TEXT,
  unit TEXT NOT NULL,
  is_archived INTEGER NOT NULL DEFAULT 0,
  total_numerator INTEGER NOT NULL,
  total_denominator INTEGER NOT NULL CHECK(total_denominator > 0),
  package_count INTEGER NOT NULL
);

CREATE TABLE packages (
  id TEXT PRIMARY KEY,
  medication_id TEXT NOT NULL,
  ordinal INTEGER NOT NULL,
  state TEXT NOT NULL,
  remaining_numerator INTEGER NOT NULL,
  remaining_denominator INTEGER NOT NULL CHECK(remaining_denominator > 0),
  capacity_numerator INTEGER NOT NULL,
  capacity_denominator INTEGER NOT NULL CHECK(capacity_denominator > 0),
  holder_person_id TEXT,
  is_pinned INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX ix_packages_medication ON packages (medication_id, ordinal);

CREATE TABLE plans (
  id TEXT PRIMARY KEY,
  version_id TEXT NOT NULL,
  person_id TEXT NOT NULL,
  medication_id TEXT NOT NULL,
  dose_numerator INTEGER NOT NULL,
  dose_denominator INTEGER NOT NULL CHECK(dose_denominator > 0),
  kind TEXT NOT NULL,
  pattern TEXT NOT NULL,
  weekday_mask INTEGER,
  interval_days INTEGER,
  effective_from TEXT,
  effective_to TEXT,
  local_time TEXT,
  time_zone_id TEXT NOT NULL,
  day_period TEXT
);

-- What the server said is due, for the day last refreshed.
CREATE TABLE due_doses (
  plan_version_id TEXT NOT NULL,
  scheduled_for TEXT,
  local_date TEXT NOT NULL,
  plan_id TEXT NOT NULL,
  person_id TEXT NOT NULL,
  medication_id TEXT NOT NULL,
  dose_numerator INTEGER NOT NULL,
  dose_denominator INTEGER NOT NULL CHECK(dose_denominator > 0),
  kind TEXT NOT NULL,
  local_time TEXT,
  day_period TEXT,
  meal_relation TEXT,
  has_enough_stock INTEGER NOT NULL,
  recorded_outcome TEXT,
  recorded_administration_id TEXT,
  PRIMARY KEY (plan_version_id, local_date)
);

-- --------------------------------------------------------------------- local ----
-- Survives every refresh. Authoritative until the server acknowledges it.

CREATE TABLE local_doses (
  id TEXT PRIMARY KEY,
  plan_version_id TEXT,
  person_id TEXT NOT NULL,
  medication_id TEXT NOT NULL,
  outcome TEXT NOT NULL,
  source TEXT NOT NULL,
  package_id TEXT,
  quantity_numerator INTEGER,
  quantity_denominator INTEGER CHECK(quantity_denominator IS NULL OR quantity_denominator > 0),
  scheduled_for TEXT,
  occurred_at TEXT NOT NULL,
  recorded_at TEXT NOT NULL,
  -- Set when the server confirms. Until then this row is the only record.
  server_administration_id TEXT
);

-- One local record per scheduled slot. SQLite treats NULLs as distinct, so an extra
-- or unplanned dose — which has no slot — is never blocked by this.
CREATE UNIQUE INDEX ux_local_doses_slot ON local_doses (plan_version_id, scheduled_for);

-- The durable queue. A row is written and committed BEFORE the request is attempted,
-- so a crash between the write and the response cannot lose the command, and the
-- retry carries the same idempotency key the server already deduplicates on.
CREATE TABLE outbox (
  idempotency_key TEXT PRIMARY KEY,
  local_dose_id TEXT NOT NULL REFERENCES local_doses(id) ON DELETE CASCADE,
  payload TEXT NOT NULL,
  created_at TEXT NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0,
  last_attempt_at TEXT,
  last_error TEXT
);

CREATE INDEX ix_outbox_order ON outbox (created_at);

-- A command the server decided against. Kept and shown rather than retried forever
-- or silently dropped: a refused health record is something the user must see.
CREATE TABLE rejections (
  idempotency_key TEXT PRIMARY KEY,
  local_dose_id TEXT NOT NULL,
  code TEXT NOT NULL,
  status INTEGER NOT NULL,
  recorded_at TEXT NOT NULL,
  resolved_at TEXT
);

-- Which notifications this device currently has scheduled, so reconciliation can be
-- a cheap diff instead of cancel-everything-and-reschedule on every launch.
CREATE TABLE reminders (
  notification_id TEXT PRIMARY KEY,
  plan_version_id TEXT NOT NULL,
  trigger_kind TEXT NOT NULL,
  fires_at TEXT,
  signature TEXT NOT NULL
);

CREATE INDEX ix_reminders_plan ON reminders (plan_version_id);
`;

/** Keys used in {@link snapshot_meta}. */
export const META = {
  lastSyncedAt: 'lastSyncedAt',
  snapshotDate: 'snapshotDate',
  /** The IANA zone the reminders were last scheduled against. */
  reminderTimeZone: 'reminderTimeZone',
  /** A hash of the plan set, so reminders are only rebuilt when something changed. */
  reminderSignature: 'reminderSignature',
} as const;

export async function readMeta(db: SQLiteDatabase, key: string): Promise<string | null> {
  const row = await db.getFirstAsync<{ value: string }>(
    'SELECT value FROM snapshot_meta WHERE key = ?',
    key,
  );

  return row?.value ?? null;
}

export async function writeMeta(db: SQLiteDatabase, key: string, value: string): Promise<void> {
  await db.runAsync(
    'INSERT INTO snapshot_meta (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value',
    key,
    value,
  );
}
