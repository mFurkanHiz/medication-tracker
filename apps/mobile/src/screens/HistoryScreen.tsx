import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import type { Session } from '../data/session';
import {
  readHistory,
  type HistoryAdministration,
  type HistoryCorrection,
  type HistoryInventory,
  type HistoryPending,
  type History,
} from '../data/snapshot';
import { syncNow } from '../data/sync';
import { entryKey, latenessLabel, outcomeKey, packageLabel, signedQuantity } from '../lib/history';
import { formatMoment, localDate } from '../lib/local-date';
import { formatQuantity } from '../lib/quantity';
import { Badge, Card, Notice, SectionTitle, palette, useTranslate } from '../ui/theme';

/**
 * Everything that happened, as the server last listed it — the web's history screen,
 * read from the cached snapshot so it renders offline.
 *
 * Stock movements, recorded doses and stock-source corrections are separate kinds of
 * event, so they are separate sections rather than one list that hides which is which
 * (the web's reasoning, kept). One thing the web does not have: the doses this device
 * recorded and the server has not acknowledged yet, at the top, so a dose taken on the
 * bus is not invisible until the bus has signal.
 *
 * Read-only: correcting a dose's stock source goes through the web for now.
 */
export function HistoryScreen({ session }: { session: Session }) {
  const { t, locale } = useTranslate();
  const db = useSQLiteContext();

  const [history, setHistory] = useState<History | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [offline, setOffline] = useState(false);

  const config = useMemo(
    () => ({ apiUrl: session.apiUrl, accessToken: session.accessToken }),
    [session.apiUrl, session.accessToken],
  );

  const reload = useCallback(async () => {
    setHistory(await readHistory(db));
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

  if (!history) {
    return (
      <View style={styles.center}>
        <Text style={styles.muted}>{t('loading')}</Text>
      </View>
    );
  }

  const empty =
    history.pending.length === 0 &&
    history.corrections.length === 0 &&
    history.administrations.length === 0 &&
    history.inventory.length === 0;

  return (
    <ScrollView
      contentContainerStyle={styles.container}
      refreshControl={<RefreshControl refreshing={syncing} onRefresh={() => void refresh()} />}
    >
      <SectionTitle>{t('history')}</SectionTitle>
      {offline ? <Notice tone="warning" message={t('offline')} /> : null}

      {empty ? (
        <Card>
          <Text style={styles.muted}>{t('historyEmpty')}</Text>
        </Card>
      ) : null}

      {history.pending.length > 0 ? (
        <Section title={t('historyPending')}>
          {history.pending.map((row) => (
            <PendingRow key={row.id} row={row} locale={locale} />
          ))}
        </Section>
      ) : null}

      {history.corrections.length > 0 ? (
        <Section title={t('historyCorrections')}>
          {history.corrections.map((row) => (
            <CorrectionRow key={row.id} row={row} locale={locale} />
          ))}
        </Section>
      ) : null}

      {history.administrations.length > 0 ? (
        <Section title={t('historyAdministrations')}>
          {history.administrations.map((row) => (
            <AdministrationRow key={row.id} row={row} locale={locale} />
          ))}
        </Section>
      ) : null}

      {history.inventory.length > 0 ? (
        <Section title={t('historyInventory')}>
          {history.inventory.map((row) => (
            <InventoryRow key={row.id} row={row} locale={locale} />
          ))}
        </Section>
      ) : null}
    </ScrollView>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <View style={styles.group}>
      <Text style={styles.groupTitle}>{title}</Text>
      {children}
    </View>
  );
}

function PendingRow({ row, locale }: { row: HistoryPending; locale: string }) {
  const { t } = useTranslate();
  const outcome = outcomeKey(row.outcome);

  return (
    <Card>
      <View style={styles.titleRow}>
        <View style={styles.titleBody}>
          <Text style={styles.title}>{row.medicationName}</Text>
          <Text style={styles.muted}>
            {row.personName}
            {row.quantity ? ` · ${formatQuantity(row.quantity)}` : ''}
          </Text>
        </View>
        <Badge tone="warning" label={t('pendingLabel')} />
      </View>
      <View style={styles.badges}>
        {outcome ? <Badge tone={row.outcome === 'Skipped' ? 'neutral' : 'positive'} label={t(outcome)} /> : null}
      </View>
      <Text style={styles.time}>{formatMoment(row.occurredAt, locale)}</Text>
    </Card>
  );
}

function CorrectionRow({ row, locale }: { row: HistoryCorrection; locale: string }) {
  const { t } = useTranslate();

  return (
    <Card>
      <View style={styles.titleRow}>
        <View style={styles.titleBody}>
          {row.medicationName ? <Text style={styles.title}>{row.medicationName}</Text> : null}
          <Text style={styles.muted}>
            {packageLabel(row.fromPackageLabel, t)} → {packageLabel(row.toPackageLabel, t)} · {formatQuantity(row.quantity)}
            {row.reason ? ` · ${row.reason}` : ''}
          </Text>
        </View>
        <Badge tone="accent" label={t('correctionApplied')} />
      </View>
      <Text style={styles.time}>{formatMoment(row.recordedAt, locale)}</Text>
    </Card>
  );
}

function AdministrationRow({ row, locale }: { row: HistoryAdministration; locale: string }) {
  const { t } = useTranslate();
  const outcome = outcomeKey(row.outcome);
  const lateness = latenessLabel(row.latenessMinutes, t);

  return (
    <Card>
      <View style={styles.titleRow}>
        <View style={styles.titleBody}>
          <Text style={styles.title}>{row.medicationName}</Text>
          <Text style={styles.muted}>
            {row.personName}
            {row.quantity ? ` · ${formatQuantity(row.quantity)}` : ''}
          </Text>
        </View>
      </View>
      <View style={styles.badges}>
        {outcome ? <Badge tone={row.outcome === 'Skipped' ? 'neutral' : 'positive'} label={t(outcome)} /> : null}
        {row.stockSource === 'UntrackedExternal' ? <Badge tone="warning" label={t('untrackedSource')} /> : null}
        {/* Lateness is derived from the timestamps, never stored as a status. */}
        {lateness ? <Badge label={lateness} /> : null}
      </View>
      <Text style={styles.time}>{formatMoment(row.occurredAt, locale)}</Text>
    </Card>
  );
}

function InventoryRow({ row, locale }: { row: HistoryInventory; locale: string }) {
  const { t } = useTranslate();
  const kind = entryKey(row.entryType);
  const positive = row.quantity.numerator > 0;

  return (
    <Card>
      <View style={styles.titleRow}>
        <View style={styles.titleBody}>
          <Text style={styles.title}>{row.medicationName}</Text>
          <Text style={styles.muted}>
            {kind ? t(kind) : row.entryType}
            {row.packageLabel !== null ? ` · ${packageLabel(row.packageLabel, t)}` : ''}
            {row.reason ? ` · ${row.reason}` : ''}
          </Text>
        </View>
        <Text style={[styles.amount, positive ? styles.positive : styles.negative]}>{signedQuantity(row.quantity)}</Text>
      </View>
      <Text style={styles.time}>{formatMoment(row.recordedAt, locale)}</Text>
    </Card>
  );
}

const styles = StyleSheet.create({
  container: { padding: 16, gap: 12, paddingBottom: 48 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
  group: { gap: 10 },
  groupTitle: { fontSize: 16, fontWeight: '800', color: palette.ink, marginTop: 4 },
  titleRow: { flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'space-between', gap: 12 },
  titleBody: { flex: 1 },
  title: { fontSize: 17, fontWeight: '800', color: palette.ink },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  time: { color: palette.inkFaint, fontSize: 13 },
  amount: { fontSize: 16, fontWeight: '800' },
  positive: { color: palette.positive },
  negative: { color: palette.accentInk },
});
