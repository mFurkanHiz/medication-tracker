import { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import { File, Paths } from 'expo-file-system';
import * as Sharing from 'expo-sharing';
import { META, readMeta, writeMeta } from '../data/database';
import type { Session } from '../data/session';
import { readNames } from '../data/snapshot';
import {
  ApiError,
  NetworkError,
  api,
  type AdherenceReport,
  type AdherenceTally,
  type InventoryReport,
} from '../lib/api';
import { errorKey, type MessageKey } from '../lib/i18n';
import { formatLocalDate, formatMoment } from '../lib/local-date';
import { formatQuantity } from '../lib/quantity';
import { PERIODS, periodRange, ratioPercent, type PeriodId } from '../lib/reports';
import { Badge, Button, Card, Notice, SectionTitle, palette, useTranslate } from '../ui/theme';

/** What the server last answered, kept so the screen has something to show offline. */
type ReportsCache = {
  period: PeriodId;
  fetchedAt: string;
  adherence: AdherenceReport;
  inventory: InventoryReport;
};

/**
 * Reports: what the household planned and recorded, and what it has left — the web's
 * screen. Both halves are the server's reads: the phone never computes them itself, so
 * the two clients can never disagree. The last answer is cached, and shown with its
 * time when the server cannot be reached.
 *
 * Nothing here interprets the numbers: no score, no target, no advice. The product
 * organises medication and does not practise medicine.
 */
export function ReportsScreen({ session }: { session: Session }) {
  const { t, locale } = useTranslate();
  const db = useSQLiteContext();

  const [period, setPeriod] = useState<PeriodId>('last30');
  const [cache, setCache] = useState<ReportsCache | null>(null);
  const [names, setNames] = useState<{ people: Map<string, string>; medications: Map<string, string> }>({
    people: new Map(),
    medications: new Map(),
  });
  const [loading, setLoading] = useState(false);
  const [stale, setStale] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const config = useMemo(
    () => ({ apiUrl: session.apiUrl, accessToken: session.accessToken }),
    [session.apiUrl, session.accessToken],
  );

  // The device's zone, so a local day on this screen is the user's day.
  const timeZoneId = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC', []);

  const load = useCallback(async (which: PeriodId) => {
    setLoading(true);
    setError(null);

    try {
      const { from, to } = periodRange(which, new Date());
      const [adherence, inventory] = await Promise.all([
        api.adherenceReport(config, session.householdId, from, to, timeZoneId),
        api.inventoryReport(config, session.householdId),
      ]);
      const fresh: ReportsCache = { period: which, fetchedAt: new Date().toISOString(), adherence, inventory };
      setCache(fresh);
      setStale(false);
      await writeMeta(db, META.reportsCache, JSON.stringify(fresh));
    } catch (caught) {
      if (caught instanceof NetworkError) {
        setStale(true);
      } else {
        setError(t(errorKey(caught instanceof ApiError ? caught.code : 'request_failed')));
      }
    } finally {
      setLoading(false);
    }
  }, [config, db, session.householdId, t, timeZoneId]);

  // On open: the names, the last answer if there is one, then the server.
  useEffect(() => {
    let cancelled = false;

    (async () => {
      const resolved = await readNames(db);
      const stored = await readMeta(db, META.reportsCache);
      let initial: PeriodId = 'last30';

      if (cancelled) {
        return;
      }

      setNames(resolved);

      if (stored) {
        try {
          const parsed = JSON.parse(stored) as ReportsCache;
          setCache(parsed);
          initial = parsed.period;
          setPeriod(initial);
        } catch {
          // A cache that cannot be read is simply fetched again.
        }
      }

      await load(initial);
    })();

    return () => {
      cancelled = true;
    };
  }, [db, load]);

  const choosePeriod = (which: PeriodId) => {
    setPeriod(which);
    void load(which);
  };

  const medicationName = (id: string) => names.medications.get(id) ?? '—';
  const personName = (id: string) => names.people.get(id) ?? '—';
  const day = (value: string) => formatLocalDate(value, locale);

  const adherence = cache?.adherence ?? null;
  const inventory = cache?.inventory ?? null;

  return (
    <ScrollView
      contentContainerStyle={styles.container}
      refreshControl={<RefreshControl refreshing={loading} onRefresh={() => void load(period)} />}
    >
      <SectionTitle>{t('reports')}</SectionTitle>

      {stale ? (
        <Notice tone="warning" message={cache ? `${t('reportsOfflineHint')} ${t('reportsFetchedAt')}: ${formatMoment(cache.fetchedAt, locale)}` : t('reportsNeverFetched')} />
      ) : null}
      {error ? <Notice tone="danger" message={error} /> : null}

      <View style={styles.group}>
        <Text style={styles.heading}>{t('reportsAdherence')}</Text>
        <View style={styles.periods}>
          {PERIODS.map((option) => (
            <Button
              key={option.id}
              tone={option.id === period ? 'primary' : 'secondary'}
              label={t(option.label)}
              accessibilityState={{ selected: option.id === period }}
              onPress={() => choosePeriod(option.id)}
              style={styles.periodButton}
            />
          ))}
        </View>

        {adherence ? (
          <>
            <Text style={styles.muted}>
              {day(adherence.from)} – {day(adherence.to)}
            </Text>

            {adherence.unknownTimeZoneIds.length > 0 ? <Notice tone="warning" message={t('reportUnknownTimeZone')} /> : null}

            {adherence.rows.length === 0 ? (
              <Card>
                <Text style={styles.muted}>{t('reportEmpty')}</Text>
              </Card>
            ) : (
              <>
                {adherence.rows.map((row) => (
                  <Card key={`${row.personId}-${row.medicationDefinitionId}`}>
                    <View style={styles.titleRow}>
                      <View style={styles.titleBody}>
                        <Text style={styles.title}>{medicationName(row.medicationDefinitionId)}</Text>
                        <Text style={styles.muted}>{personName(row.personId)}</Text>
                      </View>
                      <Ratio tally={row.tally} />
                    </View>
                    <Counts tally={row.tally} />
                  </Card>
                ))}

                {adherence.rows.length > 1 ? (
                  <Card style={styles.totalCard}>
                    <View style={styles.titleRow}>
                      <Text style={styles.title}>{t('reportHouseholdTotal')}</Text>
                      <Ratio tally={adherence.total} />
                    </View>
                    <Counts tally={adherence.total} />
                  </Card>
                ) : null}
              </>
            )}

            {/* Said plainly, because a number next to a medication invites being read as a
                verdict on the person taking it. */}
            <Text style={styles.faint}>{t('reportDescriptiveNotice')}</Text>
          </>
        ) : !stale && loading ? (
          <Text style={styles.muted}>{t('loading')}</Text>
        ) : null}
      </View>

      {inventory ? (
        <View style={styles.group}>
          <Text style={styles.heading}>{t('reportsInventory')}</Text>

          {inventory.lowStockCount > 0 || inventory.refillGapCount > 0 ? (
            <View style={styles.badges}>
              {inventory.lowStockCount > 0 ? <Badge tone="warning" label={`${inventory.lowStockCount} ${t('reportLowStockSummary')}`} /> : null}
              {inventory.refillGapCount > 0 ? <Badge tone="danger" label={`${inventory.refillGapCount} ${t('reportRefillGapSummary')}`} /> : null}
            </View>
          ) : null}

          {inventory.rows.length === 0 ? (
            <Card>
              <Text style={styles.muted}>{t('stockEmpty')}</Text>
            </Card>
          ) : (
            inventory.rows.map((row) => (
              <Card key={row.medicationDefinitionId}>
                <Text style={styles.title}>
                  {row.name}
                  {row.strength ? <Text style={styles.muted}> · {row.strength}</Text> : null}
                </Text>
                <Text style={styles.muted}>
                  {t('reportStockRemaining')}: {formatQuantity(row.total)} {row.unit.toLowerCase()} · {row.packageCount} {t('packagesLabel')}
                </Text>
                <View style={styles.badges}>
                  {row.isArchived ? <Badge label={t('archived')} /> : null}
                  {row.isLowStock ? <Badge tone="warning" label={t('reportLowStock')} /> : null}
                  {row.hasRefillGap ? (
                    <Badge tone="danger" label={`${t('reportRefillGap')}: ${row.refillGapDays ?? 0} ${t('reportRefillGapDays')}`} />
                  ) : null}
                </View>
                <Text style={styles.faint}>
                  {/* An as-needed-only medication is reported as unforecastable rather than given an invented date. */}
                  {!row.isForecastable || !row.projectedDepletionOn
                    ? t('reportNotForecastable')
                    : `${t('reportDepletion')}: ${day(row.projectedDepletionOn)}${
                        row.daysOfStockRemaining === null ? '' : ` · ${row.daysOfStockRemaining} ${t('reportDaysLeft')}`
                      }`}
                </Text>
              </Card>
            ))
          )}
        </View>
      ) : null}

      <ExportCard session={session} />
    </ScrollView>
  );
}

/**
 * The share of planned doses that were answered with medication, from the server's
 * exact pair; absent rather than nought when nothing was scheduled.
 */
function Ratio({ tally }: { tally: AdherenceTally }) {
  const { t } = useTranslate();
  const percent = ratioPercent(tally.onScheduleRatio);

  if (percent === null || !tally.onScheduleRatio) {
    return <Badge label={t('reportNoScheduledDoses')} />;
  }

  return (
    <Text style={styles.ratio}>
      <Text style={styles.ratioBig}>{percent}%</Text>
      <Text style={styles.muted}> ({tally.onScheduleRatio.numerator}/{tally.onScheduleRatio.denominator})</Text>
    </Text>
  );
}

/** The counts behind the ratio, so the number is always explainable. */
function Counts({ tally }: { tally: AdherenceTally }) {
  const { t } = useTranslate();
  const entries: { label: MessageKey; value: number; warn?: boolean }[] = [
    { label: 'reportPlanned', value: tally.scheduledDoses },
    { label: 'reportTakenOnSchedule', value: tally.onScheduleDoses },
    { label: 'reportMissed', value: tally.missedDoses, warn: true },
    { label: 'reportSkipped', value: tally.skipped },
    { label: 'reportPartial', value: tally.partialDoses },
    { label: 'reportExtra', value: tally.extraDoses },
  ];

  return (
    <View style={styles.counts}>
      {entries.map((entry) => (
        <View key={entry.label} style={styles.countRow}>
          <Text style={styles.muted}>{t(entry.label)}</Text>
          <Text style={[styles.countValue, entry.warn && entry.value > 0 ? styles.countWarn : null]}>{entry.value}</Text>
        </View>
      ))}
    </View>
  );
}

/**
 * Hands the household its own data back as a file, through the share sheet: the same
 * file the browser downloads, under the server's name, written to the cache so nothing
 * lingers in the app's documents.
 */
function ExportCard({ session }: { session: Session }) {
  const { t } = useTranslate();
  const [state, setState] = useState<'idle' | 'working' | 'done'>('idle');
  const [error, setError] = useState<string | null>(null);

  async function share() {
    setState('working');
    setError(null);

    try {
      if (!(await Sharing.isAvailableAsync())) {
        setError(t('exportUnavailable'));
        setState('idle');
        return;
      }

      const { text, filename } = await api.exportHousehold(
        { apiUrl: session.apiUrl, accessToken: session.accessToken },
        session.householdId,
      );
      const file = new File(Paths.cache, filename);
      file.write(text);
      await Sharing.shareAsync(file.uri, { mimeType: 'application/json', dialogTitle: t('exportTitle') });
      setState('done');
    } catch (caught) {
      const code = caught instanceof ApiError ? caught.code : caught instanceof NetworkError ? 'network' : 'request_failed';
      setError(t(code === 'network' ? 'errorNetwork' : errorKey(code)));
      setState('idle');
    }
  }

  return (
    <Card>
      <Text style={styles.heading}>{t('exportTitle')}</Text>
      <Text style={styles.muted}>{t('exportDescription')}</Text>
      <Button
        label={state === 'working' ? t('exportPreparing') : t('exportButton')}
        onPress={() => void share()}
        disabled={state === 'working'}
      />
      {state === 'done' ? <Notice tone="positive" message={t('exportDone')} /> : null}
      {error ? <Notice tone="danger" message={error} /> : null}
      <Text style={styles.faint}>{t('exportExcludesNotice')}</Text>
    </Card>
  );
}

const styles = StyleSheet.create({
  container: { padding: 16, gap: 12, paddingBottom: 48 },
  group: { gap: 10 },
  heading: { fontSize: 16, fontWeight: '800', color: palette.ink, marginTop: 4 },
  periods: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  periodButton: { minHeight: 40, paddingHorizontal: 12 },
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
  faint: { color: palette.inkFaint, fontSize: 13, lineHeight: 18 },
  titleRow: { flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'space-between', gap: 12 },
  titleBody: { flex: 1 },
  title: { fontSize: 17, fontWeight: '800', color: palette.ink },
  totalCard: { backgroundColor: palette.sunken },
  ratio: { fontSize: 14, fontWeight: '700', color: palette.ink },
  ratioBig: { fontSize: 22, fontWeight: '800', color: palette.ink },
  counts: { gap: 4 },
  countRow: { flexDirection: 'row', justifyContent: 'space-between', borderBottomWidth: 1, borderBottomColor: palette.line, paddingBottom: 4 },
  countValue: { fontSize: 15, fontWeight: '800', color: palette.ink },
  countWarn: { color: palette.warning },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
});
