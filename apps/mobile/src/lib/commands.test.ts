import { describe, expect, it } from 'vitest';
import { alreadyDone, buildAddStock, buildCountLines, describeCommand, describeTarget, interleave, isValidDay } from './commands';
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

  it('names the box and plan decisions that came later', () => {
    expect(describeCommand('plan.end', { endsOn: '2026-10-12' }, t)).toBe('Planı sonlandır · 2026-10-12');
    expect(describeCommand('plan.restart', { startsOn: '2026-11-01' }, t)).toBe('Yeniden başlat · 2026-11-01');
    expect(describeCommand('package.unpin', {}, t)).toBe('Etkin kutudan çıkar');
    expect(describeCommand('package.reinstate', {}, t)).toBe('Kutuyu geri getir');
    expect(describeCommand('package.update', { label: 'x' }, t)).toBe('Kutuyu düzenle');
    expect(describeCommand('package.assign', { personId: 'p1' }, t)).toBe('Kişiye ata');
    expect(describeCommand('package.assign', { personId: null }, t)).toBe('Kimseye ait değil');
    expect(describeCommand('package.lend', { borrowerPersonId: 'p2' }, t)).toBe('Ödünç ver');
    expect(describeCommand('loan.return', { packageId: 'b1' }, t)).toBe('Geri alındı');
  });

  it('says how many rows a count carries', () => {
    expect(describeCommand('inventory.count', { lines: [{}, {}, {}] }, t)).toBe('Sayım: 3 satır');
    expect(describeCommand('inventory.count', {}, t)).toBe('Sayım: 0 satır');
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

describe('buildCountLines', () => {
  it('turns the rows that were filled in into exact lines, a box when the key names one', () => {
    expect(buildCountLines({ m1: '12', 'm2:b7': '7,5', m3: '' })).toEqual({
      ok: true,
      lines: [
        { medicationDefinitionId: 'm1', observedNumerator: 12, observedDenominator: 1 },
        { medicationDefinitionId: 'm2', observedNumerator: 15, observedDenominator: 2, packageId: 'b7' },
      ],
    });
  });

  it('accepts a count of nothing left, but not a negative one', () => {
    expect(buildCountLines({ m1: '0' })).toEqual({
      ok: true,
      lines: [{ medicationDefinitionId: 'm1', observedNumerator: 0, observedDenominator: 1 }],
    });
    expect(buildCountLines({ m1: '-1' })).toEqual({ ok: false, error: 'countingInvalidAmount' });
  });

  it('refuses the whole count when one row cannot be read, and an empty count', () => {
    expect(buildCountLines({ m1: '12', m2: 'on iki' })).toEqual({ ok: false, error: 'countingInvalidAmount' });
    expect(buildCountLines({ m1: '', m2: '  ' })).toEqual({ ok: false, error: 'countingNothingEntered' });
    expect(buildCountLines({})).toEqual({ ok: false, error: 'countingNothingEntered' });
  });
});

describe('isValidDay', () => {
  it('accepts a real calendar day and nothing else', () => {
    expect(isValidDay('2026-10-09')).toBe(true);
    expect(isValidDay(' 2026-02-28 ')).toBe(true);
    expect(isValidDay('2028-02-29')).toBe(true);
    expect(isValidDay('2026-02-29')).toBe(false);
    expect(isValidDay('2026-13-01')).toBe(false);
    expect(isValidDay('2026-10-9')).toBe(false);
    expect(isValidDay('09.10.2026')).toBe(false);
    expect(isValidDay('')).toBe(false);
  });
});

describe('alreadyDone', () => {
  it('reads the two refusals that mean the goal is already reached, and nothing else', () => {
    expect(alreadyDone('package.reinstate', 'package_not_retired')).toBe(true);
    expect(alreadyDone('loan.return', 'already_returned')).toBe(true);
    expect(alreadyDone('package.pin', 'package_not_available')).toBe(false);
    expect(alreadyDone('package.lend', 'already_on_loan')).toBe(false);
    expect(alreadyDone('loan.return', 'not_found')).toBe(false);
  });
});
