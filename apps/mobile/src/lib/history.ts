import type { MessageKey, Translate } from './i18n';
import { formatQuantity, type Quantity } from './quantity';

/**
 * How the History screen words what happened — the web's wording, so a household
 * member reads the same event the same way on both screens. Pure, so it is unit-tested.
 */

/** The server's ledger entry types, keyed to their words. An unknown type shows its raw name. */
const ENTRY_KEYS: Record<string, MessageKey> = {
  Acquire: 'entryAcquire',
  Consume: 'entryConsume',
  CorrectionReversal: 'entryCorrectionReversal',
  CorrectionConsume: 'entryCorrectionConsume',
  Found: 'entryFound',
  Loss: 'entryLoss',
  Dispose: 'entryDispose',
  CountAdjustment: 'entryCountAdjustment',
  PackageTransfer: 'entryPackageTransfer',
  ManualAdjustment: 'entryManualAdjustment',
};

export function entryKey(entryType: string): MessageKey | null {
  return ENTRY_KEYS[entryType] ?? null;
}

const OUTCOME_KEYS: Record<string, MessageKey> = {
  Taken: 'taken',
  Skipped: 'outcomeSkipped',
  PartialDose: 'partialDose',
  ExtraDose: 'extraDose',
};

export function outcomeKey(outcome: string): MessageKey | null {
  return OUTCOME_KEYS[outcome] ?? null;
}

/** Lateness worth a badge: a quarter of an hour either way, as on the web. */
export const LATENESS_THRESHOLD_MINUTES = 15;

/** `"20 dk geç"`, `"30 dk erken"`, or null when the dose was close enough to its time. */
export function latenessLabel(minutes: number | null, t: Translate): string | null {
  if (minutes === null || Math.abs(minutes) < LATENESS_THRESHOLD_MINUTES) {
    return null;
  }

  return `${Math.abs(minutes)} ${t(minutes > 0 ? 'lateBy' : 'earlyBy')}`;
}

/** "Kutu 3", or the loose-stock word when the movement touched no box. */
export function packageLabel(label: number | null, t: Translate): string {
  return label === null ? t('looseStock') : `${t('packageOrdinal')} ${label}`;
}

/** A stock movement with its sign, so "+20" and "-1" read as what they did. */
export function signedQuantity(quantity: Quantity): string {
  const text = formatQuantity(quantity);
  return quantity.numerator > 0 ? `+${text}` : text;
}
