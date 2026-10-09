import { useState } from 'react';
import { Modal, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import { enqueueCommand } from '../data/outbox';
import type { StockRow } from '../data/snapshot';
import { buildCountLines } from '../lib/commands';
import { formatQuantity } from '../lib/quantity';
import { Button, Card, Notice, palette, useTranslate } from '../ui/theme';

/** What the user typed, keyed by medication or by `medication:box`, as on the web. */
type Entries = Record<string, string>;

const packageKey = (medicationId: string, packageId: string) => `${medicationId}:${packageId}`;

/**
 * Counting what is physically there, queued for the server.
 *
 * The everyday path is one number per medicine; counting boxes one by one is
 * reconciliation work behind a switch, as on the web. Only the rows filled in are
 * recorded. The count is a command like any other: written to the phone first, applied
 * to the cached stock at once, sent with its key on the next sync. Past counts and
 * corrections stay on the web, and the sheet says so.
 */
export function CountingSheet({
  rows,
  householdId,
  onClose,
  onQueued,
}: {
  rows: StockRow[];
  householdId: string;
  onClose: () => void;
  onQueued: () => void;
}) {
  const { t } = useTranslate();
  const db = useSQLiteContext();

  const [entries, setEntries] = useState<Entries>({});
  const [byPackage, setByPackage] = useState<Record<string, boolean>>({});
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const setEntry = (key: string, value: string) => setEntries((current) => ({ ...current, [key]: value }));

  // Switching mode clears the other mode's entries so a hidden number cannot be sent.
  const switchMode = (row: StockRow, perBox: boolean) => {
    setByPackage((current) => ({ ...current, [row.id]: perBox }));
    setEntries((current) => {
      const next = { ...current };
      delete next[row.id];
      for (const box of row.packages) {
        delete next[packageKey(row.id, box.id)];
      }
      return next;
    });
  };

  async function submit() {
    const outcome = buildCountLines(entries);

    if (!outcome.ok) {
      setError(t(outcome.error));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await enqueueCommand(
        db,
        {
          kind: 'inventory.count',
          targetId: householdId,
          medicationId: null,
          body: { lines: outcome.lines, note: note.trim() === '' ? null : note.trim() },
        },
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
          {t('countingTitle')}
        </Text>
        <Text style={styles.muted}>{t('countingDescription')}</Text>
        <Text style={styles.muted}>{t('countingRevisionsOnWeb')}</Text>

        {error ? <Notice tone="danger" message={error} /> : null}

        {rows.length === 0 ? (
          <Card>
            <Text style={styles.muted}>{t('countingEmpty')}</Text>
          </Card>
        ) : null}

        {rows.map((row) => {
          const unit = row.unit.toLowerCase();
          const available = row.packages.filter((box) => box.state === 'Sealed' || box.state === 'Opened');
          const perBox = byPackage[row.id] === true;

          return (
            <Card key={row.id}>
              <Text style={styles.medication}>
                {row.name}
                {row.strength ? <Text style={styles.muted}> · {row.strength}</Text> : null}
              </Text>
              <Text style={styles.muted}>
                {t('countingExpected')}: {formatQuantity(row.total)} {unit}
              </Text>

              {!perBox ? (
                <>
                  <Text style={styles.label}>
                    {t('countingObserved')} ({unit})
                  </Text>
                  <TextInput
                    style={styles.input}
                    value={entries[row.id] ?? ''}
                    onChangeText={(value) => setEntry(row.id, value)}
                    keyboardType="decimal-pad"
                    placeholder={t('countingObservedHint')}
                    placeholderTextColor={palette.inkFaint}
                    accessibilityLabel={`${row.name} · ${t('countingObserved')}`}
                  />
                </>
              ) : (
                // Once the user is counting boxes, the boxes are the task.
                available.map((box) => {
                  const name = box.label ?? `${t('packageOrdinal')} ${box.ordinal}`;
                  return (
                    <View key={box.id} style={styles.boxRow}>
                      <Text style={styles.label}>
                        {name} · {t('countingExpected')}: {formatQuantity(box.remaining)}
                      </Text>
                      <TextInput
                        style={styles.input}
                        value={entries[packageKey(row.id, box.id)] ?? ''}
                        onChangeText={(value) => setEntry(packageKey(row.id, box.id), value)}
                        keyboardType="decimal-pad"
                        placeholder={t('countingObservedHint')}
                        placeholderTextColor={palette.inkFaint}
                        accessibilityLabel={`${row.name} · ${name} · ${t('countingObserved')}`}
                      />
                    </View>
                  );
                })
              )}

              {available.length > 0 ? (
                <Button
                  tone="quiet"
                  label={t(perBox ? 'countingWholeMedication' : 'countingByPackage')}
                  accessibilityState={{ selected: perBox }}
                  onPress={() => switchMode(row, !perBox)}
                />
              ) : null}
            </Card>
          );
        })}

        <Card>
          <Text style={styles.label}>
            {t('note')} · {t('optional')}
          </Text>
          <TextInput
            style={styles.input}
            value={note}
            onChangeText={setNote}
            accessibilityLabel={t('note')}
          />
        </Card>

        <Text style={styles.muted}>{t('queuedOffline')}</Text>

        <View style={styles.actions}>
          <Button tone="secondary" label={t('cancel')} onPress={onClose} disabled={busy} />
          <Button label={t('countingSubmit')} onPress={() => void submit()} disabled={busy || rows.length === 0} />
        </View>
      </ScrollView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  container: { padding: 20, gap: 14, paddingBottom: 48, backgroundColor: palette.surface, flexGrow: 1 },
  title: { fontSize: 22, fontWeight: '800', color: palette.ink },
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
  medication: { fontSize: 17, fontWeight: '800', color: palette.ink },
  label: { fontSize: 14, fontWeight: '700', color: palette.inkMuted },
  boxRow: { gap: 6 },
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
