import type {
  AddStockRequest,
  CountLineInput,
  CountRequest,
  RetirePackageRequest,
  SetPlanPausedRequest,
  UpdatePackageRequest,
} from './api';
import type { MessageKey, Translate } from './i18n';
import { compareQuantities, formatQuantity, parseQuantity, type Quantity } from './quantity';

/**
 * The commands the phone can queue besides a dose, and the pure parts of handling them:
 * what each one is called, how the add-stock form becomes a request, and in which order
 * a mixed queue of doses and commands is sent. No SQLite or network here, so it is
 * unit-tested.
 */

export type CommandKind =
  | 'plan.pause'
  | 'plan.end'
  | 'plan.restart'
  | 'stock.add'
  | 'package.pin'
  | 'package.unpin'
  | 'package.retire'
  | 'package.reinstate'
  | 'package.update'
  | 'package.assign'
  | 'package.lend'
  | 'loan.return'
  | 'inventory.count';

export type CommandPayload =
  | { kind: 'plan.pause'; body: SetPlanPausedRequest }
  | { kind: 'plan.end'; body: { endsOn: string } }
  | { kind: 'plan.restart'; body: { startsOn: string } }
  | { kind: 'stock.add'; body: AddStockRequest }
  | { kind: 'package.pin'; body: Record<string, never> }
  | { kind: 'package.unpin'; body: Record<string, never> }
  | { kind: 'package.retire'; body: RetirePackageRequest }
  | { kind: 'package.reinstate'; body: Record<string, never> }
  | { kind: 'package.update'; body: UpdatePackageRequest }
  | { kind: 'package.assign'; body: { personId: string | null } }
  | { kind: 'package.lend'; body: { borrowerPersonId: string } }
  /** `packageId` rides along so the phone can show the box again once the loan is back. */
  | { kind: 'loan.return'; body: { packageId: string } }
  | { kind: 'inventory.count'; body: CountRequest };

/** The kinds whose target is a box, for the queue's joins and the Stock screen's badges. */
export const PACKAGE_COMMAND_KINDS: readonly CommandKind[] = [
  'package.pin', 'package.unpin', 'package.retire', 'package.reinstate', 'package.update', 'package.assign', 'package.lend',
];

/** The command's name in the reader's words: "Şimdilik ara ver", "Stok ekle: 2 kutu". */
export function describeCommand(kind: string, payload: unknown, t: Translate): string {
  const body = (payload ?? {}) as Record<string, unknown>;

  switch (kind as CommandKind) {
    case 'plan.pause':
      return t(body.isPaused ? 'pausePlan' : 'resumePlan');
    case 'plan.end':
      return `${t('endPlan')} · ${String(body.endsOn ?? '')}`;
    case 'plan.restart':
      return `${t('restartPlan')} · ${String(body.startsOn ?? '')}`;
    case 'package.pin':
      return t('makeActive');
    case 'package.unpin':
      return t('unpin');
    case 'package.retire':
      return t(body.state === 'Disposed' ? 'markDisposed' : 'markLost');
    case 'package.reinstate':
      return t('reinstatePackage');
    case 'package.update':
      return t('editPackage');
    case 'package.assign':
      return body.personId ? t('assignTo') : t('noOwner');
    case 'package.lend':
      return t('lend');
    case 'loan.return':
      return t('returnLoan');
    case 'stock.add':
      return `${t('addStock')}: ${describeStock(body as Partial<AddStockRequest>, t)}`;
    case 'inventory.count': {
      const lines = Array.isArray(body.lines) ? body.lines.length : 0;
      return `${t('counting')}: ${lines} ${t('countLines')}`;
    }
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

export type CountOutcome =
  | { ok: true; lines: CountLineInput[] }
  | { ok: false; error: 'countingInvalidAmount' | 'countingNothingEntered' };

/**
 * What was typed on the counting sheet, as the server's lines. Keys are a medicine's id,
 * or `medicine:box` for one box. Blank rows are left alone; one unreadable row refuses
 * the whole count, because a count that quietly dropped a row would write a
 * reconciliation the household did not agree to — the web's rule, kept.
 */
export function buildCountLines(entries: Record<string, string>): CountOutcome {
  const lines: CountLineInput[] = [];

  for (const [key, raw] of Object.entries(entries)) {
    if (raw.trim() === '') {
      continue;
    }

    const parsed = parseQuantity(raw);
    if (!parsed || parsed.numerator < 0) {
      return { ok: false, error: 'countingInvalidAmount' };
    }

    const [medicationDefinitionId, packageId] = key.split(':');
    lines.push({
      medicationDefinitionId,
      observedNumerator: parsed.numerator,
      observedDenominator: parsed.denominator,
      ...(packageId ? { packageId } : {}),
    });
  }

  return lines.length === 0 ? { ok: false, error: 'countingNothingEntered' } : { ok: true, lines };
}

/** A calendar day typed as `YYYY-MM-DD` and real: 2026-02-30 is not a day. */
export function isValidDay(text: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text.trim());
  if (!match) {
    return false;
  }

  const [, year, month, day] = match.map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

/**
 * A refusal that says the command's goal is already reached: a box the server says is
 * not retired is back, a loan it says is already returned is home. The web shows these
 * as errors for a double click; the phone, replaying a command whose answer never
 * arrived, treats them as done rather than as a failure to show.
 */
export function alreadyDone(kind: string, code: string): boolean {
  return (
    (kind === 'package.reinstate' && code === 'package_not_retired') ||
    (kind === 'loan.return' && code === 'already_returned')
  );
}
