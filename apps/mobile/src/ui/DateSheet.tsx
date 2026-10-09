import { useState } from 'react';
import { Modal, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { isValidDay } from '../lib/commands';
import { localDate } from '../lib/local-date';
import { Button, Card, Notice, palette, useTranslate } from './theme';

/**
 * One date, confirmed: the last day of a plan, or the first day it starts again.
 *
 * Typed as `YYYY-MM-DD`, today offered, because a decision about a day is the whole
 * question here and a wrong day must be refused before it is queued.
 */
export function DateSheet({
  title,
  label,
  hint,
  confirmLabel,
  onClose,
  onConfirm,
}: {
  title: string;
  label: string;
  hint: string;
  confirmLabel: string;
  onClose: () => void;
  onConfirm: (day: string) => Promise<void> | void;
}) {
  const { t } = useTranslate();
  const [day, setDay] = useState(localDate());
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function confirm() {
    if (!isValidDay(day)) {
      setError(t('invalidDate'));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await onConfirm(day.trim());
    } catch {
      setError(t('errorGeneric'));
      setBusy(false);
    }
  }

  return (
    <Modal visible animationType="slide" onRequestClose={onClose} transparent={false}>
      <ScrollView contentContainerStyle={styles.container}>
        <Text accessibilityRole="header" style={styles.title}>
          {title}
        </Text>
        <Text style={styles.muted}>{hint}</Text>

        {error ? <Notice tone="danger" message={error} /> : null}

        <Card>
          <Text style={styles.label}>{label}</Text>
          <TextInput
            style={styles.input}
            value={day}
            onChangeText={setDay}
            keyboardType="numbers-and-punctuation"
            placeholder="2026-10-09"
            placeholderTextColor={palette.inkFaint}
            accessibilityLabel={label}
          />
        </Card>

        <Text style={styles.muted}>{t('queuedOffline')}</Text>

        <View style={styles.actions}>
          <Button tone="secondary" label={t('cancel')} onPress={onClose} disabled={busy} />
          <Button label={confirmLabel} onPress={() => void confirm()} disabled={busy} />
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
  actions: { flexDirection: 'row', gap: 10, justifyContent: 'flex-end' },
});
