import { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import type { Session } from '../data/session';
import { readPeople, type PersonRow } from '../data/snapshot';
import { syncNow } from '../data/sync';
import { localDate } from '../lib/local-date';
import { Badge, Card, Notice, SectionTitle, palette, useTranslate } from '../ui/theme';

/**
 * Who the household looks after, with how many plans each person has — the web's
 * people screen, read from the cached snapshot so it renders with no network.
 *
 * Read-only: adding, renaming or archiving a person goes through the web until the
 * phone's outbox carries those commands. The screen says so.
 */
export function PeopleScreen({ session }: { session: Session }) {
  const { t } = useTranslate();
  const db = useSQLiteContext();

  const [rows, setRows] = useState<PersonRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [offline, setOffline] = useState(false);

  const config = useMemo(
    () => ({ apiUrl: session.apiUrl, accessToken: session.accessToken }),
    [session.apiUrl, session.accessToken],
  );

  const reload = useCallback(async () => {
    setRows(await readPeople(db));
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

  if (loading) {
    return (
      <View style={styles.center}>
        <Text style={styles.muted}>{t('loading')}</Text>
      </View>
    );
  }

  const active = rows.filter((row) => !row.isArchived);
  const archived = rows.filter((row) => row.isArchived);

  return (
    <ScrollView
      contentContainerStyle={styles.container}
      refreshControl={<RefreshControl refreshing={syncing} onRefresh={() => void refresh()} />}
    >
      <SectionTitle>{t('people')}</SectionTitle>
      <Text style={styles.muted}>{t('editOnWeb')}</Text>
      {offline ? <Notice tone="warning" message={t('offline')} /> : null}

      {rows.length === 0 ? (
        <Card>
          <Text style={styles.muted}>{t('peopleEmpty')}</Text>
        </Card>
      ) : null}

      {active.map((row) => (
        <PersonCard key={row.id} row={row} />
      ))}

      {archived.length > 0 ? (
        <View style={styles.group}>
          <Text style={styles.groupTitle}>{t('archivedPeople')}</Text>
          {archived.map((row) => (
            <PersonCard key={row.id} row={row} />
          ))}
        </View>
      ) : null}
    </ScrollView>
  );
}

function PersonCard({ row }: { row: PersonRow }) {
  const { t } = useTranslate();

  return (
    <Card style={row.isArchived ? styles.archived : undefined}>
      <Text style={styles.name}>{row.name}</Text>
      <View style={styles.badges}>
        <Badge label={`${row.planCount} ${t('plansLabel')}`} />
        {row.pausedCount > 0 ? <Badge tone="warning" label={`${row.pausedCount} ${t('pausedLabel')}`} /> : null}
        {row.isArchived ? <Badge label={t('stateArchived')} /> : null}
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
