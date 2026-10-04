/**
 * Exact medication amounts in the client.
 *
 * The API never sends a decimal: every amount arrives as an integer
 * numerator/denominator pair. The client keeps it that way, so a half tablet stays
 * `1/2` instead of becoming `0.5` and drifting the moment anything adds it up.
 */

export type Quantity = {
  readonly numerator: number;
  readonly denominator: number;
  /** Invariant rendering from the server, e.g. `7` or `3/2`. */
  readonly display: string;
};

export const ZERO: Quantity = { numerator: 0, denominator: 1, display: '0' };

/** Vulgar fractions for the amounts households actually write on a box. */
const VULGAR: Record<string, string> = {
  '1/2': '½',
  '1/3': '⅓',
  '2/3': '⅔',
  '1/4': '¼',
  '3/4': '¾',
};

/**
 * Formats an amount for reading: `20`, `7½`, `¾`.
 *
 * Mixed numbers are used because "seven and a half tablets" is how people say it,
 * while the underlying value stays exact.
 */
export function formatQuantity(value: Quantity | null | undefined): string {
  if (!value) {
    return '—';
  }

  // Reduced first, and not as tidiness. Without it an unreduced whole amount printed its
  // whole part and a zero remainder side by side: 4/2 read as "20/2" and 6/3 as "20/3" —
  // a different number entirely, on the screen somebody reads with a box in their hand.
  //
  // `ExactQuantity` reduces on construction, so the API cannot send 4/2 today. That is
  // why this was never seen rather than why it was safe: the formatter was relying on an
  // invariant it does not itself enforce, and the failure mode was silent and wrong.
  const { numerator, denominator } = reduce(value.numerator, value.denominator);

  if (denominator === 1) {
    return String(numerator);
  }

  const sign = numerator < 0 ? '-' : '';
  const magnitude = Math.abs(numerator);
  const whole = Math.floor(magnitude / denominator);
  const remainder = magnitude % denominator;
  const fraction = VULGAR[`${remainder}/${denominator}`] ?? `${remainder}/${denominator}`;

  if (whole === 0) {
    return `${sign}${fraction}`;
  }

  return `${sign}${whole}${fraction}`;
}

/**
 * Parses what a user typed into an exact pair: `2`, `1.5`, `3/2`, `1 1/2`, `0,5`.
 *
 * Decimal input is converted through its digits rather than through a float, so
 * `0.1` becomes `1/10` exactly.
 */
export function parseQuantity(input: string): { numerator: number; denominator: number } | null {
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

/** Adds exact amounts without ever converting to a float. */
export function addQuantities(values: readonly Quantity[]): { numerator: number; denominator: number } {
  return values.reduce<{ numerator: number; denominator: number }>(
    (total, value) =>
      reduce(
        total.numerator * value.denominator + value.numerator * total.denominator,
        total.denominator * value.denominator,
      ),
    { numerator: 0, denominator: 1 },
  );
}

/** Exact comparison by cross-multiplication. */
export function compareQuantities(left: Quantity, right: Quantity): number {
  return left.numerator * right.denominator - right.numerator * left.denominator;
}

export function isPositive(value: Quantity | null | undefined): boolean {
  return !!value && value.numerator > 0;
}

function reduce(numerator: number, denominator: number): { numerator: number; denominator: number } {
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
