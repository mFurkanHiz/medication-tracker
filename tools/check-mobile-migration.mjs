#!/usr/bin/env node
// Proves the on-device schema migration against a database that already exists.
//
// WHY THIS EXISTS
// A server migration is tested by CI on a production-shaped baseline. A phone migration
// has no such net: the database lives on somebody's device, holds doses the server has
// not seen yet, and gets exactly one chance to upgrade correctly. A migration that works
// on a clean install and fails on an upgrade is the hardest kind of failure to diagnose,
// because the developer's own phone is always the clean install.
//
// WHAT IT CHECKS
// It runs the REAL migrateDatabase function — compiled from the real source, not a copy —
// against a real SQLite database, down both paths:
//
//   1. a fresh install, which must be created at the current shape and must NOT then run
//      the step migrations, because an ALTER that adds an existing column throws;
//   2. an existing v1 database holding a plan row and a due row, which must gain every
//      column added since, keep its rows intact, and land on the current version;
//   3. an existing database at every later version, the same way — every version that
//      was ever on a phone is an upgrade path, not only the oldest;
//   4. running the migration again, which must do nothing.
//
// The fixtures below are frozen on purpose. They are what is on people's phones today, so
// they must not be regenerated from the current schema — that would make the test agree
// with whatever the code says and prove nothing.

import { DatabaseSync } from 'node:sqlite';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const REPO = new URL('..', import.meta.url).pathname.replace(/\/$/, '');
const SOURCE = `${REPO}/apps/mobile/src/data/database.ts`;

/**
 * The tables as each schema version created them, as frozen column lists.
 *
 * Frozen on purpose: these are what is on people's phones, so they must not be
 * regenerated from the current CREATE_SCHEMA — that would make the test agree with
 * whatever the code says and prove nothing. A new step migration adds its columns under
 * its version number here, and nothing else changes.
 */
const BASE_COLUMNS = {
  plans: [
    'id TEXT PRIMARY KEY', 'version_id TEXT NOT NULL', 'person_id TEXT NOT NULL',
    'medication_id TEXT NOT NULL', 'dose_numerator INTEGER NOT NULL',
    'dose_denominator INTEGER NOT NULL CHECK(dose_denominator > 0)', 'kind TEXT NOT NULL',
    'pattern TEXT NOT NULL', 'weekday_mask INTEGER', 'interval_days INTEGER', 'effective_from TEXT',
    'effective_to TEXT', 'local_time TEXT', 'time_zone_id TEXT NOT NULL', 'day_period TEXT',
  ],
  due_doses: [
    'plan_version_id TEXT NOT NULL', 'scheduled_for TEXT', 'local_date TEXT NOT NULL',
    'plan_id TEXT NOT NULL', 'person_id TEXT NOT NULL', 'medication_id TEXT NOT NULL',
    'dose_numerator INTEGER NOT NULL', 'dose_denominator INTEGER NOT NULL CHECK(dose_denominator > 0)',
    'kind TEXT NOT NULL', 'local_time TEXT', 'day_period TEXT', 'meal_relation TEXT',
    'has_enough_stock INTEGER NOT NULL', 'recorded_outcome TEXT', 'recorded_administration_id TEXT',
    'PRIMARY KEY (plan_version_id, local_date)',
  ],
  packages: [
    'id TEXT PRIMARY KEY', 'medication_id TEXT NOT NULL', 'ordinal INTEGER NOT NULL', 'state TEXT NOT NULL',
    'remaining_numerator INTEGER NOT NULL', 'remaining_denominator INTEGER NOT NULL CHECK(remaining_denominator > 0)',
    'capacity_numerator INTEGER NOT NULL', 'capacity_denominator INTEGER NOT NULL CHECK(capacity_denominator > 0)',
    'holder_person_id TEXT', 'is_pinned INTEGER NOT NULL DEFAULT 0',
  ],
  medications: [
    'id TEXT PRIMARY KEY', 'name TEXT NOT NULL', 'strength TEXT', 'unit TEXT NOT NULL',
    'is_archived INTEGER NOT NULL DEFAULT 0', 'total_numerator INTEGER NOT NULL',
    'total_denominator INTEGER NOT NULL CHECK(total_denominator > 0)', 'package_count INTEGER NOT NULL',
  ],
};

/** What each step migration added to an existing table, by the version it upgrades TO. */
const ADDED_IN = {
  2: { plans: ['is_paused INTEGER NOT NULL DEFAULT 0'] },
  3: { plans: ['day_of_month INTEGER', 'interval_months INTEGER'], due_doses: ["conflicts TEXT NOT NULL DEFAULT '[]'"] },
  4: { medications: ['coverage TEXT'], packages: ['label TEXT', 'expires_on TEXT', 'coverage TEXT'], plans: ['meal_relation TEXT'] },
  6: { medications: ['default_capacity_numerator INTEGER', 'default_capacity_denominator INTEGER CHECK(default_capacity_denominator IS NULL OR default_capacity_denominator > 0)'] },
  7: { packages: ['owner_person_id TEXT', 'active_loan_id TEXT', 'acquired_on TEXT', 'lot_number TEXT', 'barcode TEXT', 'source TEXT', 'storage_location TEXT', 'note TEXT'] },
  8: { plans: ['version_number INTEGER NOT NULL DEFAULT 1'] },
};

/** Whole tables a step migration created, by the version it upgrades TO. Frozen too. */
const TABLES_ADDED_IN = {
  5: {
    activity_entries: [
      'id TEXT PRIMARY KEY', 'kind TEXT NOT NULL', 'occurred_at TEXT NOT NULL', 'recorded_at TEXT NOT NULL',
      'person_id TEXT', 'medication_id TEXT', 'detail TEXT', 'stock_source TEXT', 'package_label INTEGER',
      'from_package_label INTEGER', 'to_package_label INTEGER', 'quantity_numerator INTEGER',
      'quantity_denominator INTEGER CHECK(quantity_denominator IS NULL OR quantity_denominator > 0)',
      'scheduled_for TEXT', 'lateness_minutes INTEGER', 'reason TEXT', 'administration_event_id TEXT',
    ],
  },
  6: {
    commands: [
      'idempotency_key TEXT PRIMARY KEY', 'kind TEXT NOT NULL', 'target_id TEXT NOT NULL', 'medication_id TEXT',
      'payload TEXT NOT NULL', 'created_at TEXT NOT NULL', 'attempts INTEGER NOT NULL DEFAULT 0',
      'last_attempt_at TEXT', 'last_error TEXT', 'rejected_code TEXT', 'rejected_status INTEGER', 'rejected_at TEXT',
    ],
  },
  8: {
    plan_versions: [
      'version_id TEXT PRIMARY KEY', 'plan_id TEXT NOT NULL', 'version_number INTEGER NOT NULL',
      'dose_numerator INTEGER NOT NULL', 'dose_denominator INTEGER NOT NULL CHECK(dose_denominator > 0)',
      'kind TEXT NOT NULL', 'pattern TEXT NOT NULL', 'weekday_mask INTEGER', 'interval_days INTEGER',
      'day_of_month INTEGER', 'interval_months INTEGER', 'effective_from TEXT', 'effective_to TEXT',
      'local_time TEXT', 'time_zone_id TEXT NOT NULL', 'is_paused INTEGER NOT NULL DEFAULT 0',
    ],
  },
};

/** The version a table first exists at: 1 for the base tables. */
function tableSince(table) {
  if (BASE_COLUMNS[table]) return 1;
  for (const [step, tables] of Object.entries(TABLES_ADDED_IN)) {
    if (tables[table]) return Number(step);
  }
  throw new Error(`unknown table ${table}`);
}

/** Every table the schema has ever had, base first. */
const ALL_TABLES = [
  ...Object.keys(BASE_COLUMNS),
  ...Object.values(TABLES_ADDED_IN).flatMap((tables) => Object.keys(tables)),
];

/** The column definitions a table was created with, at the version it first existed. */
function createdWith(table) {
  if (BASE_COLUMNS[table]) return BASE_COLUMNS[table];
  return TABLES_ADDED_IN[tableSince(table)][table];
}

const columnName = (definition) => definition.split(' ')[0];

/** The column names a table has at `version`; none before the table exists. */
function columnsAt(table, version) {
  if (tableSince(table) > version) return [];
  const names = createdWith(table).filter((d) => !d.startsWith('PRIMARY KEY')).map(columnName);
  for (const [step, added] of Object.entries(ADDED_IN)) {
    if (Number(step) <= version) names.push(...(added[table] ?? []).map(columnName));
  }
  return names;
}

/** The columns every step after `version` adds — a whole table counts as all of its columns. */
function columnsAddedAfter(table, version) {
  if (tableSince(table) > version) return columnsAt(table, SCHEMA_VERSION_EXPECTED);
  const names = [];
  for (const [step, added] of Object.entries(ADDED_IN)) {
    if (Number(step) > version) names.push(...(added[table] ?? []).map(columnName));
  }
  return names;
}

/** CREATE TABLE statements for every table as schema `version` had them. */
function fixtureAt(version) {
  return ALL_TABLES.filter((table) => tableSince(table) <= version).map((table) => {
    const definitions = createdWith(table).filter((d) => !d.startsWith('PRIMARY KEY'));
    for (const [step, added] of Object.entries(ADDED_IN)) {
      if (Number(step) <= version) definitions.push(...(added[table] ?? []));
    }
    definitions.push(...createdWith(table).filter((d) => d.startsWith('PRIMARY KEY')));
    return `CREATE TABLE ${table} (\n  ${definitions.join(',\n  ')}\n);`;
  }).join('\n');
}

/** Set once the real module is loaded, so the helpers above can name the current version. */
let SCHEMA_VERSION_EXPECTED = 0;

const failures = [];

function check(what, condition, detail = '') {
  if (condition) {
    console.log(`  ok   ${what}`);
  } else {
    console.log(`  FAIL ${what}${detail ? ` — ${detail}` : ''}`);
    failures.push(what);
  }
}

/** Compiles the real module so the test exercises its actual control flow. */
function compileDatabaseModule(workDir) {
  const out = join(workDir, 'compiled');

  execFileSync(
    'node',
    [
      `${REPO}/apps/mobile/node_modules/typescript/lib/tsc.js`,
      SOURCE,
      '--outDir', out,
      '--module', 'esnext',
      '--target', 'es2022',
      '--moduleResolution', 'bundler',
      '--skipLibCheck',
    ],
    { stdio: 'pipe' },
  );

  return pathToFileURL(join(out, 'database.js')).href;
}

/** Enough of the expo-sqlite surface for migrateDatabase, backed by real SQLite. */
function adapt(db) {
  return {
    async execAsync(sql) {
      db.exec(sql);
    },
    async getFirstAsync(sql) {
      return db.prepare(sql).get() ?? null;
    },
    async runAsync(sql, ...params) {
      db.prepare(sql).run(...params);
    },
  };
}

const columnsOf = (db, table) =>
  db.prepare(`PRAGMA table_info(${table})`).all().map((column) => column.name);

const hasEveryColumn = (db, version) =>
  ALL_TABLES.filter((table) => tableSince(table) <= version).every((table) =>
    columnsAt(table, version).every((column) => columnsOf(db, table).includes(column)));

const userVersionOf = (db) => db.prepare('PRAGMA user_version').get().user_version;

const workDir = mkdtempSync(join(tmpdir(), 'mobile-migration-'));

try {
  const moduleUrl = compileDatabaseModule(workDir);
  const { migrateDatabase, SCHEMA_VERSION } = await import(moduleUrl);

  // The source of truth for what the migration must reach.
  const expected = SCHEMA_VERSION;
  SCHEMA_VERSION_EXPECTED = expected;
  console.log(`Mobile schema version ${expected}\n`);

  // ------------------------------------------------------------- fresh install ----
  console.log('A fresh install');
  {
    const db = new DatabaseSync(join(workDir, 'fresh.db'));

    // Caught rather than thrown, because this is the most likely way to break this file
    // and the failure should read as a failed check in CI rather than a stack trace. A
    // fresh install that replays the step migrations dies here on "duplicate column".
    let created = true;

    try {
      await migrateDatabase(adapt(db));
    } catch (error) {
      created = false;
      check('did not replay the step migrations over its own schema', false, String(error));
    }

    if (created) {
      check('is created at the current version', userVersionOf(db) === expected,
        `user_version was ${userVersionOf(db)}`);
      check('has every column every step ever added', hasEveryColumn(db, expected));
      check('did not replay the step migrations over its own schema', true);
    }

    db.close();
  }

  // ---------------------------------------- an upgrade from every earlier version ----
  for (let from = 1; from < expected; from++) {
    console.log(`\nA database already on a phone, at version ${from}`);

    const db = new DatabaseSync(join(workDir, `existing-v${from}.db`));
    db.exec(fixtureAt(from));
    db.exec(`PRAGMA user_version = ${from};`);
    db.prepare(
      `INSERT INTO plans (id, version_id, person_id, medication_id, dose_numerator,
         dose_denominator, kind, pattern, time_zone_id, local_time)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    ).run('plan-1', 'version-1', 'person-1', 'medication-1', 1, 2, 'Scheduled', 'Daily',
      'Europe/Istanbul', '08:00:00');
    db.prepare(
      `INSERT INTO due_doses (plan_version_id, local_date, plan_id, person_id, medication_id,
         dose_numerator, dose_denominator, kind, has_enough_stock)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    ).run('version-1', '2026-10-05', 'plan-1', 'person-1', 'medication-1', 1, 2, 'Scheduled', 1);
    db.prepare(
      `INSERT INTO medications (id, name, unit, total_numerator, total_denominator, package_count)
       VALUES (?, ?, ?, ?, ?, ?)`,
    ).run('medication-1', 'Synthetic tablet', 'Tablet', 20, 1, 1);
    db.prepare(
      `INSERT INTO packages (id, medication_id, ordinal, state, remaining_numerator,
         remaining_denominator, capacity_numerator, capacity_denominator)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
    ).run('package-1', 'medication-1', 1, 'Sealed', 20, 1, 20, 1);

    const later = ALL_TABLES.flatMap((table) =>
      columnsAddedAfter(table, from).map((column) => [table, column]));
    check(`starts without the ${later.length} column(s) later steps add`,
      later.every(([table, column]) => !columnsOf(db, table).includes(column)));
    check('starts with every column its own version had', hasEveryColumn(db, from));

    await migrateDatabase(adapt(db));

    check('gains every column every later step adds', hasEveryColumn(db, expected));
    check('lands on the current version', userVersionOf(db) === expected,
      `user_version was ${userVersionOf(db)}`);

    const row = db.prepare('SELECT * FROM plans WHERE id = ?').get('plan-1');

    check('keeps the plan row it already held', row !== undefined);
    check('keeps every value on that row',
      row?.version_id === 'version-1' && row?.dose_numerator === 1 && row?.dose_denominator === 2
      && row?.local_time === '08:00:00' && row?.time_zone_id === 'Europe/Istanbul');

    // The default has to be "not paused". A phone that upgrades while offline would
    // otherwise treat every plan as set aside and go silent until the next sync.
    check('defaults the existing plan to not paused', row?.is_paused === 0,
      `is_paused was ${row?.is_paused}`);

    // The one version a phone held so far is that plan's first; the history table is
    // empty until the next sync lists the rest.
    check('counts the existing plan as its first version, with no history yet',
      row?.version_number === 1 && db.prepare('SELECT count(*) AS n FROM plan_versions').get().n === 0,
      `version_number was ${row?.version_number}`);

    // A plan from before the monthly patterns has no monthly fields, which is the truth;
    // likewise no meal relation until the next sync says otherwise.
    check('leaves the fields an older plan never had empty',
      row?.day_of_month === null && row?.interval_months === null && row?.meal_relation === null);

    // A cached Today row from before the upgrade carries no warnings, as an empty list
    // rather than a null the screen would have to special-case.
    const due = db.prepare('SELECT * FROM due_doses WHERE plan_version_id = ?').get('version-1');
    check('keeps the due row and defaults its warnings to an empty list',
      due !== undefined && due?.conflicts === '[]', `conflicts was ${due?.conflicts}`);

    // A box without a name shows its ordinal; nothing invents a label, an expiry or a payer.
    const box = db.prepare('SELECT * FROM packages WHERE id = ?').get('package-1');
    check('keeps the box and leaves its name, expiry and coverage empty',
      box !== undefined && box?.label === null && box?.expires_on === null && box?.coverage === null);
    const medication = db.prepare('SELECT * FROM medications WHERE id = ?').get('medication-1');
    check('keeps the medication and leaves its coverage empty',
      medication !== undefined && medication?.coverage === null);

    // --------------------------------------------------------------- idempotent ----
    await migrateDatabase(adapt(db));

    check('is unchanged by running the migration again', userVersionOf(db) === expected
      && columnsOf(db, 'plans').filter((name) => name === 'is_paused').length === 1
      && columnsOf(db, 'packages').filter((name) => name === 'label').length === 1);

    db.close();
  }

  // The frozen fixture must actually be one version behind, or the upgrade path above
  // silently stops testing anything.
  const source = readFileSync(SOURCE, 'utf8');
  console.log('\nThe check itself');
  check('exercises every step migration the source declares',
    (source.match(/const MIGRATE_\d+_TO_\d+/g) ?? []).length === expected - 1,
    'add the new step to this script when you add one to database.ts');
} finally {
  rmSync(workDir, { recursive: true, force: true });
}

console.log('');

if (failures.length > 0) {
  console.error(`${failures.length} check(s) failed.`);
  process.exit(1);
}

console.log('The on-device migration is safe on a fresh install and on an upgrade.');
