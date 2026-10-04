import { describe, expect, it } from 'vitest';
import { cautionList, en, enumKey, tr, type MessageKey } from '@/lib/i18n';

/**
 * The translation dictionaries, and the two helpers that read from them.
 *
 * `en` is typed `Record<MessageKey, string>`, so a Turkish key with no English
 * counterpart already fails the build. What the type cannot see is a key whose value is
 * empty, or one left as a copy of the other language — both of which compile and both of
 * which reach a screen.
 *
 * This file is imported through `@/lib/i18n` on purpose: it is the only check that the
 * alias resolves in a test the same way it resolves in the application.
 */

describe('the dictionaries', () => {
  it('cover exactly the same keys in both languages', () => {
    expect(Object.keys(en).sort()).toEqual(Object.keys(tr).sort());
  });

  it('have no key that resolves to nothing', () => {
    // An empty string renders as a gap with no hint that anything is missing, which is
    // worse than an untranslated word: there is nothing to notice.
    for (const [dictionary, name] of [[tr, 'tr'], [en, 'en']] as const) {
      const blank = Object.entries(dictionary)
        .filter(([, value]) => value.trim() === '')
        .map(([key]) => key);

      expect(blank, `${name} has blank values`).toEqual([]);
    }
  });

  it('translate the product name itself rather than sharing one string', () => {
    // Both dictionaries import these from document-metadata.ts, which is also what the
    // document title reads. If this ever starts failing, the title and the heading have
    // been allowed to drift apart.
    expect(tr.appName).toBe('İlaç Takip');
    expect(en.appName).toBe('Medication Tracker');
  });
});

describe('cautionList', () => {
  const t = (key: MessageKey) => tr[key];

  it('returns nothing when the household wrote nothing', () => {
    expect(cautionList(null, t)).toEqual([]);
    expect(cautionList(undefined, t)).toEqual([]);
    expect(cautionList({
      doNotTakeWith: null,
      foodsToAvoid: null,
      thingsToDo: null,
      thingsToAvoid: null,
      warning: null,
    }, t)).toEqual([]);
  });

  it('drops a note that is only whitespace', () => {
    // Otherwise an accidental space renders an empty labelled row, which reads as a
    // caution the household did not write.
    expect(cautionList({
      doNotTakeWith: '   ',
      foodsToAvoid: null,
      thingsToDo: null,
      thingsToAvoid: null,
      warning: null,
    }, t)).toEqual([]);
  });

  it('keeps the order the form asks in, not the order the API happened to send', () => {
    const notes = cautionList({
      warning: 'Uyarı',
      thingsToAvoid: 'Yapmayın',
      thingsToDo: 'Yapın',
      foodsToAvoid: 'Kaçının',
      doNotTakeWith: 'Birlikte almayın',
    }, t);

    expect(notes.map((note) => note.value)).toEqual([
      'Birlikte almayın', 'Kaçının', 'Yapın', 'Yapmayın', 'Uyarı',
    ]);
  });

  it('trims the value but keeps the household\'s own line breaks', () => {
    const notes = cautionList({
      doNotTakeWith: '  greyfurt\nkan sulandırıcı  ',
      foodsToAvoid: null,
      thingsToDo: null,
      thingsToAvoid: null,
      warning: null,
    }, t);

    // Two lines written on two lines are a list. Collapsing them would merge two separate
    // cautions into one sentence.
    expect(notes[0].value).toBe('greyfurt\nkan sulandırıcı');
  });
});

describe('enumKey', () => {
  /**
   * Every enum name the API sends on a screen the household reads. Listed here rather
   * than derived from the map, so deleting an entry from the map is a failure instead of
   * a quietly smaller test.
   */
  const SENT_BY_THE_API = [
    'Morning', 'Noon', 'Afternoon', 'Evening', 'Night', 'Bedtime',
    'Fasting', 'FullStomach', 'BeforeFood', 'WithFood', 'AfterFood',
    'Sealed', 'Opened', 'Disposed', 'Lost', 'Archived',
    'Acquire', 'Consume', 'CorrectionReversal', 'CorrectionConsume',
    'Found', 'Loss', 'Dispose', 'CountAdjustment', 'PackageTransfer', 'ManualAdjustment',
  ];

  it('maps every enum the API sends', () => {
    const unmapped = SENT_BY_THE_API.filter((value) => enumKey(value) === null);

    expect(unmapped).toEqual([]);
  });

  it('maps each of them to a key that actually resolves in both languages', () => {
    // The type guarantees the key exists. It does not guarantee the key was not renamed
    // in the dictionaries while the map kept the old name — which would reach a screen as
    // `undefined`.
    for (const value of SENT_BY_THE_API) {
      const key = enumKey(value)!;

      expect(tr[key], `tr.${key} for ${value}`).toBeTruthy();
      expect(en[key], `en.${key} for ${value}`).toBeTruthy();
    }
  });

  it('returns null for a name it does not know rather than a broken key', () => {
    // When the server adds an enum member, the screen must fall back rather than call
    // `t()` with a key that resolves to nothing.
    expect(enumKey(null)).toBeNull();
    expect(enumKey(undefined)).toBeNull();
    expect(enumKey('SomethingTheServerAddedLater')).toBeNull();
    expect(enumKey('')).toBeNull();
  });
});
