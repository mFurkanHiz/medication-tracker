import { useState } from 'react';
import { Modal, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import { enqueueCommand } from '../data/outbox';
import type { StockPackage } from '../data/snapshot';
import { isValidDay } from '../lib/commands';
import { Button, Card, Notice, palette, useTranslate } from '../ui/theme';

/**
 * Everything the household may change about a box, as on the web's edit dialog, reduced
 * to what a phone can usefully ask: the box's own name, when it expires, whose coverage
 * it falls under, its lot number, where it is kept, a note.
 *
 * The server replaces every detail at once, so the details this sheet does not show
 * (acquired on, barcode, source) are sent back exactly as cached, never blanked.
 */
export function EditPackageSheet({
  box,
  medicationId,
  onClose,
  onQueued,
}: {
  box: StockPackage;
  medicationId: string;
  onClose: () => void;
  onQueued: () => void;
}) {
  const { t } = useTranslate();
  const db = useSQLiteContext();

  const [label, setLabel] = useState(box.label ?? '');
  const [expiresOn, setExpiresOn] = useState(box.expiresOn ?? '');
  const [coverage, setCoverage] = useState<string | null>(box.coverage);
  const [lotNumber, setLotNumber] = useState(box.lotNumber ?? '');
  const [storageLocation, setStorageLocation] = useState(box.storageLocation ?? '');
  const [note, setNote] = useState(box.note ?? '');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const clean = (value: string) => (value.trim() === '' ? null : value.trim());

  async function save() {
    if (expiresOn.trim() !== '' && !isValidDay(expiresOn)) {
      setError(t('invalidDate'));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await enqueueCommand(
        db,
        {
          kind: 'package.update',
          targetId: box.id,
          medicationId,
          body: {
            label: clean(label),
            coverage,
            expiresOn: clean(expiresOn),
            acquiredOn: box.acquiredOn,
            lotNumber: clean(lotNumber),
            barcode: box.barcode,
            source: box.source,
            storageLocation: clean(storageLocation),
            note: clean(note),
          },
        },
        new Date().toISOString(),
      );
      onQueued();
    } catch {
      setError(t('errorGeneric'));
      setBusy(false);
    }
  }

  const coverageOptions: { value: string | null; label: string }[] = [
    { value: null, label: t('coverageInherit') },
    { value: 'InsuranceCovered', label: t('coverageInsuranceCovered') },
    { value: 'SelfPaid', label: t('coverageSelfPaid') },
  ];

  return (
    <Modal visible animationType="slide" onRequestClose={onClose} transparent={false}>
      <ScrollView contentContainerStyle={styles.container}>
        <Text accessibilityRole="header" style={styles.title}>
          {t('editPackage')} — {box.label ?? `${t('packageOrdinal')} ${box.ordinal}`}
        </Text>

        {error ? <Notice tone="danger" message={error} /> : null}

        <Card>
          <Text style={styles.label}>{t('packageLabel')}</Text>
          <TextInput style={styles.input} value={label} onChangeText={setLabel} accessibilityLabel={t('packageLabel')} />
          <Text style={styles.muted}>{t('packageLabelHint')}</Text>

          <Text style={styles.label}>
            {t('expiresOn')} · {t('optional')}
          </Text>
          <TextInput
            style={styles.input}
            value={expiresOn}
            onChangeText={setExpiresOn}
            keyboardType="numbers-and-punctuation"
            placeholder="2027-06-30"
            placeholderTextColor={palette.inkFaint}
            accessibilityLabel={t('expiresOn')}
          />

          <Text style={styles.label}>{t('coverageTitle')}</Text>
          <View style={styles.options}>
            {coverageOptions.map((option) => (
              <Button
                key={option.value ?? 'inherit'}
                tone={coverage === option.value ? 'primary' : 'secondary'}
                label={option.label}
                accessibilityState={{ selected: coverage === option.value }}
                onPress={() => setCoverage(option.value)}
                style={styles.option}
              />
            ))}
          </View>

          <Text style={styles.label}>
            {t('lotNumber')} · {t('optional')}
          </Text>
          <TextInput style={styles.input} value={lotNumber} onChangeText={setLotNumber} accessibilityLabel={t('lotNumber')} />

          <Text style={styles.label}>
            {t('storageLocation')} · {t('optional')}
          </Text>
          <TextInput
            style={styles.input}
            value={storageLocation}
            onChangeText={setStorageLocation}
            accessibilityLabel={t('storageLocation')}
          />
          <Text style={styles.muted}>{t('storageLocationHint')}</Text>

          <Text style={styles.label}>
            {t('note')} · {t('optional')}
          </Text>
          <TextInput style={styles.input} value={note} onChangeText={setNote} accessibilityLabel={t('note')} />
        </Card>

        <Text style={styles.muted}>{t('queuedOffline')}</Text>

        <View style={styles.actions}>
          <Button tone="secondary" label={t('cancel')} onPress={onClose} disabled={busy} />
          <Button label={t('save')} onPress={() => void save()} disabled={busy} />
        </View>
      </ScrollView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { padding: 20, gap: 14, paddingBottom: 48, backgroundColor: palette.surface, flexGrow: 1 },
  title: { fontSize: 22, fontWeight: '800', color: palette.ink },
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
  label: { fontSize: 14, fontWeight: '700', color: palette.inkMuted },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderColor: palette.line,
    borderRadius: 12,
    paddingHorizontal: 14,
    fontSize: 18,
    color: palette.ink,
    backgroundColor: palette.raised,
  },
  options: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  option: { minHeight: 40, paddingHorizontal: 12 },
  actions: { flexDirection: 'row', gap: 10, justifyContent: 'flex-end' },
});
