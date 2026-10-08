import { describe, expect, it } from 'vitest';
import { entryKey, latenessLabel, outcomeKey, packageLabel, signedQuantity } from './history';
import { dictionaries, type MessageKey } from './i18n';

const t = (key: MessageKey) => dictionaries.tr[key];

describe('stock movements in words', () => {
  it('knows every entry type the server writes', () => {
    const expected: Record<string, string> = {
      Acquire: 'Stok eklendi',
      Consume: 'Kullanıldı',
      CorrectionReversal: 'Düzeltme iadesi',
      CorrectionConsume: 'Düzeltme ile düşüldü',
      Found: 'Bulundu',
      Loss: 'Kayıp',
      Dispose: 'Atıldı',
      CountAdjustment: 'Sayım düzeltmesi',
      PackageTransfer: 'Kutu aktarımı',
      ManualAdjustment: 'Elle düzeltme',
    };

    for (const [type, words] of Object.entries(expected)) {
      const key = entryKey(type);
      expect(key, type).not.toBeNull();
      expect(t(key!)).toBe(words);
    }
  });

  it('leaves an unknown type to be shown raw rather than guessed', () => {
    expect(entryKey('SomethingNew')).toBeNull();
  });

  it('signs a movement', () => {
    expect(signedQuantity({ numerator: 20, denominator: 1 })).toBe('+20');
    expect(signedQuantity({ numerator: -1, denominator: 2 })).toBe('-½');
    expect(signedQuantity({ numerator: 0, denominator: 1 })).toBe('0');
  });

  it('names the box or the loose stock', () => {
    expect(packageLabel(3, t)).toBe('Kutu 3');
    expect(packageLabel(null, t)).toBe('Kutusuz stok');
  });
});

describe('recorded doses in words', () => {
  it('knows every outcome', () => {
    expect(t(outcomeKey('Taken')!)).toBe('Alındı');
    expect(t(outcomeKey('Skipped')!)).toBe('Atlandı');
    expect(t(outcomeKey('PartialDose')!)).toBe('Eksik doz');
    expect(t(outcomeKey('ExtraDose')!)).toBe('Ek doz');
    expect(outcomeKey('Other')).toBeNull();
  });

  it('mentions lateness only from a quarter of an hour, either way', () => {
    expect(latenessLabel(null, t)).toBeNull();
    expect(latenessLabel(14, t)).toBeNull();
    expect(latenessLabel(-14, t)).toBeNull();
    expect(latenessLabel(15, t)).toBe('15 dk geç');
    expect(latenessLabel(-30, t)).toBe('30 dk erken');
  });
});
