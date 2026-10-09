import { useState } from 'react';
import { Modal, ScrollView, Text, TextInput, View } from 'react-native';
import * as Crypto from 'expo-crypto';
import { useSQLiteContext } from 'expo-sqlite';
import { enqueueCommand } from '../data/outbox';
import { FORMS, FORM_KEYS, buildMedication, defaultUnitFor } from '../lib/commands';
import { Choices } from '../ui/Choices';
import { sheetStyles as styles } from '../ui/sheet';
import { Button, Card, Notice, palette, useTranslate } from '../ui/theme';

/**
 * Defining what a medication *is*, on the phone — separate from adding its stock, as on
 * the web. Reduced to what a phone sheet can carry: name, form, strength, the usual box
 * size and who pays. The unit follows the form by the server's rule and is shown, not
 * asked. The cautions, tags and the rest stay on the web's larger form, where the
 * medicine can be edited once it has synced.
 */
export function AddMedicationSheet({ onClose, onQueued }: { onClose: () => void; onQueued: () => void }) {
  const { t } = useTranslate();
  const db = useSQLiteContext();

  const [name, setName] = useState('');
  const [form, setForm] = useState<string>('Tablet');
  const [strength, setStrength] = useState('');
  const [capacity, setCapacity] = useState('');
  const [coverage, setCoverage] = useState<string>('Unspecified');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const unit = defaultUnitFor(form).toLowerCase();

  async function submit() {
    const outcome = buildMedication({ name, form, strength, capacity, coverage });
    if (!outcome.ok) {
      setError(t(outcome.error));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const id = Crypto.randomUUID();
      await enqueueCommand(
        db,
        { kind: 'medication.create', targetId: id, medicationId: id, body: { id, ...outcome.body } },
        new Date().toISOString(),
      );
      onQueued();
    } catch {
      setError(t('errorGeneric'));
      setBusy(false);
    }
  }

  return (
    <Modal visible animationType="slide" onRequestClose={onClose} transparent={false}>
      <ScrollView contentContainerStyle={styles.container}>
        <Text accessibilityRole="header" style={styles.title}>
          {t('addMedication')}
        </Text>

        {error ? <Notice tone="danger" message={error} /> : null}

        <Card>
          <Text style={styles.label}>{t('medicationName')}</Text>
          <TextInput
            style={styles.input}
            value={name}
            onChangeText={setName}
            autoFocus
            accessibilityLabel={t('medicationName')}
          />

          <Text style={styles.label}>{t('form')}</Text>
          <Choices
            options={FORMS.map((option) => ({ value: option, label: t(FORM_KEYS[option]) }))}
            value={form}
            onChange={setForm}
            accessibilityLabel={t('form')}
          />
          <Text style={styles.muted}>
            {t('unitLabel')}: {unit}
          </Text>

          <Text style={styles.label}>
            {t('strength')} · {t('optional')}
          </Text>
          <TextInput
            style={styles.input}
            value={strength}
            onChangeText={setStrength}
            placeholder={t('strengthPlaceholder')}
            placeholderTextColor={palette.inkFaint}
            accessibilityLabel={t('strength')}
          />

          <Text style={styles.label}>
            {t('defaultPackageSize')} ({unit}) · {t('optional')}
          </Text>
          <TextInput
            style={styles.input}
            value={capacity}
            onChangeText={setCapacity}
            keyboardType="decimal-pad"
            placeholder="28"
            placeholderTextColor={palette.inkFaint}
            accessibilityLabel={t('defaultPackageSize')}
          />
          <Text style={styles.muted}>{t('defaultPackageSizeHint')}</Text>

          <Text style={styles.label}>{t('coverageTitle')}</Text>
          <Choices
            options={[
              { value: 'Unspecified', label: t('coverageUnspecified') },
              { value: 'InsuranceCovered', label: t('coverageInsuranceCovered') },
              { value: 'SelfPaid', label: t('coverageSelfPaid') },
            ]}
            value={coverage}
            onChange={setCoverage}
            accessibilityLabel={t('coverageTitle')}
          />
          <Text style={styles.muted}>{t('coverageHint')}</Text>
        </Card>

        <Text style={styles.muted}>{t('queuedOffline')}</Text>

        <View style={styles.actions}>
          <Button tone="secondary" label={t('cancel')} onPress={onClose} disabled={busy} />
          <Button label={t('save')} onPress={() => void submit()} disabled={busy} />
        </View>
      </ScrollView>
    </Modal>
  );
}
