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
//   3. an existing v2 database, the same way — every version that was ever on a phone
//      is an upgrade path, not only the oldest;
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
 * The `plans` table exactly as schema version 1 created it.
 *
 * Frozen. Do not regenerate this from the current CREATE_SCHEMA.
 */
const V1_PLANS = `
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
`;

/** The `due_doses` table as schema versions 1 and 2 created it. Frozen, as above. */
const V1_DUE_DOSES = `
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
`;

/** The `plans` table as schema version 2 left it: v1 plus the paused flag. Frozen. */
const V2_PLANS = V1_PLANS.replace(
  '  day_period TEXT\n);',
  '  day_period TEXT,\n  is_paused INTEGER NOT NULL DEFAULT 0\n);',
);

/** Every column added after v2; an upgrade from either older version must gain them all. */
const V3_COLUMNS = { plans: ['day_of_month', 'interval_months'], due_doses: ['conflicts'] };

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

const hasV3Columns = (db) =>
  Object.entries(V3_COLUMNS).every(([table, columns]) =>
    columns.every((column) => columnsOf(db, table).includes(column)));

const userVersionOf = (db) => db.prepare('PRAGMA user_version').get().user_version;

const workDir = mkdtempSync(join(tmpdir(), 'mobile-migration-'));

try {
  const moduleUrl = compileDatabaseModule(workDir);
  const { migrateDatabase, SCHEMA_VERSION } = await import(moduleUrl);

  // The source of truth for what the migration must reach.
  const expected = SCHEMA_VERSION;
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
      check('has the is_paused column', columnsOf(db, 'plans').includes('is_paused'));
      check('has the v3 columns', hasV3Columns(db));
      check('did not replay the step migrations over its own schema', true);
    }

    db.close();
  }

  // ------------------------------------------- an upgrade from versions 1 and 2 ----
  for (const from of [1, 2]) {
    console.log(`\nA database already on a phone, at version ${from}`);

    const path = join(workDir, `existing-v${from}.db`);
    const db = new DatabaseSync(path);
    db.exec(from === 1 ? V1_PLANS : V2_PLANS);
    db.exec(V1_DUE_DOSES);
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

    check('starts with the is_paused column only from v2',
      columnsOf(db, 'plans').includes('is_paused') === (from === 2));
    check('starts without the v3 columns', !hasV3Columns(db));

    await migrateDatabase(adapt(db));

    check('gains the is_paused column', columnsOf(db, 'plans').includes('is_paused'));
    check('gains the v3 columns', hasV3Columns(db));
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

    // A plan from before the monthly patterns has no monthly fields, which is the truth.
    check('leaves the monthly fields of an older plan empty',
      row?.day_of_month === null && row?.interval_months === null);

    // A cached Today row from before the upgrade carries no warnings, as an empty list
    // rather than a null the screen would have to special-case.
    const due = db.prepare('SELECT * FROM due_doses WHERE plan_version_id = ?').get('version-1');
    check('keeps the due row and defaults its warnings to an empty list',
      due !== undefined && due?.conflicts === '[]', `conflicts was ${due?.conflicts}`);

    // --------------------------------------------------------------- idempotent ----
    await migrateDatabase(adapt(db));

    check('is unchanged by running the migration again', userVersionOf(db) === expected
      && columnsOf(db, 'plans').filter((name) => name === 'is_paused').length === 1
      && columnsOf(db, 'plans').filter((name) => name === 'day_of_month').length === 1);

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
