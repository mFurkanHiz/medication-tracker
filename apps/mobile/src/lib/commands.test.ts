import { describe, expect, it } from 'vitest';
import { buildAddStock, describeCommand, describeTarget, interleave } from './commands';
import { dictionaries, type MessageKey } from './i18n';

const t = (key: MessageKey) => dictionaries.tr[key];

describe('describeCommand', () => {
  it('names each command in the web\'s words', () => {
    expect(describeCommand('plan.pause', { isPaused: true }, t)).toBe('Şimdilik ara ver');
    expect(describeCommand('plan.pause', { isPaused: false }, t)).toBe('Yeniden başla');
    expect(describeCommand('package.pin', {}, t)).toBe('Etkin kutu yap');
    expect(describeCommand('package.retire', { state: 'Lost' }, t)).toBe('Kayıp olarak işaretle');
    expect(describeCommand('package.retire', { state: 'Disposed' }, t)).toBe('Atıldı olarak işaretle');
  });

  it('spells out what stock is being added', () => {
    expect(
      describeCommand(
        'stock.add',
        { fullPackages: 2, openedPackages: [{ remainingNumerator: 8, remainingDenominator: 1 }], looseNumerator: 1, looseDenominator: 2 },
        t,
      ),
    ).toBe('Stok ekle: 2 kapalı kutu + açık kutu (8) + ½ kutusuz miktar');
    expect(describeCommand('stock.add', { fullPackages: 1, openedPackages: [] }, t)).toBe('Stok ekle: 1 kapalı kutu');
  });

  it('shows an unknown kind by its name rather than crashing', () => {
    expect(describeCommand('something.new', null, t)).toBe('something.new');
  });
});

describe('buildAddStock', () => {
  const twenty = { numerator: 20, denominator: 1 };

  it('builds sealed boxes, an opened box and loose stock exactly', () => {
    const outcome = buildAddStock(
      { fullPackages: '2', openedRemaining: '7,5', capacity: '', loose: '1/2' },
      twenty,
    );

    expect(outcome).toEqual({
      ok: true,
      body: {
        fullPackages: 2,
        openedPackages: [{ remainingNumerator: 15, remainingDenominator: 2 }],
        looseNumerator: 1,
        looseDenominator: 2,
      },
    });
  });

  it('sends a typed capacity and leaves it out when the catalogue default is used', () => {
    const typed = buildAddStock({ fullPackages: '1', openedRemaining: '', capacity: '28', loose: '' }, null);
    expect(typed.ok && typed.body.capacityNumerator).toBe(28);

    const defaulted = buildAddStock({ fullPackages: '1', openedRemaining: '', capacity: '', loose: '' }, twenty);
    expect(defaulted.ok && 'capacityNumerator' in defaulted.body).toBe(false);
  });

  it('refuses an empty form', () => {
    expect(buildAddStock({ fullPackages: '', openedRemaining: '', capacity: '', loose: '' }, twenty)).toEqual({
      ok: false,
      error: 'stockAddInvalid',
    });
    expect(buildAddStock({ fullPackages: '0', openedRemaining: '', capacity: '20', loose: '' }, twenty)).toEqual({
      ok: false,
      error: 'stockAddInvalid',
    });
  });

  it('requires a capacity for boxes when the catalogue has none, but not for loose stock', () => {
    expect(buildAddStock({ fullPackages: '1', openedRemaining: '', capacity: '', loose: '' }, null)).toEqual({
      ok: false,
      error: 'capacityRequired',
    });
    expect(buildAddStock({ fullPackages: '', openedRemaining: '', capacity: '', loose: '3' }, null).ok).toBe(true);
  });

  it('refuses an opened box holding more than its capacity', () => {
    expect(buildAddStock({ fullPackages: '', openedRemaining: '21', capacity: '', loose: '' }, twenty)).toEqual({
      ok: false,
      error: 'remainingExceedsCapacity',
    });
    expect(buildAddStock({ fullPackages: '', openedRemaining: '20', capacity: '', loose: '' }, twenty).ok).toBe(true);
  });

  it('refuses what is not a number', () => {
    expect(buildAddStock({ fullPackages: 'iki', openedRemaining: '', capacity: '', loose: '' }, twenty).ok).toBe(false);
    expect(buildAddStock({ fullPackages: '1.5', openedRemaining: '', capacity: '', loose: '' }, twenty).ok).toBe(false);
    expect(buildAddStock({ fullPackages: '', openedRemaining: 'x', capacity: '', loose: '' }, twenty).ok).toBe(false);
    expect(buildAddStock({ fullPackages: '', openedRemaining: '', capacity: '', loose: '0' }, twenty).ok).toBe(false);
  });
});

describe('interleave', () => {
  it('sends doses and commands in creation order, a dose first on a tie', () => {
    const merged = interleave(
      [
        { createdAt: '2026-10-08T10:00:00Z', item: 'dose-1' },
        { createdAt: '2026-10-08T10:05:00Z', item: 'dose-2' },
      ],
      [
        { createdAt: '2026-10-08T09:59:00Z', item: 'pause' },
        { createdAt: '2026-10-08T10:05:00Z', item: 'pin' },
        { createdAt: '2026-10-08T10:06:00Z', item: 'stock' },
      ],
    );

    expect(merged.map((entry) => entry.item)).toEqual(['pause', 'dose-1', 'dose-2', 'pin', 'stock']);
    expect(merged.map((entry) => entry.kind)).toEqual(['command', 'dose', 'dose', 'command', 'command']);
  });

  it('orders by the instant, so a time with milliseconds is not sorted as text', () => {
    const merged = interleave(
      [{ createdAt: '2026-10-08T10:00:02.500Z', item: 'dose' }],
      [{ createdAt: '2026-10-08T10:00:02Z', item: 'command' }],
    );

    expect(merged.map((entry) => entry.item)).toEqual(['command', 'dose']);
  });

  it('copes with either queue being empty', () => {
    expect(interleave([], [{ createdAt: 'a', item: 1 }]).map((e) => e.item)).toEqual([1]);
    expect(interleave([{ createdAt: 'a', item: 1 }], []).map((e) => e.item)).toEqual([1]);
    expect(interleave([], [])).toEqual([]);
  });
});

describe('describeTarget', () => {
  it('names the plan, the box or the medicine', () => {
    expect(describeTarget({ medicationName: 'Parol', personName: 'Ayşe', packageLabel: null, packageOrdinal: null }, t)).toBe('Parol · Ayşe');
    expect(describeTarget({ medicationName: 'Parol', personName: null, packageLabel: null, packageOrdinal: 3 }, t)).toBe('Parol · Kutu 3');
    expect(describeTarget({ medicationName: 'Parol', personName: null, packageLabel: 'Yatak odası', packageOrdinal: 3 }, t)).toBe('Parol · Yatak odası');
    expect(describeTarget({ medicationName: 'Parol', personName: null, packageLabel: null, packageOrdinal: null }, t)).toBe('Parol');
    expect(describeTarget({ medicationName: null, personName: null, packageLabel: null, packageOrdinal: null }, t)).toBe('');
  });
});
