import type { SQLiteDatabase } from 'expo-sqlite';

export type TodayDose = {
  regimenVersionId: string; personName: string; medicationName: string;
  doseNumerator: number; doseDenominator: number; scheduledFor: string; taken: boolean;
};

export async function migrateDatabase(db: SQLiteDatabase) {
  const row = await db.getFirstAsync<{ user_version: number }>('PRAGMA user_version');
  if ((row?.user_version ?? 0) >= 1) return;
  await db.withExclusiveTransactionAsync(async transaction => {
    await transaction.execAsync(`
      PRAGMA journal_mode = WAL;
      PRAGMA foreign_keys = ON;
      CREATE TABLE people (id TEXT PRIMARY KEY, name TEXT NOT NULL);
      CREATE TABLE medications (id TEXT PRIMARY KEY, person_id TEXT NOT NULL, name TEXT NOT NULL, form TEXT NOT NULL CHECK(form = 'tablet'));
      CREATE TABLE inventory_items (id TEXT PRIMARY KEY, medication_id TEXT NOT NULL UNIQUE);
      CREATE TABLE inventory_ledger (id TEXT PRIMARY KEY, inventory_item_id TEXT NOT NULL, administration_id TEXT UNIQUE, quantity_numerator INTEGER NOT NULL, quantity_denominator INTEGER NOT NULL CHECK(quantity_denominator > 0), reason TEXT NOT NULL, occurred_at TEXT NOT NULL);
      CREATE TABLE regimens (id TEXT PRIMARY KEY, person_id TEXT NOT NULL, medication_id TEXT NOT NULL);
      CREATE TABLE regimen_versions (id TEXT PRIMARY KEY, regimen_id TEXT NOT NULL, valid_from TEXT NOT NULL, valid_to TEXT, dose_numerator INTEGER NOT NULL, dose_denominator INTEGER NOT NULL CHECK(dose_denominator > 0), local_time TEXT NOT NULL);
      CREATE TABLE administrations (id TEXT PRIMARY KEY, regimen_version_id TEXT NOT NULL, scheduled_for TEXT NOT NULL, taken_at TEXT NOT NULL, UNIQUE(regimen_version_id, scheduled_for));
      CREATE TABLE outbox (id TEXT PRIMARY KEY, idempotency_key TEXT NOT NULL UNIQUE, kind TEXT NOT NULL, payload TEXT NOT NULL, created_at TEXT NOT NULL, synced_at TEXT);
      PRAGMA user_version = 1;
    `);
  });
}

const id = () => `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
const localDate = () => new Date().toLocaleDateString('en-CA');

export async function createSamplePlan(db: SQLiteDatabase, personName: string, medicationName: string) {
  if (await db.getFirstAsync('SELECT 1 FROM regimen_versions LIMIT 1')) return;
  await db.withExclusiveTransactionAsync(async transaction => {
    const personId = id(), medicationId = id(), itemId = id(), regimenId = id(), versionId = id();
    await transaction.runAsync('INSERT INTO people VALUES (?, ?)', personId, personName);
    await transaction.runAsync('INSERT INTO medications VALUES (?, ?, ?, ?)', medicationId, personId, medicationName, 'tablet');
    await transaction.runAsync('INSERT INTO inventory_items VALUES (?, ?)', itemId, medicationId);
    await transaction.runAsync('INSERT INTO inventory_ledger VALUES (?, ?, NULL, ?, ?, ?, ?)', id(), itemId, 15, 2, 'acquisition', new Date().toISOString());
    await transaction.runAsync('INSERT INTO regimens VALUES (?, ?, ?)', regimenId, personId, medicationId);
    await transaction.runAsync('INSERT INTO regimen_versions VALUES (?, ?, ?, NULL, ?, ?, ?)', versionId, regimenId, localDate(), 1, 2, '09:00');
  });
}

export async function getToday(db: SQLiteDatabase): Promise<TodayDose[]> {
  const rows = await db.getAllAsync<Omit<TodayDose, 'taken'> & { administrationId: string | null }>(`
    SELECT rv.id regimenVersionId, p.name personName, m.name medicationName,
      rv.dose_numerator doseNumerator, rv.dose_denominator doseDenominator,
      date('now', 'localtime') || 'T' || rv.local_time || ':00' scheduledFor, a.id administrationId
    FROM regimen_versions rv JOIN regimens r ON r.id = rv.regimen_id
    JOIN people p ON p.id = r.person_id JOIN medications m ON m.id = r.medication_id
    LEFT JOIN administrations a ON a.regimen_version_id = rv.id AND date(a.scheduled_for) = date('now', 'localtime')
    WHERE rv.valid_from <= date('now', 'localtime') AND (rv.valid_to IS NULL OR rv.valid_to >= date('now', 'localtime'))`);
  return rows.map(row => ({ ...row, taken: row.administrationId !== null }));
}

export async function markTaken(db: SQLiteDatabase, dose: TodayDose) {
  await db.withExclusiveTransactionAsync(async transaction => {
    const administrationId = id();
    const result = await transaction.runAsync('INSERT OR IGNORE INTO administrations VALUES (?, ?, ?, ?)', administrationId, dose.regimenVersionId, dose.scheduledFor, new Date().toISOString());
    if (result.changes === 0) return;
    const item = await transaction.getFirstAsync<{ id: string }>('SELECT i.id FROM inventory_items i JOIN medications m ON m.id = i.medication_id JOIN regimens r ON r.medication_id = m.id JOIN regimen_versions rv ON rv.regimen_id = r.id WHERE rv.id = ?', dose.regimenVersionId);
    if (!item) throw new Error('inventory_item_missing');
    await transaction.runAsync('INSERT INTO inventory_ledger VALUES (?, ?, ?, ?, ?, ?, ?)', id(), item.id, administrationId, -dose.doseNumerator, dose.doseDenominator, 'administration', new Date().toISOString());
    const outboxId = id();
    await transaction.runAsync('INSERT INTO outbox VALUES (?, ?, ?, ?, ?, NULL)', outboxId, `administration:${administrationId}`, 'administration.recorded', JSON.stringify({ administrationId, regimenVersionId: dose.regimenVersionId, scheduledFor: dose.scheduledFor }), new Date().toISOString());
  });
}

export async function getSummary(db: SQLiteDatabase) {
  const entries = await db.getAllAsync<{ numerator: number; denominator: number }>('SELECT quantity_numerator numerator, quantity_denominator denominator FROM inventory_ledger');
  const value = entries.reduce((sum, entry) => normalize(sum.numerator * entry.denominator + entry.numerator * sum.denominator, sum.denominator * entry.denominator), { numerator: 0, denominator: 1 });
  const pending = await db.getFirstAsync<{ count: number }>('SELECT count(*) count FROM outbox WHERE synced_at IS NULL');
  return { stock: formatQuantity(value.numerator, value.denominator), pending: pending?.count ?? 0 };
}

function normalize(numerator: number, denominator: number) {
  let left = Math.abs(numerator), right = denominator;
  while (right !== 0) [left, right] = [right, left % right];
  const divisor = left || 1;
  return { numerator: numerator / divisor, denominator: denominator / divisor };
}

function formatQuantity(numerator: number, denominator: number) {
  const whole = Math.trunc(numerator / denominator), remainder = Math.abs(numerator % denominator);
  if (remainder === 0) return `${whole}`;
  return whole === 0 ? `${remainder}/${denominator}` : `${whole} ${remainder}/${denominator}`;
}
