import { useState } from 'react';
import { Modal, ScrollView, Text, TextInput, View } from 'react-native';
import * as Crypto from 'expo-crypto';
import { useSQLiteContext } from 'expo-sqlite';
import { enqueueCommand } from '../data/outbox';
import { buildPerson } from '../lib/commands';
import { sheetStyles as styles } from '../ui/sheet';
import { Button, Card, Notice, useTranslate } from '../ui/theme';

/**
 * A person the household looks after, created on the phone.
 *
 * The person exists on the phone the moment the sheet closes, under an id the phone
 * chose, and the server hears on the next sync — a plan or a box can point at them
 * before it has. The web's form is one field, and so is this.
 */
export function AddPersonSheet({ onClose, onQueued }: { onClose: () => void; onQueued: () => void }) {
  const { t } = useTranslate();
  const db = useSQLiteContext();

  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    const outcome = buildPerson(name);
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
        { kind: 'person.create', targetId: id, medicationId: null, body: { id, ...outcome.body } },
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
          {t('addPerson')}
        </Text>

        {error ? <Notice tone="danger" message={error} /> : null}

        <Card>
          <Text style={styles.label}>{t('personName')}</Text>
          <TextInput
            style={styles.input}
            value={name}
            onChangeText={setName}
            autoFocus
            accessibilityLabel={t('personName')}
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
