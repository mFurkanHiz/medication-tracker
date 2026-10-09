import { describe, expect, it } from 'vitest';
import {
  alreadyDone,
  buildAddStock,
  buildCountLines,
  buildMedication,
  buildPerson,
  buildPlan,
  carriesKeyInBody,
  defaultUnitFor,
  describeCommand,
  describeTarget,
  interleave,
  isValidDay,
  isValidTime,
  type PlanForm,
} from './commands';
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

describe('the creates', () => {
  it('names a queued create by what it creates, so a refused one still says who or what', () => {
    expect(describeCommand('person.create', { id: 'p9', name: 'Zeynep' }, t)).toBe('Kişi ekle: Zeynep');
    expect(describeCommand('medication.create', { id: 'm9', name: 'Şurup', form: 'OralLiquid' }, t)).toBe('İlaç tanımla: Şurup');
    expect(
      describeCommand('plan.create', { kind: 'Scheduled', pattern: 'SelectedWeekdays', weekdayMask: 5, localTime: '08:30:00' }, t),
    ).toBe('Plan ekle · Pzt, Çar · 08:30');
    expect(describeCommand('plan.create', { kind: 'AsNeeded', pattern: 'Daily', dayPeriod: 'Evening' }, t)).toBe(
      'Plan ekle · Gerektiğinde · tercihen Akşam',
    );
  });

  it('carries the key in the body only for the kinds the server cannot tell apart alone', () => {
    for (const kind of ['stock.add', 'inventory.count', 'person.create', 'medication.create', 'plan.create']) {
      expect(carriesKeyInBody(kind)).toBe(true);
    }
    for (const kind of ['plan.pause', 'plan.end', 'package.pin', 'package.update', 'loan.return']) {
      expect(carriesKeyInBody(kind)).toBe(false);
    }
  });

  it("derives the unit from the form by the server's rule", () => {
    expect(defaultUnitFor('Tablet')).toBe('Tablet');
    expect(defaultUnitFor('Capsule')).toBe('Capsule');
    expect(defaultUnitFor('OralLiquid')).toBe('Milliliter');
    expect(defaultUnitFor('EyeDrops')).toBe('Drop');
    expect(defaultUnitFor('Injection')).toBe('Ampoule');
    expect(defaultUnitFor('Gel')).toBe('Gram');
    expect(defaultUnitFor('NasalSpray')).toBe('Puff');
    expect(defaultUnitFor('Patch')).toBe('Patch');
    expect(defaultUnitFor('Other')).toBe('Dose');
    expect(defaultUnitFor('Nonsense')).toBe('Dose');
  });
});

describe('buildPerson', () => {
  it('trims the name and refuses a blank or over-long one', () => {
    expect(buildPerson('  Ayşe ')).toEqual({ ok: true, body: { name: 'Ayşe' } });
    expect(buildPerson('   ')).toEqual({ ok: false, error: 'personNameRequired' });
    expect(buildPerson('x'.repeat(161))).toEqual({ ok: false, error: 'personNameRequired' });
  });
});

describe('buildMedication', () => {
  it('builds the server request with the default box size exact', () => {
    expect(
      buildMedication({ name: ' Parol ', form: 'Tablet', strength: ' 500 mg ', capacity: '20', coverage: 'Unspecified' }),
    ).toEqual({
      ok: true,
      body: {
        name: 'Parol',
        form: 'Tablet',
        strength: '500 mg',
        defaultPackageCapacityNumerator: 20,
        defaultPackageCapacityDenominator: 1,
        coverage: 'Unspecified',
      },
    });
    expect(
      buildMedication({ name: 'Şurup', form: 'OralLiquid', strength: '', capacity: '2,5', coverage: 'SelfPaid' }),
    ).toMatchObject({ ok: true, body: { strength: null, defaultPackageCapacityNumerator: 5, defaultPackageCapacityDenominator: 2 } });
  });

  it('leaves blanks out and refuses what the server would', () => {
    expect(buildMedication({ name: 'Şurup', form: 'OralLiquid', strength: '', capacity: '', coverage: 'SelfPaid' })).toEqual({
      ok: true,
      body: {
        name: 'Şurup',
        form: 'OralLiquid',
        strength: null,
        defaultPackageCapacityNumerator: null,
        defaultPackageCapacityDenominator: null,
        coverage: 'SelfPaid',
      },
    });
    const blank = { form: 'Tablet', strength: '', capacity: '', coverage: 'Unspecified' };
    expect(buildMedication({ ...blank, name: ' ' })).toEqual({ ok: false, error: 'medicationNameRequired' });
    expect(buildMedication({ ...blank, name: 'x'.repeat(201) })).toEqual({ ok: false, error: 'medicationNameRequired' });
    expect(buildMedication({ ...blank, name: 'x', capacity: '0' })).toEqual({ ok: false, error: 'invalidAmount' });
    expect(buildMedication({ ...blank, name: 'x', capacity: 'abc' })).toEqual({ ok: false, error: 'invalidAmount' });
    expect(buildMedication({ ...blank, name: 'x', form: 'Pill' })).toEqual({ ok: false, error: 'errorGeneric' });
    expect(buildMedication({ ...blank, name: 'x', coverage: 'Free' })).toEqual({ ok: false, error: 'errorGeneric' });
  });
});

describe('buildPlan', () => {
  const base: PlanForm = {
    personId: 'p1',
    medicationId: 'm1',
    dose: '1/2',
    schedule: 'Daily',
    weekdayMask: 0,
    intervalDays: '2',
    dayOfMonth: '1',
    intervalMonths: '1',
    localTime: '08:30',
    dayPeriod: '',
    mealRelation: '',
    effectiveFrom: '2026-10-09',
    effectiveTo: '',
  };

  it('builds a daily schedule at a clock time, the fraction exact', () => {
    expect(buildPlan(base, 'Europe/Istanbul')).toEqual({
      ok: true,
      body: {
        personId: 'p1',
        medicationDefinitionId: 'm1',
        doseNumerator: 1,
        doseDenominator: 2,
        timeZoneId: 'Europe/Istanbul',
        kind: 'Scheduled',
        pattern: 'Daily',
        weekdayMask: null,
        intervalDays: null,
        dayOfMonth: null,
        intervalMonths: null,
        effectiveFrom: '2026-10-09',
        effectiveTo: null,
        localTime: '08:30:00',
        dayPeriod: null,
        mealRelation: null,
      },
    });
  });

  it('gives each pattern exactly its own fields, and a day period instead of a clock', () => {
    expect(
      buildPlan({ ...base, schedule: 'SelectedWeekdays', weekdayMask: 5, dayPeriod: 'Morning', mealRelation: 'AfterFood' }, 'UTC'),
    ).toMatchObject({
      ok: true,
      body: { pattern: 'SelectedWeekdays', weekdayMask: 5, intervalDays: null, localTime: null, dayPeriod: 'Morning', mealRelation: 'AfterFood' },
    });
    expect(buildPlan({ ...base, schedule: 'EveryNDays', intervalDays: '3' }, 'UTC')).toMatchObject({
      ok: true,
      body: { pattern: 'EveryNDays', intervalDays: 3, weekdayMask: null, localTime: '08:30:00' },
    });
    expect(buildPlan({ ...base, schedule: 'DayOfMonth', dayOfMonth: '31' }, 'UTC')).toMatchObject({
      ok: true,
      body: { pattern: 'DayOfMonth', dayOfMonth: 31, intervalMonths: null },
    });
    expect(buildPlan({ ...base, schedule: 'EveryNMonths', intervalMonths: '2', effectiveTo: '2027-10-09' }, 'UTC')).toMatchObject({
      ok: true,
      body: { pattern: 'EveryNMonths', intervalMonths: 2, dayOfMonth: null, effectiveTo: '2027-10-09' },
    });
  });

  it('sends an as-needed plan as daily with no clock, keeping the preference', () => {
    expect(buildPlan({ ...base, schedule: 'AsNeeded', weekdayMask: 5, dayPeriod: 'Evening' }, 'UTC')).toMatchObject({
      ok: true,
      body: { kind: 'AsNeeded', pattern: 'Daily', weekdayMask: null, intervalDays: null, localTime: null, dayPeriod: 'Evening' },
    });
  });

  it('refuses what the server would, naming the field', () => {
    expect(buildPlan({ ...base, personId: '' }, 'UTC')).toEqual({ ok: false, error: 'planNeedsPersonAndMedication' });
    expect(buildPlan({ ...base, dose: '0' }, 'UTC')).toEqual({ ok: false, error: 'invalidAmount' });
    expect(buildPlan({ ...base, dose: 'bir' }, 'UTC')).toEqual({ ok: false, error: 'invalidAmount' });
    expect(buildPlan({ ...base, schedule: 'SelectedWeekdays', weekdayMask: 0 }, 'UTC')).toEqual({ ok: false, error: 'weekdaysRequired' });
    expect(buildPlan({ ...base, schedule: 'EveryNDays', intervalDays: '0' }, 'UTC')).toEqual({ ok: false, error: 'intervalDaysInvalid' });
    expect(buildPlan({ ...base, schedule: 'EveryNDays', intervalDays: '3651' }, 'UTC')).toEqual({ ok: false, error: 'intervalDaysInvalid' });
    expect(buildPlan({ ...base, schedule: 'EveryNDays', effectiveFrom: '' }, 'UTC')).toEqual({ ok: false, error: 'startRequired' });
    expect(buildPlan({ ...base, schedule: 'DayOfMonth', dayOfMonth: '32' }, 'UTC')).toEqual({ ok: false, error: 'dayOfMonthInvalid' });
    expect(buildPlan({ ...base, schedule: 'EveryNMonths', intervalMonths: '121' }, 'UTC')).toEqual({ ok: false, error: 'intervalMonthsInvalid' });
    expect(buildPlan({ ...base, schedule: 'EveryNMonths', effectiveFrom: ' ' }, 'UTC')).toEqual({ ok: false, error: 'startRequired' });
    expect(buildPlan({ ...base, localTime: '8:30' }, 'UTC')).toEqual({ ok: false, error: 'timeInvalid' });
    expect(buildPlan({ ...base, localTime: '24:00' }, 'UTC')).toEqual({ ok: false, error: 'timeInvalid' });
    expect(buildPlan({ ...base, effectiveTo: '2026-02-30' }, 'UTC')).toEqual({ ok: false, error: 'invalidDate' });
    expect(buildPlan({ ...base, effectiveTo: '2026-10-01' }, 'UTC')).toEqual({ ok: false, error: 'endBeforeStart' });
    expect(buildPlan({ ...base, dayPeriod: 'Dawn' }, 'UTC')).toEqual({ ok: false, error: 'errorGeneric' });
  });
});

describe('isValidTime', () => {
  it('accepts a 24-hour HH:MM and nothing else', () => {
    expect(isValidTime('08:30')).toBe(true);
    expect(isValidTime('23:59')).toBe(true);
    expect(isValidTime(' 00:00 ')).toBe(true);
    expect(isValidTime('8:30')).toBe(false);
    expect(isValidTime('24:00')).toBe(false);
    expect(isValidTime('08:60')).toBe(false);
    expect(isValidTime('08:30:00')).toBe(false);
  });
});
