import { useEffect, useState } from 'react';
import { Modal, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import { enqueueDose } from '../data/outbox';
import { readPackageOptions, type LocalDueDose, type PackageOption } from '../data/snapshot';
import type { DoseSource } from '../lib/api';
import { formatQuantity, parseQuantity } from '../lib/quantity';
import { Badge, Button, Card, Notice, palette, useTranslate } from '../ui/theme';

/**
 * The advanced path, behind "details".
 *
 * Everything the one-tap flow deliberately hides lives here: a different amount, a
 * partial or extra dose, a specific package, loose stock, or a dose taken from stock
 * this household does not track at all.
 */
export function DoseDetailsSheet({
  dose,
  onClose,
  onRecorded,
}: {
  dose: LocalDueDose;
  onClose: () => void;
  onRecorded: () => void;
}) {
  const { t } = useTranslate();
  const db = useSQLiteContext();

  const [source, setSource] = useState<DoseSource>('Automatic');
  const [packageId, setPackageId] = useState<string | null>(null);
  const [amount, setAmount] = useState(formatQuantity(dose.dose));
  const [outcome, setOutcome] = useState<'Taken' | 'PartialDose' | 'ExtraDose'>('Taken');
  const [note, setNote] = useState('');
  const [packages, setPackages] = useState<PackageOption[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    readPackageOptions(db, dose.medicationId)
      .then(setPackages)
      .catch(() => setPackages([]));
  }, [db, dose.medicationId]);

  async function submit() {
    const quantity = parseQuantity(amount);

    if (!quantity || quantity.numerator <= 0) {
      setError(t('errorGeneric'));
      return;
    }

    if (source === 'SpecificPackage' && !packageId) {
      setError(t('errorPackageNotFound'));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const now = new Date().toISOString();

      await enqueueDose(
        db,
        {
          // An extra dose belongs to no scheduled slot, so it is recorded against the
          // person and medication instead of claiming the plan's slot.
          planVersionId: outcome === 'ExtraDose' ? null : dose.planVersionId,
          personId: dose.personId,
          medicationDefinitionId: dose.medicationId,
          outcome,
          source,
          packageId: source === 'SpecificPackage' ? packageId : null,
          quantity,
          scheduledFor: outcome === 'ExtraDose' ? null : dose.scheduledFor,
          occurredAt: now,
          note: note.trim() === '' ? null : note.trim(),
        },
        now,
      );

      onRecorded();
    } catch {
      setError(t('errorGeneric'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal visible animationType="slide" onRequestClose={onClose} transparent={false}>
      <ScrollView contentContainerStyle={styles.container} keyboardShouldPersistTaps="handled">
        <View style={styles.header}>
          <Text accessibilityRole="header" style={styles.title}>
            {dose.medicationName}
          </Text>
          <Button tone="quiet" label={t('close')} onPress={onClose} />
        </View>

        {error ? <Notice tone="danger" message={error} /> : null}

        <Card>
          <Text style={styles.label} nativeID="amount-label">
            {t('amountTaken')}
          </Text>
          <TextInput
            accessibilityLabelledBy="amount-label"
            accessibilityLabel={t('amountTaken')}
            value={amount}
            onChangeText={setAmount}
            style={styles.input}
          />
          <Text style={styles.hint}>{t('amountTakenHint')}</Text>
        </Card>

        <Card>
          <Text style={styles.label}>{t('outcomeLabel')}</Text>
          <Choice
            options={[
              { value: 'Taken', label: t('taken') },
              { value: 'PartialDose', label: t('partialDose') },
              { value: 'ExtraDose', label: t('extraDose') },
            ]}
            selected={outcome}
            onSelect={(value) => setOutcome(value as typeof outcome)}
          />
        </Card>

        <Card>
          <Text style={styles.label}>{t('whichPackage')}</Text>
          <Choice
            options={[
              { value: 'Automatic', label: t('sourceAutomatic') },
              { value: 'SpecificPackage', label: t('sourceSpecific') },
              { value: 'LooseStock', label: t('sourceLoose') },
              { value: 'UntrackedExternal', label: t('sourceUntracked') },
            ]}
            selected={source}
            onSelect={(value) => setSource(value as DoseSource)}
          />

          {source === 'Automatic' ? <Text style={styles.hint}>{t('sourceAutomaticHint')}</Text> : null}
          {source === 'UntrackedExternal' ? <Notice message={t('sourceUntrackedHint')} /> : null}

          {source === 'SpecificPackage' ? (
            <View style={styles.packageList}>
              {packages.map((option) => (
                <Pressable
                  key={option.id}
                  accessibilityRole="radio"
                  accessibilityState={{ selected: packageId === option.id }}
                  accessibilityLabel={`${t('packageOrdinal')} ${option.ordinal}`}
                  onPress={() => setPackageId(option.id)}
                  style={[styles.packageRow, packageId === option.id ? styles.packageSelected : null]}
                >
                  <Text style={styles.packageTitle}>
                    {t('packageOrdinal')} {option.ordinal}
                  </Text>
                  <Text style={styles.hint}>
                    {formatQuantity(option.remaining)} / {formatQuantity(option.capacity)}
                  </Text>
                  {option.isPinned ? <Badge tone="positive" label={t('sourceSpecific')} /> : null}
                </Pressable>
              ))}
              {packages.length === 0 ? <Text style={styles.hint}>{t('none')}</Text> : null}
            </View>
          ) : null}
        </Card>

        <Card>
          <Text style={styles.label} nativeID="note-label">
            {t('note')}
          </Text>
          <TextInput
            accessibilityLabelledBy="note-label"
            accessibilityLabel={t('note')}
            value={note}
            onChangeText={setNote}
            style={styles.input}
          />
        </Card>

        <Button label={t('recordDose')} disabled={busy} onPress={() => void submit()} />
      </ScrollView>
    </Modal>
  );
}

/** A radio group with real touch targets and announced selection state. */
function Choice({
  options,
  selected,
  onSelect,
}: {
  options: { value: string; label: string }[];
  selected: string;
  onSelect: (value: string) => void;
}) {
  return (
    <View accessibilityRole="radiogroup" style={styles.choiceList}>
      {options.map((option) => (
        <Pressable
          key={option.value}
          accessibilityRole="radio"
          accessibilityState={{ selected: selected === option.value }}
          accessibilityLabel={option.label}
          onPress={() => onSelect(option.value)}
          style={[styles.choice, selected === option.value ? styles.choiceSelected : null]}
        >
          <Text style={[styles.choiceText, selected === option.value ? styles.choiceTextSelected : null]}>
            {option.label}
          </Text>
        </Pressable>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { padding: 16, gap: 12, backgroundColor: palette.surface, paddingBottom: 48 },
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  title: { fontSize: 20, fontWeight: '800', color: palette.ink, flex: 1 },
  label: { fontSize: 14, fontWeight: '700', color: palette.ink },
  hint: { color: palette.inkMuted, fontSize: 13, lineHeight: 19 },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderColor: palette.line,
    borderRadius: 12,
    paddingHorizontal: 12,
    backgroundColor: palette.raised,
    color: palette.ink,
    fontSize: 16,
  },
  choiceList: { gap: 8 },
  choice: {
    minHeight: 48,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.line,
    paddingHorizontal: 14,
    justifyContent: 'center',
    backgroundColor: palette.raised,
  },
  choiceSelected: { borderColor: palette.ink, backgroundColor: palette.sunken },
  choiceText: { fontSize: 16, color: palette.ink },
  choiceTextSelected: { fontWeight: '800' },
  packageList: { gap: 8 },
  packageRow: {
    minHeight: 48,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.line,
    padding: 12,
    gap: 4,
    backgroundColor: palette.raised,
  },
  packageSelected: { borderColor: palette.ink, backgroundColor: palette.sunken },
  packageTitle: { fontSize: 16, fontWeight: '700', color: palette.ink },
});
