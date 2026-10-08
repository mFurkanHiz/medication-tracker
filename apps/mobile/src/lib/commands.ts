import type { AddStockRequest, RetirePackageRequest, SetPlanPausedRequest } from './api';
import type { MessageKey, Translate } from './i18n';
import { compareQuantities, formatQuantity, parseQuantity, type Quantity } from './quantity';

/**
 * The commands the phone can queue besides a dose, and the pure parts of handling them:
 * what each one is called, how the add-stock form becomes a request, and in which order
 * a mixed queue of doses and commands is sent. No SQLite or network here, so it is
 * unit-tested.
 */

export type CommandKind = 'plan.pause' | 'stock.add' | 'package.pin' | 'package.retire';

export type CommandPayload =
  | { kind: 'plan.pause'; body: SetPlanPausedRequest }
  | { kind: 'stock.add'; body: AddStockRequest }
  | { kind: 'package.pin'; body: Record<string, never> }
  | { kind: 'package.retire'; body: RetirePackageRequest };

/** The command's name in the reader's words: "Şimdilik ara ver", "Stok ekle: 2 kutu". */
export function describeCommand(kind: string, payload: unknown, t: Translate): string {
  const body = (payload ?? {}) as Record<string, unknown>;

  switch (kind as CommandKind) {
    case 'plan.pause':
      return t(body.isPaused ? 'pausePlan' : 'resumePlan');
    case 'package.pin':
      return t('makeActive');
    case 'package.retire':
      return t(body.state === 'Disposed' ? 'markDisposed' : 'markLost');
    case 'stock.add':
      return `${t('addStock')}: ${describeStock(body as Partial<AddStockRequest>, t)}`;
    default:
      return kind;
  }
}

function describeStock(body: Partial<AddStockRequest>, t: Translate): string {
  const parts: string[] = [];

  if (body.fullPackages) {
    parts.push(`${body.fullPackages} ${t('sealedBoxes')}`);
  }

  for (const opened of body.openedPackages ?? []) {
    const remaining = formatQuantity({
      numerator: opened.remainingNumerator,
      denominator: opened.remainingDenominator ?? 1,
    });
    parts.push(`${t('openedBox')} (${remaining})`);
  }

  if (body.looseNumerator) {
    parts.push(`${formatQuantity({ numerator: body.looseNumerator, denominator: body.looseDenominator ?? 1 })} ${t('looseLabel').toLowerCase()}`);
  }

  return parts.join(' + ');
}

export type AddStockForm = {
  fullPackages: string;
  openedRemaining: string;
  capacity: string;
  loose: string;
};

export type AddStockOutcome =
  | { ok: true; body: Omit<AddStockRequest, 'idempotencyKey'> }
  | { ok: false; error: MessageKey };

/**
 * Turns what was typed into the server's request, refusing what the server would refuse
 * so the user hears it now rather than from a rejection after the next sync.
 *
 * Capacity is required unless the catalogue has a default the server will use.
 */
export function buildAddStock(form: AddStockForm, defaultCapacity: Quantity | null): AddStockOutcome {
  const fullPackages = form.fullPackages.trim() === '' ? 0 : Number(form.fullPackages.trim());
  if (!Number.isInteger(fullPackages) || fullPackages < 0 || fullPackages > 100) {
    return { ok: false, error: 'stockAddInvalid' };
  }

  const opened = form.openedRemaining.trim() === '' ? null : parseQuantity(form.openedRemaining);
  if (form.openedRemaining.trim() !== '' && (!opened || opened.numerator < 0)) {
    return { ok: false, error: 'stockAddInvalid' };
  }

  const loose = form.loose.trim() === '' ? null : parseQuantity(form.loose);
  if (form.loose.trim() !== '' && (!loose || loose.numerator <= 0)) {
    return { ok: false, error: 'stockAddInvalid' };
  }

  if (fullPackages === 0 && opened === null && loose === null) {
    return { ok: false, error: 'stockAddInvalid' };
  }

  const typedCapacity = form.capacity.trim() === '' ? null : parseQuantity(form.capacity);
  if (form.capacity.trim() !== '' && (!typedCapacity || typedCapacity.numerator <= 0)) {
    return { ok: false, error: 'capacityRequired' };
  }

  const capacity = typedCapacity ?? defaultCapacity;
  const needsCapacity = fullPackages > 0 || opened !== null;
  if (needsCapacity && capacity === null) {
    return { ok: false, error: 'capacityRequired' };
  }

  if (opened && capacity && compareQuantities(opened, capacity) > 0) {
    return { ok: false, error: 'remainingExceedsCapacity' };
  }

  return {
    ok: true,
    body: {
      ...(typedCapacity
        ? { capacityNumerator: typedCapacity.numerator, capacityDenominator: typedCapacity.denominator }
        : {}),
      fullPackages,
      openedPackages: opened
        ? [{ remainingNumerator: opened.numerator, remainingDenominator: opened.denominator }]
        : [],
      ...(loose ? { looseNumerator: loose.numerator, looseDenominator: loose.denominator } : {}),
    },
  };
}

export type Queued<T> = { createdAt: string; item: T };

/** ISO timestamps compared as instants, not as text: "…:02.500Z" sorts after "…:02Z". */
function isNotAfter(left: string, right: string): boolean {
  const a = Date.parse(left);
  const b = Date.parse(right);
  return Number.isNaN(a) || Number.isNaN(b) ? left <= right : a <= b;
}

/**
 * One send order for two queues. Creation time decides; at the same instant a dose
 * goes first, because it is the record a household member cares most about reaching
 * the server, and a command rarely depends on a dose.
 */
export function interleave<D, C>(
  doses: readonly Queued<D>[],
  commands: readonly Queued<C>[],
): ({ kind: 'dose'; item: D } | { kind: 'command'; item: C })[] {
  const merged: ({ kind: 'dose'; item: D } | { kind: 'command'; item: C })[] = [];
  let d = 0;
  let c = 0;

  while (d < doses.length || c < commands.length) {
    const dose = doses[d];
    const command = commands[c];

    if (dose && (!command || isNotAfter(dose.createdAt, command.createdAt))) {
      merged.push({ kind: 'dose', item: dose.item });
      d += 1;
    } else if (command) {
      merged.push({ kind: 'command', item: command.item });
      c += 1;
    }
  }

  return merged;
}

/** "Parol · Ayşe" for a plan, "Parol · Kutu 3" for a box, "Parol" for added stock. */
export function describeTarget(
  row: { medicationName: string | null; personName: string | null; packageLabel: string | null; packageOrdinal: number | null },
  t: Translate,
): string {
  const parts: string[] = [];
  if (row.medicationName) {
    parts.push(row.medicationName);
  }
  if (row.personName) {
    parts.push(row.personName);
  }
  if (row.packageLabel) {
    parts.push(row.packageLabel);
  } else if (row.packageOrdinal !== null) {
    parts.push(`${t('packageOrdinal')} ${row.packageOrdinal}`);
  }
  return parts.join(' · ');
}
