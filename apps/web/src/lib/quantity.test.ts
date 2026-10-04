import { describe, expect, it } from 'vitest';
import {
  addQuantities,
  compareQuantities,
  formatQuantity,
  isPositive,
  parseQuantity,
  type Quantity,
} from './quantity';

/**
 * Exact amounts in the client.
 *
 * This is the first thing the web client got tests for, because it is the module where
 * being wrong is read by somebody holding a box. Everything else on the screen is a label;
 * this is the number.
 */

const q = (numerator: number, denominator: number): Quantity => ({
  numerator,
  denominator,
  display: `${numerator}/${denominator}`,
});

describe('formatQuantity', () => {
  it('reads a whole amount as a whole number', () => {
    expect(formatQuantity(q(20, 1))).toBe('20');
    expect(formatQuantity(q(0, 1))).toBe('0');
  });

  it('reads the fractions a household writes on a box', () => {
    expect(formatQuantity(q(1, 2))).toBe('½');
    expect(formatQuantity(q(1, 3))).toBe('⅓');
    expect(formatQuantity(q(2, 3))).toBe('⅔');
    expect(formatQuantity(q(1, 4))).toBe('¼');
    expect(formatQuantity(q(3, 4))).toBe('¾');
  });

  it('reads a mixed amount the way people say it', () => {
    // "Seven and a half", not "fifteen halves". The stored value stays 15/2 either way.
    expect(formatQuantity(q(15, 2))).toBe('7½');
    expect(formatQuantity(q(3, 2))).toBe('1½');
  });

  it('keeps the sign in front, where a reader looks for it', () => {
    expect(formatQuantity(q(-3, 2))).toBe('-1½');
    expect(formatQuantity(q(-1, 2))).toBe('-½');
  });

  it('falls back to a plain fraction rather than inventing a glyph', () => {
    expect(formatQuantity(q(5, 7))).toBe('5/7');
    expect(formatQuantity(q(9, 7))).toBe('12/7');
  });

  it('says nothing rather than zero when there is no amount', () => {
    // A missing amount and an amount of zero are different facts about a dose.
    expect(formatQuantity(null)).toBe('—');
    expect(formatQuantity(undefined)).toBe('—');
  });

  /**
   * The case this test file was written for.
   *
   * An unreduced whole amount used to render as a different number entirely: `4/2` came
   * out as "20/2" — the whole part and a zero remainder printed side by side — and `6/3`
   * as "20/3". On a medication screen that is not a cosmetic fault, it is a reader taking
   * twenty of something.
   *
   * It was latent rather than live: `ExactQuantity` reduces on construction, so the API
   * cannot currently send `4/2`. That is a reason to pin it, not a reason to rely on it.
   * The formatter now holds on its own, because the client does not enforce the invariant
   * it was depending on.
   */
  it('reads an unreduced whole amount as that whole number', () => {
    expect(formatQuantity(q(4, 2))).toBe('2');
    expect(formatQuantity(q(6, 3))).toBe('2');
    expect(formatQuantity(q(0, 2))).toBe('0');
    expect(formatQuantity(q(-4, 2))).toBe('-2');
  });

  it('reads an unreduced fraction as its reduced self', () => {
    expect(formatQuantity(q(2, 4))).toBe('½');
    expect(formatQuantity(q(6, 4))).toBe('1½');
  });
});

describe('parseQuantity', () => {
  it('takes a whole number', () => {
    expect(parseQuantity('2')).toEqual({ numerator: 2, denominator: 1 });
    expect(parseQuantity('  7  ')).toEqual({ numerator: 7, denominator: 1 });
  });

  it('takes a decimal through its digits, not through a float', () => {
    // The reason the module does this by hand: 0.1 has no exact binary form, so a float
    // round trip would store 0.1000000000000000055… and drift once anything sums it.
    expect(parseQuantity('0.1')).toEqual({ numerator: 1, denominator: 10 });
    expect(parseQuantity('1.5')).toEqual({ numerator: 3, denominator: 2 });
    expect(parseQuantity('1.50')).toEqual({ numerator: 3, denominator: 2 });
    expect(parseQuantity('0.125')).toEqual({ numerator: 1, denominator: 8 });
  });

  it('takes a comma, because a Turkish keyboard types one', () => {
    expect(parseQuantity('0,5')).toEqual({ numerator: 1, denominator: 2 });
    expect(parseQuantity('2,25')).toEqual({ numerator: 9, denominator: 4 });
  });

  it('takes a fraction and a mixed number', () => {
    expect(parseQuantity('3/2')).toEqual({ numerator: 3, denominator: 2 });
    expect(parseQuantity('1 1/2')).toEqual({ numerator: 3, denominator: 2 });
    expect(parseQuantity('2 3/4')).toEqual({ numerator: 11, denominator: 4 });
    expect(parseQuantity('-1 1/2')).toEqual({ numerator: -3, denominator: 2 });
  });

  it('reduces what it returns', () => {
    expect(parseQuantity('2/4')).toEqual({ numerator: 1, denominator: 2 });
    expect(parseQuantity('4/2')).toEqual({ numerator: 2, denominator: 1 });
  });

  it('refuses what it cannot read instead of guessing', () => {
    // Returning null is the point: a guessed dose is worse than a rejected one.
    for (const input of ['', '   ', 'abc', '1/0', '1/', '/2', '.', '5.', '1..5', '½', '1 2']) {
      expect(parseQuantity(input), `"${input}"`).toBeNull();
    }
  });
});

describe('addQuantities', () => {
  it('adds without ever converting to a float', () => {
    expect(addQuantities([q(1, 2), q(1, 2)])).toEqual({ numerator: 1, denominator: 1 });
    expect(addQuantities([q(1, 3), q(1, 6)])).toEqual({ numerator: 1, denominator: 2 });
  });

  it('adds a tenth ten times and lands exactly on one', () => {
    // The whole reason the module exists. In floating point this sums to
    // 0.9999999999999999, and a stock total that cannot reach a round number is a stock
    // total somebody stops trusting.
    const tenths = Array.from({ length: 10 }, () => q(1, 10));

    expect(addQuantities(tenths)).toEqual({ numerator: 1, denominator: 1 });
  });

  it('starts from nothing rather than failing on an empty list', () => {
    expect(addQuantities([])).toEqual({ numerator: 0, denominator: 1 });
  });

  it('subtracts, because a negative amount is how the ledger says "left the box"', () => {
    expect(addQuantities([q(3, 2), q(-1, 2)])).toEqual({ numerator: 1, denominator: 1 });
  });
});

describe('compareQuantities', () => {
  it('compares by cross-multiplication rather than by value', () => {
    expect(compareQuantities(q(1, 2), q(1, 3))).toBeGreaterThan(0);
    expect(compareQuantities(q(1, 3), q(1, 2))).toBeLessThan(0);
    expect(compareQuantities(q(2, 4), q(1, 2))).toBe(0);
  });

  it('orders a list the way a sort needs', () => {
    const sorted = [q(3, 2), q(1, 4), q(1, 1)].sort(compareQuantities).map(formatQuantity);

    expect(sorted).toEqual(['¼', '1', '1½']);
  });
});

describe('isPositive', () => {
  it('treats absent and zero as not positive', () => {
    expect(isPositive(null)).toBe(false);
    expect(isPositive(undefined)).toBe(false);
    expect(isPositive(q(0, 1))).toBe(false);
    expect(isPositive(q(-1, 2))).toBe(false);
    expect(isPositive(q(1, 2))).toBe(true);
  });
});
