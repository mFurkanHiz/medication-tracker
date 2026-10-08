import { useState } from 'react';
import { Modal, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import { enqueueCommand } from '../data/outbox';
import type { StockRow } from '../data/snapshot';
import { buildAddStock } from '../lib/commands';
import { formatQuantity } from '../lib/quantity';
import { Button, Card, Notice, palette, useTranslate } from '../ui/theme';

/**
 * Stock the household brought home, queued for the server.
 *
 * The web's form, reduced to what a phone can usefully ask: how many sealed boxes, what
 * is left in an opened one, the box size, and any loose amount. The box size is offered
 * from the catalogue's default or the last box, because typing "28" every time is how
 * people stop recording. What is typed is checked here with the server's rules, so a
 * mistake is heard now and not as a refusal after the next sync.
 */
export function AddStockSheet({ row, onClose, onQueued }: { row: StockRow; onClose: () => void; onQueued: () => void }) {
  const { t } = useTranslate();
  const db = useSQLiteContext();

  const lastBox = row.packages.find((box) => box.state === 'Sealed' || box.state === 'Opened');
  const offeredCapacity = row.defaultCapacity ?? lastBox?.capacity ?? null;

  const [fullPackages, setFullPackages] = useState('1');
  const [openedRemaining, setOpenedRemaining] = useState('');
  const [capacity, setCapacity] = useState(offeredCapacity ? formatQuantity(offeredCapacity) : '');
  const [loose, setLoose] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    const outcome = buildAddStock({ fullPackages, openedRemaining, capacity, loose }, row.defaultCapacity);

    if (!outcome.ok) {
      setError(t(outcome.error));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await enqueueCommand(
        db,
        { kind: 'stock.add', targetId: row.id, medicationId: row.id, body: outcome.body },
        new Date().toISOString(),
      );
      onQueued();
    } catch {
      setError(t('errorGeneric'));
      setBusy(false);
    }
  }

  const unit = row.unit.toLowerCase();

  return (
    <Modal visible animationType="slide" onRequestClose={onClose} transparent={false}>
      <ScrollView contentContainerStyle={styles.container}>
        <Text accessibilityRole="header" style={styles.title}>
          {t('addStock')} — {row.name}
        </Text>

        {error ? <Notice tone="danger" message={error} /> : null}

        <Card>
          <Text style={styles.label}>{t('fullPackagesLabel')}</Text>
          <TextInput
            style={styles.input}
            value={fullPackages}
            onChangeText={setFullPackages}
            keyboardType="number-pad"
            accessibilityLabel={t('fullPackagesLabel')}
          />

          <Text style={styles.label}>
            {t('capacityLabel')} ({unit})
          </Text>
          <TextInput
            style={styles.input}
            value={capacity}
            onChangeText={setCapacity}
            keyboardType="decimal-pad"
            accessibilityLabel={t('capacityLabel')}
          />

          <Text style={styles.label}>
            {t('openedRemainingLabel')} ({unit}) · {t('optional')}
          </Text>
          <TextInput
            style={styles.input}
            value={openedRemaining}
            onChangeText={setOpenedRemaining}
            keyboardType="decimal-pad"
            placeholder="8"
            placeholderTextColor={palette.inkFaint}
            accessibilityLabel={t('openedRemainingLabel')}
          />

          <Text style={styles.label}>
            {t('looseLabel')} ({unit}) · {t('optional')}
          </Text>
          <TextInput
            style={styles.input}
            value={loose}
            onChangeText={setLoose}
            keyboardType="decimal-pad"
            placeholder="1/2"
            placeholderTextColor={palette.inkFaint}
            accessibilityLabel={t('looseLabel')}
          />
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

const styles = StyleSheet.create({
  container: { padding: 20, gap: 14, paddingBottom: 48, backgroundColor: palette.surface, flexGrow: 1 },
  title: { fontSize: 22, fontWeight: '800', color: palette.ink },
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
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
  actions: { flexDirection: 'row', gap: 10, justifyContent: 'flex-end' },
});
