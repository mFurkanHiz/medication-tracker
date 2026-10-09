import { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import { listCommands, type CommandRow } from '../data/command-queue';
import { dismissCommand } from '../data/outbox';
import type { Session } from '../data/session';
import { readPeople, type PersonRow } from '../data/snapshot';
import { syncNow } from '../data/sync';
import { localDate } from '../lib/local-date';
import { RefusedCommands } from '../ui/RefusedCommands';
import { Badge, Button, Card, Notice, SectionTitle, palette, useTranslate } from '../ui/theme';
import { AddPersonSheet } from './AddPersonSheet';

/**
 * Who the household looks after, with how many plans each person has — the web's
 * people screen, read from the cached snapshot so it renders with no network.
 *
 * Adding a person queues a command: the person is on the phone at once, under an id
 * the phone chose, and the server hears on the next sync. Renaming, archiving and
 * restoring still go through the web, and the screen says so.
 */
export function PeopleScreen({ session }: { session: Session }) {
  const { t } = useTranslate();
  const db = useSQLiteContext();

  const [rows, setRows] = useState<PersonRow[]>([]);
  const [queued, setQueued] = useState<CommandRow[]>([]);
  const [refused, setRefused] = useState<CommandRow[]>([]);
  const [adding, setAdding] = useState(false);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [offline, setOffline] = useState(false);

  const config = useMemo(
    () => ({ apiUrl: session.apiUrl, accessToken: session.accessToken }),
    [session.apiUrl, session.accessToken],
  );

  const isPersonCommand = (row: CommandRow) => row.kind === 'person.create';

  const reload = useCallback(async () => {
    setRows(await readPeople(db));
    setQueued((await listCommands(db, 'queued')).filter(isPersonCommand));
    setRefused((await listCommands(db, 'rejected')).filter(isPersonCommand));
    setLoading(false);
  }, [db]);

  const refresh = useCallback(async () => {
    setSyncing(true);

    try {
      const outcome = await syncNow(db, config, session.householdId, localDate(), new Date().toISOString());
      setOffline(outcome.offline);
      await reload();
    } finally {
      setSyncing(false);
    }
  }, [db, config, session.householdId, reload]);

  useEffect(() => {
    void reload();
  }, [reload]);

  async function afterQueued() {
    setAdding(false);
    await reload();
    void refresh();
  }

  async function dismiss(idempotencyKey: string) {
    await dismissCommand(db, idempotencyKey);
    await reload();
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <Text style={styles.muted}>{t('loading')}</Text>
      </View>
    );
  }

  const active = rows.filter((row) => !row.isArchived);
  const archived = rows.filter((row) => row.isArchived);
  const isPending = (personId: string) => queued.some((command) => command.targetId === personId);

  return (
    <>
      <ScrollView
        contentContainerStyle={styles.container}
        refreshControl={<RefreshControl refreshing={syncing} onRefresh={() => void refresh()} />}
      >
        <SectionTitle>{t('people')}</SectionTitle>
        <Text style={styles.muted}>{t('otherEditsOnWeb')}</Text>
        {offline ? <Notice tone="warning" message={t('offline')} /> : null}

        <RefusedCommands rows={refused} onDismiss={(key) => void dismiss(key)} />

        <Button label={t('addPerson')} onPress={() => setAdding(true)} />

        {rows.length === 0 ? (
          <Card>
            <Text style={styles.muted}>{t('peopleEmpty')}</Text>
            <Text style={styles.muted}>{t('peopleEmptyHint')}</Text>
          </Card>
        ) : null}

        {active.map((row) => (
          <PersonCard key={row.id} row={row} pending={isPending(row.id)} />
        ))}

        {archived.length > 0 ? (
          <View style={styles.group}>
            <Text style={styles.groupTitle}>{t('archivedPeople')}</Text>
            {archived.map((row) => (
              <PersonCard key={row.id} row={row} pending={false} />
            ))}
          </View>
        ) : null}
      </ScrollView>

      {adding ? <AddPersonSheet onClose={() => setAdding(false)} onQueued={() => void afterQueued()} /> : null}
    </>
  );
}

function PersonCard({ row, pending }: { row: PersonRow; pending: boolean }) {
  const { t } = useTranslate();

  return (
    <Card style={row.isArchived ? styles.archived : undefined}>
      <Text style={styles.name}>{row.name}</Text>
      <View style={styles.badges}>
        <Badge label={`${row.planCount} ${t('plansLabel')}`} />
        {row.pausedCount > 0 ? <Badge tone="warning" label={`${row.pausedCount} ${t('pausedLabel')}`} /> : null}
        {row.isArchived ? <Badge label={t('stateArchived')} /> : null}
        {pending ? <Badge tone="warning" label={t('createdOffline')} /> : null}
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  container: { padding: 16, gap: 12, paddingBottom: 48 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
  name: { fontSize: 18, fontWeight: '800', color: palette.ink },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  group: { gap: 10 },
  groupTitle: { fontSize: 16, fontWeight: '800', color: palette.ink, marginTop: 4 },
  archived: { opacity: 0.7 },
});
