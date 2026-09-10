import type { SQLiteDatabase } from 'expo-sqlite';
import { randomUUID } from 'expo-crypto';

export type TodayDose = {
  regimenVersionId: string; personName: string; medicationName: string;
  doseNumerator: number; doseDenominator: number; scheduledFor: string; outcome: 'due' | 'taken' | 'skipped';
};

export async function migrateDatabase(db: SQLiteDatabase) {
  await db.execAsync('PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;');
  const row = await db.getFirstAsync<{ user_version: number }>('PRAGMA user_version');
  let version = row?.user_version ?? 0;
  if (version < 1) {
    await db.withExclusiveTransactionAsync(async transaction => {
      await transaction.execAsync(`
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
    version = 1;
  }
  if (version < 2) await db.withExclusiveTransactionAsync(async transaction => {
    await transaction.execAsync("ALTER TABLE administrations ADD COLUMN outcome TEXT NOT NULL DEFAULT 'taken' CHECK(outcome IN ('taken', 'skipped')); PRAGMA user_version = 2;");
  });
  if (version < 3) await db.withExclusiveTransactionAsync(async transaction => {
    await transaction.execAsync("ALTER TABLE regimen_versions ADD COLUMN time_zone_id TEXT NOT NULL DEFAULT 'Europe/Istanbul'; PRAGMA user_version = 3;");
  });
}

const id = () => randomUUID();
const localDate = () => new Date().toLocaleDateString('en-CA');

export async function createPlan(db: SQLiteDatabase, personName: string, medicationName: string, stockText: string, doseText: string, time: string) {
  const parse = (text: string) => { if (!/^\d+(\/\d+)?$/.test(text)) throw new Error('invalid_quantity'); const [n, d = 1] = text.split('/').map(Number); if (n <= 0 || n > 1000000 || d <= 0 || d > 10000) throw new Error('invalid_quantity'); return [n, d]; };
  const [stockN, stockD] = parse(stockText); const [doseN, doseD] = parse(doseText);
  if (!personName.trim() || !medicationName.trim() || personName.length > 160 || medicationName.length > 200 || !/^([01]\d|2[0-3]):[0-5]\d$/.test(time)) throw new Error('invalid_plan');
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  await db.withExclusiveTransactionAsync(async transaction => {
    const personId = id(), medicationId = id(), itemId = id(), regimenId = id(), versionId = id();
    await transaction.runAsync('INSERT INTO people VALUES (?, ?)', personId, personName);
    await addOutbox(transaction, `person:${personId}`, 'person.created', { id: personId, name: personName });
    await transaction.runAsync('INSERT INTO medications VALUES (?, ?, ?, ?)', medicationId, personId, medicationName, 'tablet');
    await transaction.runAsync('INSERT INTO inventory_items VALUES (?, ?)', itemId, medicationId);
    await transaction.runAsync('INSERT INTO inventory_ledger VALUES (?, ?, NULL, ?, ?, ?, ?)', id(), itemId, stockN, stockD, 'acquisition', new Date().toISOString());
    await addOutbox(transaction, `medication:${medicationId}`, 'medication.created', { id: medicationId, personId, inventoryItemId: itemId, name: medicationName, stockNumerator: stockN, stockDenominator: stockD });
    await transaction.runAsync('INSERT INTO regimens VALUES (?, ?, ?)', regimenId, personId, medicationId);
    await transaction.runAsync('INSERT INTO regimen_versions VALUES (?, ?, ?, NULL, ?, ?, ?, ?)', versionId, regimenId, localDate(), doseN, doseD, time, zone);
    await addOutbox(transaction, `regimen:${regimenId}`, 'regimen.created', { id: regimenId, versionId, personId, medicationId, validFrom: localDate(), doseNumerator: doseN, doseDenominator: doseD, localTime: `${time}:00`, timeZoneId: zone });
  });
}

export async function getToday(db: SQLiteDatabase): Promise<TodayDose[]> {
  const rows = await db.getAllAsync<Omit<TodayDose, 'outcome' | 'scheduledFor'> & { validFrom: string; validTo: string | null; localTime: string; timeZoneId: string }>(`
    SELECT rv.id regimenVersionId, p.name personName, m.name medicationName,
      rv.dose_numerator doseNumerator, rv.dose_denominator doseDenominator,
      rv.valid_from validFrom, rv.valid_to validTo, rv.local_time localTime, rv.time_zone_id timeZoneId
    FROM regimen_versions rv JOIN regimens r ON r.id = rv.regimen_id
    JOIN people p ON p.id = r.person_id JOIN medications m ON m.id = r.medication_id`);
  const administrations = await db.getAllAsync<{ regimenVersionId: string; scheduledFor: string; outcome: 'taken' | 'skipped' }>('SELECT regimen_version_id regimenVersionId, scheduled_for scheduledFor, outcome FROM administrations');
  return rows.flatMap<TodayDose>(row => {
    const day = zonedParts(new Date(), row.timeZoneId).slice(0, 10);
    if (day < row.validFrom || (row.validTo && day > row.validTo)) return [];
    const scheduledFor = localInstant(day, row.localTime, row.timeZoneId);
    const outcome = administrations.find(a => a.regimenVersionId === row.regimenVersionId && new Date(a.scheduledFor).toISOString() === scheduledFor)?.outcome ?? 'due';
    return [{ ...row, scheduledFor, outcome }];
  }).sort((a, b) => a.scheduledFor.localeCompare(b.scheduledFor));
}

function zonedParts(date: Date, timeZone: string) {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }).formatToParts(date);
  const p = Object.fromEntries(parts.map(part => [part.type, part.value]));
  return `${p.year}-${p.month}-${p.day}T${p.hour}:${p.minute}:${p.second}`;
}
function localInstant(day: string, time: string, zone: string) {
  const target = `${day}T${time.slice(0, 5)}:00`;
  const wall = Date.parse(`${target}Z`);
  const offsets = [-36, 0, 36].map(hours => { const instant = wall + hours * 3600000; return Date.parse(`${zonedParts(new Date(instant), zone)}Z`) - instant; });
  const candidates = [...new Set(offsets)].map(offset => wall - offset);
  const valid = candidates.filter(candidate => zonedParts(new Date(candidate), zone) === target);
  // Repeated wall time uses the later instant; a gap moves forward by its offset change.
  return new Date(Math.max(...(valid.length ? valid : candidates))).toISOString();
}

export async function recordOutcome(db: SQLiteDatabase, dose: TodayDose, outcome: 'taken' | 'skipped') {
  await db.withExclusiveTransactionAsync(async transaction => {
    const administrationId = id();
    const occurredAt = new Date().toISOString();
    const result = await transaction.runAsync('INSERT OR IGNORE INTO administrations (id, regimen_version_id, scheduled_for, taken_at, outcome) VALUES (?, ?, ?, ?, ?)', administrationId, dose.regimenVersionId, dose.scheduledFor, occurredAt, outcome);
    if (result.changes === 0) return;
    if (outcome === 'taken') {
      const item = await transaction.getFirstAsync<{ id: string }>('SELECT i.id FROM inventory_items i JOIN medications m ON m.id = i.medication_id JOIN regimens r ON r.medication_id = m.id JOIN regimen_versions rv ON rv.regimen_id = r.id WHERE rv.id = ?', dose.regimenVersionId);
      if (!item) throw new Error('inventory_item_missing');
      await transaction.runAsync('INSERT INTO inventory_ledger VALUES (?, ?, ?, ?, ?, ?, ?)', id(), item.id, administrationId, -dose.doseNumerator, dose.doseDenominator, 'administration', occurredAt);
    }
    await addOutbox(transaction, `administration:${administrationId}`, 'administration.recorded', { administrationId, regimenVersionId: dose.regimenVersionId, scheduledFor: dose.scheduledFor, takenAt: occurredAt, outcome });
  });
}

export async function getSummary(db: SQLiteDatabase) {
  const entries = await db.getAllAsync<{ numerator: number; denominator: number }>('SELECT quantity_numerator numerator, quantity_denominator denominator FROM inventory_ledger');
  const value = entries.reduce((sum, entry) => normalize(sum.numerator * entry.denominator + entry.numerator * sum.denominator, sum.denominator * entry.denominator), { numerator: 0, denominator: 1 });
  const pending = await db.getFirstAsync<{ count: number }>('SELECT count(*) count FROM outbox WHERE synced_at IS NULL');
  const dailyRows = await db.getAllAsync<{ numerator: number; denominator: number }>("SELECT dose_numerator numerator, dose_denominator denominator FROM regimen_versions WHERE valid_from <= date('now', 'localtime') AND (valid_to IS NULL OR valid_to >= date('now', 'localtime'))");
  const daily = dailyRows.reduce((sum, entry) => normalize(sum.numerator * entry.denominator + entry.numerator * sum.denominator, sum.denominator * entry.denominator), { numerator: 0, denominator: 1 });
  const depletionDays = value.numerator > 0 && daily.numerator > 0 ? Math.trunc((value.numerator * daily.denominator) / (value.denominator * daily.numerator)) : 0;
  return { stock: formatQuantity(value.numerator, value.denominator), pending: pending?.count ?? 0, depletionDays };
}

export async function getInventorySummary(db: SQLiteDatabase) {
  const medications = await db.getAllAsync<{ id: string; name: string; itemId: string }>('SELECT m.id, m.name, i.id itemId FROM medications m JOIN inventory_items i ON i.medication_id = m.id');
  return Promise.all(medications.map(async medication => {
    const entries = await db.getAllAsync<{ n: number; d: number }>('SELECT quantity_numerator n, quantity_denominator d FROM inventory_ledger WHERE inventory_item_id = ?', medication.itemId);
    const value = entries.reduce((s, e) => normalize(s.numerator * e.d + e.n * s.denominator, s.denominator * e.d), { numerator: 0, denominator: 1 });
    return { ...medication, stock: formatQuantity(value.numerator, value.denominator) };
  }));
}

async function addOutbox(transaction: SQLiteDatabase, idempotencyKey: string, kind: string, payload: object) {
  await transaction.runAsync('INSERT INTO outbox VALUES (?, ?, ?, ?, ?, NULL)', id(), idempotencyKey, kind, JSON.stringify(payload), new Date().toISOString());
}

export type SyncConfig = { apiUrl: string; accountId: string; householdId: string; accessToken: string };
export async function syncPending(db: SQLiteDatabase, config: SyncConfig) {
  const rows = await db.getAllAsync<{ id: string; idempotencyKey: string; kind: string; payload: string }>('SELECT id, idempotency_key idempotencyKey, kind, payload FROM outbox WHERE synced_at IS NULL ORDER BY rowid');
  let synced = 0;
  for (const row of rows) {
    const administration = row.kind === 'administration.recorded';
    const payload = JSON.parse(row.payload) as Record<string, unknown>;
    const response = await fetch(`${config.apiUrl.replace(/\/$/, '')}/api/households/${config.householdId}/sync/${administration ? 'administrations' : 'commands'}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Medication-Client': '1', Authorization: `Bearer ${config.accessToken}` },
      body: JSON.stringify(administration ? { idempotencyKey: row.idempotencyKey, ...payload } : { idempotencyKey: row.idempotencyKey, kind: row.kind, payload }),
    });
    if (!response.ok) throw new Error(`sync_failed_${response.status}`);
    await db.runAsync('UPDATE outbox SET synced_at = ? WHERE id = ?', new Date().toISOString(), row.id);
    synced += 1;
  }
  await pullWorkspace(db, config);
  return synced;
}

type RemoteWorkspace = {
  people: { id: string; name: string }[];
  medications: { id: string; personId: string; name: string; form: string; inventoryItemId: string }[];
  regimens: { id: string; versionId: string; personId: string; medicationId: string; validFrom: string; validTo: string | null; doseNumerator: number; doseDenominator: number; localTime: string; timeZoneId: string }[];
  ledger: { id: string; inventoryItemId: string; administrationEventId: string | null; quantityNumerator: number; quantityDenominator: number; reason: string; occurredAt: string }[];
  administrations: { id: string; regimenVersionId: string; scheduledFor: string; occurredAt: string; outcome: string }[];
};
async function pullWorkspace(db: SQLiteDatabase, config: SyncConfig) {
  const response = await fetch(`${config.apiUrl}/api/households/${config.householdId}/workspace`, { headers: { Authorization: `Bearer ${config.accessToken}` } });
  if (!response.ok) throw new Error('pull_failed');
  const data = await response.json() as RemoteWorkspace;
  await db.withExclusiveTransactionAsync(async tx => {
    // Replace only an acknowledged cache; a new offline action during fetch prevents replacement.
    if (await tx.getFirstAsync('SELECT 1 FROM outbox WHERE synced_at IS NULL LIMIT 1')) return;
    await tx.execAsync('DELETE FROM administrations; DELETE FROM inventory_ledger; DELETE FROM regimen_versions; DELETE FROM regimens; DELETE FROM inventory_items; DELETE FROM medications; DELETE FROM people;');
    for (const p of data.people) await tx.runAsync('INSERT INTO people VALUES (?, ?)', p.id, p.name);
    for (const m of data.medications) { await tx.runAsync('INSERT INTO medications VALUES (?, ?, ?, ?)', m.id, m.personId, m.name, m.form); await tx.runAsync('INSERT INTO inventory_items VALUES (?, ?)', m.inventoryItemId, m.id); }
    for (const r of data.regimens) { await tx.runAsync('INSERT OR IGNORE INTO regimens VALUES (?, ?, ?)', r.id, r.personId, r.medicationId); await tx.runAsync('INSERT INTO regimen_versions VALUES (?, ?, ?, ?, ?, ?, ?, ?)', r.versionId, r.id, r.validFrom, r.validTo, r.doseNumerator, r.doseDenominator, r.localTime.slice(0, 5), r.timeZoneId); }
    for (const e of data.ledger) await tx.runAsync('INSERT INTO inventory_ledger VALUES (?, ?, ?, ?, ?, ?, ?)', e.id, e.inventoryItemId, e.administrationEventId, e.quantityNumerator, e.quantityDenominator, e.reason, e.occurredAt);
    for (const a of data.administrations) await tx.runAsync('INSERT INTO administrations VALUES (?, ?, ?, ?, ?)', a.id, a.regimenVersionId, a.scheduledFor, a.occurredAt, a.outcome);
  });
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
