/**
 * Exact medication amounts on the device.
 *
 * The API never sends a decimal and SQLite never stores one: every amount is an
 * integer numerator/denominator pair, so a half tablet is 1/2 and summing a column of
 * ledger rows cannot drift.
 *
 * Deliberately duplicated from the web client rather than shared through a workspace
 * package: Metro resolves a React Native bundle differently from a Next build, and a
 * hundred lines of pure arithmetic is a smaller cost than a shared build target.
 */

export type Quantity = {
  readonly numerator: number;
  readonly denominator: number;
};

export const ZERO: Quantity = { numerator: 0, denominator: 1 };
export const ONE: Quantity = { numerator: 1, denominator: 1 };

const VULGAR: Record<string, string> = {
  '1/2': '½',
  '1/3': '⅓',
  '2/3': '⅔',
  '1/4': '¼',
  '3/4': '¾',
};

/** Reads as `20`, `7½`, `¾`. The underlying value stays exact. */
export function formatQuantity(value: Quantity | null | undefined): string {
  if (!value) {
    return '—';
  }

  const { numerator, denominator } = value;
  if (denominator === 1) {
    return String(numerator);
  }

  const sign = numerator < 0 ? '-' : '';
  const magnitude = Math.abs(numerator);
  const whole = Math.floor(magnitude / denominator);
  const remainder = magnitude % denominator;
  const fraction = VULGAR[`${remainder}/${denominator}`] ?? `${remainder}/${denominator}`;

  return whole === 0 ? `${sign}${fraction}` : `${sign}${whole}${fraction}`;
}

/**
 * Parses what someone typed: `2`, `1.5`, `0,5`, `3/2`, `1 1/2`.
 *
 * Decimals are converted through their digits, never through a float, so `0.1` becomes
 * exactly `1/10`.
 */
export function parseQuantity(input: string): Quantity | null {
  const text = input.trim().replace(',', '.');
  if (text === '') {
    return null;
  }

  const mixed = /^(-?\d+)\s+(\d+)\/(\d+)$/.exec(text);
  if (mixed) {
    const whole = Number(mixed[1]);
    const numerator = Number(mixed[2]);
    const denominator = Number(mixed[3]);
    if (denominator <= 0 || numerator < 0) {
      return null;
    }
    const sign = whole < 0 ? -1 : 1;
    return reduce(sign * (Math.abs(whole) * denominator + numerator), denominator);
  }

  const fraction = /^(-?\d+)\/(\d+)$/.exec(text);
  if (fraction) {
    const denominator = Number(fraction[2]);
    return denominator > 0 ? reduce(Number(fraction[1]), denominator) : null;
  }

  const decimal = /^(-?)(\d*)(?:\.(\d+))?$/.exec(text);
  if (decimal && (decimal[2] !== '' || decimal[3])) {
    const sign = decimal[1] === '-' ? -1 : 1;
    const whole = decimal[2] === '' ? 0 : Number(decimal[2]);
    const decimals = decimal[3] ?? '';
    const denominator = 10 ** decimals.length;
    const numerator = whole * denominator + (decimals === '' ? 0 : Number(decimals));
    return reduce(sign * numerator, denominator);
  }

  return null;
}

export function addQuantities(values: readonly Quantity[]): Quantity {
  return values.reduce<Quantity>(
    (total, value) =>
      reduce(
        total.numerator * value.denominator + value.numerator * total.denominator,
        total.denominator * value.denominator,
      ),
    ZERO,
  );
}

export function subtractQuantity(left: Quantity, right: Quantity): Quantity {
  return reduce(
    left.numerator * right.denominator - right.numerator * left.denominator,
    left.denominator * right.denominator,
  );
}

/** Exact ordering by cross-multiplication, never by converting to a float. */
export function compareQuantities(left: Quantity, right: Quantity): number {
  return left.numerator * right.denominator - right.numerator * left.denominator;
}

export function isPositive(value: Quantity | null | undefined): boolean {
  return !!value && value.numerator > 0;
}

/** How many whole doses the balance covers. Used for the local depletion hint. */
export function wholeMultiplesOf(balance: Quantity, dose: Quantity): number {
  if (balance.numerator <= 0 || dose.numerator <= 0) {
    return 0;
  }

  return Math.floor((balance.numerator * dose.denominator) / (balance.denominator * dose.numerator));
}

function reduce(numerator: number, denominator: number): Quantity {
  if (denominator < 0) {
    numerator = -numerator;
    denominator = -denominator;
  }

  const divisor = greatestCommonDivisor(Math.abs(numerator), denominator);
  return { numerator: numerator / divisor, denominator: denominator / divisor };
}

function greatestCommonDivisor(left: number, right: number): number {
  while (right !== 0) {
    [left, right] = [right, left % right];
  }

  return left === 0 ? 1 : left;
}
