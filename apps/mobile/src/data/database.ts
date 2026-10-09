import type { SQLiteDatabase } from 'expo-sqlite';

/**
 * The on-device database.
 *
 * Two kinds of table live here and they are not interchangeable:
 *
 *  - **Snapshot tables** (`people`, `medications`, `packages`, `plans`, `due_doses`,
 *    `activity_entries`) are a cache of what the server last said. They are replaced wholesale on every
 *    refresh and nothing is ever lost by discarding them.
 *  - **Local tables** (`local_doses`, `outbox`, `commands`, `rejections`, `reminders`) hold
 *    what this device knows and the server may not. They are authoritative until the
 *    server acknowledges them and are never dropped by a refresh.
 */

/** Bumped only for a change that needs a migration; see {@link migrateDatabase}. */
export const SCHEMA_VERSION = 6;

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
    // A new install is created at the current shape. It must NOT then run the step
    // migrations below: CREATE_SCHEMA already contains everything they add, and an
    // ALTER that adds an existing column fails outright. This is the half that only
    // breaks on a clean device, which is the half a developer always tests.
    await db.execAsync(CREATE_SCHEMA);
  } else {
    // Everything here runs against a database that is already on somebody's phone,
    // holding records the server may not have yet. Each step is additive, guarded on
    // the version it upgrades from, and ordered oldest first.
    if (version < 2) {
      await db.execAsync(MIGRATE_1_TO_2);
    }
    if (version < 3) {
      await db.execAsync(MIGRATE_2_TO_3);
    }
    if (version < 4) {
      await db.execAsync(MIGRATE_3_TO_4);
    }
    if (version < 5) {
      await db.execAsync(MIGRATE_4_TO_5);
    }
    if (version < 6) {
      await db.execAsync(MIGRATE_5_TO_6);
    }
  }

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
  package_count INTEGER NOT NULL,
  -- Who pays, as the household recorded it; a box may override it below.
  coverage TEXT,
  -- The catalogue's usual box size, so adding stock can offer it as the default.
  default_capacity_numerator INTEGER,
  default_capacity_denominator INTEGER CHECK(default_capacity_denominator IS NULL OR default_capacity_denominator > 0)
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
  is_pinned INTEGER NOT NULL DEFAULT 0,
  -- What the Stock screen shows beyond the ordinal: the household's own name for the
  -- box, when it expires, and a coverage that overrides the medicine's.
  label TEXT,
  expires_on TEXT,
  coverage TEXT
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
  -- The monthly patterns (server Sprint 7): the day of the month, or the months between
  -- occurrences counted from effective_from. Each pattern owns exactly its fields.
  day_of_month INTEGER,
  interval_months INTEGER,
  effective_from TEXT,
  effective_to TEXT,
  local_time TEXT,
  time_zone_id TEXT NOT NULL,
  day_period TEXT,
  meal_relation TEXT,
  -- A plan the household deliberately set aside. Without this the phone kept its
  -- reminders: the one defect on the mobile list that told somebody something untrue.
  is_paused INTEGER NOT NULL DEFAULT 0
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
  -- The server's "do not take with" warnings for this row, as a JSON array; '[]' when
  -- nothing matched. Shown in red, never a block (ADR 0016).
  conflicts TEXT NOT NULL DEFAULT '[]',
  PRIMARY KEY (plan_version_id, local_date)
);

-- What happened, as the server last listed it: its latest page of stock movements,
-- recorded doses and corrections, one row each with a kind. Replaced on every refresh
-- like the rest of the snapshot; the device's own unsynced doses live in local_doses.
CREATE TABLE activity_entries (
  id TEXT PRIMARY KEY,
  kind TEXT NOT NULL,
  occurred_at TEXT NOT NULL,
  recorded_at TEXT NOT NULL,
  person_id TEXT,
  medication_id TEXT,
  detail TEXT,
  stock_source TEXT,
  package_label INTEGER,
  from_package_label INTEGER,
  to_package_label INTEGER,
  quantity_numerator INTEGER,
  quantity_denominator INTEGER CHECK(quantity_denominator IS NULL OR quantity_denominator > 0),
  scheduled_for TEXT,
  lateness_minutes INTEGER,
  reason TEXT,
  administration_event_id TEXT
);

CREATE INDEX ix_activity_recorded ON activity_entries (kind, recorded_at);

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

-- The other commands the phone can queue: pause or resume a plan, add stock, make a
-- box the active one, mark a box lost or disposed. Written and committed before the
-- request is attempted, like a dose, and sent in creation order together with the
-- doses. A delivered row is deleted; a refused one stays, marked, until the user has
-- seen it and dismissed it.
CREATE TABLE commands (
  idempotency_key TEXT PRIMARY KEY,
  kind TEXT NOT NULL,
  target_id TEXT NOT NULL,
  medication_id TEXT,
  payload TEXT NOT NULL,
  created_at TEXT NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0,
  last_attempt_at TEXT,
  last_error TEXT,
  rejected_code TEXT,
  rejected_status INTEGER,
  rejected_at TEXT
);

CREATE INDEX ix_commands_order ON commands (created_at);

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

/**
 * v1 → v2: a paused plan stops reminding.
 *
 * Additive and defaulted, so an existing row is already correct — a plan nobody paused
 * is not paused. The next sync overwrites the whole snapshot with the server's answer
 * anyway; the default only has to be right for the minutes in between, and a phone that
 * upgrades offline must not start treating every plan as paused.
 */
const MIGRATE_1_TO_2 = `
ALTER TABLE plans ADD COLUMN is_paused INTEGER NOT NULL DEFAULT 0;
`;

/**
 * v2 → v3: the monthly patterns and the do-not-take-with warnings.
 *
 * Additive and defaulted. A plan row that predates the columns has no monthly fields,
 * which is correct — it was written for a daily, weekly or every-N-days pattern — and a
 * cached Today row from before the upgrade simply carries no warnings until the next
 * sync replaces the snapshot.
 */
const MIGRATE_2_TO_3 = `
ALTER TABLE plans ADD COLUMN day_of_month INTEGER;
ALTER TABLE plans ADD COLUMN interval_months INTEGER;
ALTER TABLE due_doses ADD COLUMN conflicts TEXT NOT NULL DEFAULT '[]';
`;

/**
 * v3 → v4: what the Stock and Plans screens show that the Today screen never needed.
 *
 * All nullable, so an existing row is already correct: a box without a name shows its
 * ordinal, a plan without a meal relation shows none. The next sync fills them in.
 */
const MIGRATE_3_TO_4 = `
ALTER TABLE medications ADD COLUMN coverage TEXT;
ALTER TABLE packages ADD COLUMN label TEXT;
ALTER TABLE packages ADD COLUMN expires_on TEXT;
ALTER TABLE packages ADD COLUMN coverage TEXT;
ALTER TABLE plans ADD COLUMN meal_relation TEXT;
`;

/**
 * v4 → v5: the History screen's cache.
 *
 * A new snapshot table, empty until the next sync fills it; nothing existing is touched.
 */
const MIGRATE_4_TO_5 = `
CREATE TABLE activity_entries (
  id TEXT PRIMARY KEY,
  kind TEXT NOT NULL,
  occurred_at TEXT NOT NULL,
  recorded_at TEXT NOT NULL,
  person_id TEXT,
  medication_id TEXT,
  detail TEXT,
  stock_source TEXT,
  package_label INTEGER,
  from_package_label INTEGER,
  to_package_label INTEGER,
  quantity_numerator INTEGER,
  quantity_denominator INTEGER CHECK(quantity_denominator IS NULL OR quantity_denominator > 0),
  scheduled_for TEXT,
  lateness_minutes INTEGER,
  reason TEXT,
  administration_event_id TEXT
);

CREATE INDEX ix_activity_recorded ON activity_entries (kind, recorded_at);
`;

/**
 * v5 → v6: the command queue, and the catalogue's default box size.
 *
 * A new local table, empty on upgrade, and two nullable columns the next sync fills;
 * nothing a phone already holds is touched.
 */
const MIGRATE_5_TO_6 = `
ALTER TABLE medications ADD COLUMN default_capacity_numerator INTEGER;
ALTER TABLE medications ADD COLUMN default_capacity_denominator INTEGER CHECK(default_capacity_denominator IS NULL OR default_capacity_denominator > 0);

CREATE TABLE commands (
  idempotency_key TEXT PRIMARY KEY,
  kind TEXT NOT NULL,
  target_id TEXT NOT NULL,
  medication_id TEXT,
  payload TEXT NOT NULL,
  created_at TEXT NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0,
  last_attempt_at TEXT,
  last_error TEXT,
  rejected_code TEXT,
  rejected_status INTEGER,
  rejected_at TEXT
);

CREATE INDEX ix_commands_order ON commands (created_at);
`;

/** Keys used in {@link snapshot_meta}. */
export const META = {
  lastSyncedAt: 'lastSyncedAt',
  snapshotDate: 'snapshotDate',
  /** The IANA zone the reminders were last scheduled against. */
  reminderTimeZone: 'reminderTimeZone',
  /** A hash of the plan set, so reminders are only rebuilt when something changed. */
  reminderSignature: 'reminderSignature',
  /** The last reports the server answered with, so the screen has something offline. */
  reportsCache: 'reportsCache',
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
