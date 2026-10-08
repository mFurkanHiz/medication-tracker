import { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import type { Session } from '../data/session';
import { readPlans, type PlanRow } from '../data/snapshot';
import { syncNow } from '../data/sync';
import { enumKey } from '../lib/i18n';
import { formatLocalDate, localDate } from '../lib/local-date';
import { STATUS_KEYS, describeSchedule, planStatus, type PlanStatus } from '../lib/plans';
import { formatQuantity } from '../lib/quantity';
import { startOfDayParts } from '../notifications/schedule';
import { Badge, Card, Notice, SectionTitle, palette, useTranslate } from '../ui/theme';

/**
 * Every plan the household has, grouped by person, with the schedule in words and
 * where it stands today — the web's plans screen, read from the cached snapshot.
 *
 * Read-only for now: pausing, ending and editing a plan are decisions that create a
 * new effective-dated version on the server, and they go through the web until the
 * phone's outbox carries them. The screen says so.
 */
export function PlansScreen({ session }: { session: Session }) {
  const { t, locale } = useTranslate();
  const db = useSQLiteContext();

  const [rows, setRows] = useState<PlanRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [offline, setOffline] = useState(false);

  const config = useMemo(
    () => ({ apiUrl: session.apiUrl, accessToken: session.accessToken }),
    [session.apiUrl, session.accessToken],
  );

  const reload = useCallback(async () => {
    setRows(await readPlans(db));
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

  const now = new Date();
  const withStatus = rows.map((row) => ({
    row,
    // Each plan's own calendar decides whether it has started or ended.
    status: planStatus(row, startOfDayParts(now, row.timeZoneId)),
  }));
  const current = withStatus.filter((entry) => entry.status !== 'ended');
  const past = withStatus.filter((entry) => entry.status === 'ended');

  const people = new Map<string, typeof current>();
  for (const entry of current) {
    const list = people.get(entry.row.personName) ?? [];
    list.push(entry);
    people.set(entry.row.personName, list);
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <Text style={styles.muted}>{t('loading')}</Text>
      </View>
    );
  }

  return (
    <ScrollView
      contentContainerStyle={styles.container}
      refreshControl={<RefreshControl refreshing={syncing} onRefresh={() => void refresh()} />}
    >
      <SectionTitle>{t('plans')}</SectionTitle>
      <Text style={styles.muted}>{t('editOnWeb')}</Text>
      {offline ? <Notice tone="warning" message={t('offline')} /> : null}

      {rows.length === 0 ? (
        <Card>
          <Text style={styles.muted}>{t('plansEmpty')}</Text>
        </Card>
      ) : null}

      {[...people.entries()].map(([personName, entries]) => (
        <View key={personName} style={styles.group}>
          <Text style={styles.person}>{personName}</Text>
          {entries.map(({ row, status }) => (
            <PlanCard key={row.versionId} row={row} status={status} locale={locale} />
          ))}
        </View>
      ))}

      {past.length > 0 ? (
        <View style={styles.group}>
          <Text style={styles.person}>{t('pastPlans')}</Text>
          {past.map(({ row, status }) => (
            <PlanCard key={row.versionId} row={row} status={status} locale={locale} />
          ))}
        </View>
      ) : null}
    </ScrollView>
  );
}

function PlanCard({ row, status, locale }: { row: PlanRow; status: PlanStatus; locale: string }) {
  const { t } = useTranslate();
  const meal = enumKey(row.mealRelation);
  const tone = status === 'active' ? 'positive' : status === 'paused' ? 'warning' : 'neutral';

  return (
    <Card style={status === 'ended' ? styles.ended : undefined}>
      <View style={styles.titleRow}>
        <View style={styles.titleBody}>
          <Text style={styles.medication}>{row.medicationName}</Text>
          <Text style={styles.muted}>
            {formatQuantity(row.dose)} {row.unit.toLowerCase()}
            {meal ? ` · ${t(meal)}` : ''}
          </Text>
        </View>
        <Badge tone={tone} label={t(STATUS_KEYS[status])} />
      </View>

      <Text style={styles.schedule}>{describeSchedule(row, t)}</Text>

      {row.effectiveFrom || row.effectiveTo ? (
        <Text style={styles.muted}>
          {row.effectiveFrom ? `${t('startsOn')}: ${formatLocalDate(row.effectiveFrom, locale)}` : ''}
          {row.effectiveFrom && row.effectiveTo ? ' · ' : ''}
          {row.effectiveTo ? `${t('endsOn')}: ${formatLocalDate(row.effectiveTo, locale)}` : ''}
        </Text>
      ) : null}
    </Card>
  );
}

const styles = StyleSheet.create({
  container: { padding: 16, gap: 12, paddingBottom: 48 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
  group: { gap: 10 },
  person: { fontSize: 16, fontWeight: '800', color: palette.ink, marginTop: 4 },
  titleRow: { flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'space-between', gap: 12 },
  titleBody: { flex: 1 },
  medication: { fontSize: 18, fontWeight: '800', color: palette.ink },
  schedule: { fontSize: 15, fontWeight: '700', color: palette.accentInk },
  ended: { opacity: 0.7 },
});
