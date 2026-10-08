import { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import type { Session } from '../data/session';
import { readStockWithPackages, type StockPackage, type StockRow } from '../data/snapshot';
import { syncNow } from '../data/sync';
import { formatLocalDate, localDate } from '../lib/local-date';
import type { MessageKey } from '../lib/i18n';
import { formatQuantity, isPositive } from '../lib/quantity';
import { Badge, Card, Notice, SectionTitle, palette, useTranslate } from '../ui/theme';

/**
 * What the household has, box by box — the same picture the web's medications screen
 * gives, read from the cached snapshot so it renders with no network at all.
 *
 * Read-only on purpose: adding stock, pinning a box or marking one lost changes the
 * inventory ledger, and those commands go through the web until the phone's outbox
 * carries them. The screen says so rather than hiding the buttons quietly.
 */
export function StockScreen({ session }: { session: Session }) {
  const { t, locale } = useTranslate();
  const db = useSQLiteContext();

  const [rows, setRows] = useState<StockRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [offline, setOffline] = useState(false);

  const config = useMemo(
    () => ({ apiUrl: session.apiUrl, accessToken: session.accessToken }),
    [session.apiUrl, session.accessToken],
  );

  const reload = useCallback(async () => {
    setRows(await readStockWithPackages(db));
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

  return (
    <ScrollView
      contentContainerStyle={styles.container}
      refreshControl={<RefreshControl refreshing={syncing} onRefresh={() => void refresh()} />}
    >
      <SectionTitle>{t('stock')}</SectionTitle>
      <Text style={styles.muted}>{t('editOnWeb')}</Text>
      {offline ? <Notice tone="warning" message={t('offline')} /> : null}

      {rows.length === 0 ? (
        <Card>
          <Text style={styles.muted}>{t('stockEmpty')}</Text>
        </Card>
      ) : (
        rows.map((row) => <MedicationCard key={row.id} row={row} locale={locale} />)
      )}
    </ScrollView>
  );
}

function MedicationCard({ row, locale }: { row: StockRow; locale: string }) {
  const { t } = useTranslate();
  const unit = row.unit.toLowerCase();

  return (
    <Card>
      <View style={styles.titleRow}>
        <View style={styles.titleBody}>
          <Text style={styles.medication}>{row.name}</Text>
          {row.strength ? <Text style={styles.muted}>{row.strength}</Text> : null}
        </View>
        <Text style={styles.total}>
          {formatQuantity(row.total)} {unit}
        </Text>
      </View>

      <View style={styles.badges}>
        <Badge label={`${row.packageCount} ${t('packagesLabel')}`} />
        {row.coverage === 'SelfPaid' ? <Badge tone="accent" label={t('coverageSelfPaid')} /> : null}
        {!isPositive(row.total) ? <Badge tone="danger" label={t('outOfStock')} /> : null}
      </View>

      {row.packages.map((box) => (
        <PackageRow key={box.id} box={box} unit={unit} medicationCoverage={row.coverage} locale={locale} />
      ))}

      {isPositive(row.loose) ? (
        <Text style={styles.muted}>
          {t('looseStock')}: {formatQuantity(row.loose)} {unit}
        </Text>
      ) : null}
    </Card>
  );
}

const STATE_KEYS: Record<string, MessageKey> = {
  Sealed: 'stateSealed',
  Opened: 'stateOpened',
  Disposed: 'stateDisposed',
  Lost: 'stateLost',
  Archived: 'stateArchived',
};

function PackageRow({
  box,
  unit,
  medicationCoverage,
  locale,
}: {
  box: StockPackage;
  unit: string;
  medicationCoverage: string | null;
  locale: string;
}) {
  const { t } = useTranslate();
  const stateKey = STATE_KEYS[box.state];
  const retired = box.state === 'Disposed' || box.state === 'Lost';
  // A box's own coverage overrides the medicine's; loose stock follows the medicine.
  const coverage = box.coverage ?? medicationCoverage;

  return (
    <View style={[styles.box, retired ? styles.retired : null]}>
      <View style={styles.boxHeader}>
        <Text style={styles.boxName}>{box.label ?? `${t('packageOrdinal')} ${box.ordinal}`}</Text>
        <Text style={styles.boxAmount}>
          {formatQuantity(box.remaining)} / {formatQuantity(box.capacity)} {unit}
        </Text>
      </View>
      <View style={styles.badges}>
        {stateKey ? <Badge tone={retired ? 'danger' : box.state === 'Opened' ? 'accent' : 'neutral'} label={t(stateKey)} /> : null}
        {box.isPinned ? <Badge tone="positive" label={t('pinned')} /> : null}
        {coverage === 'SelfPaid' && box.coverage !== null ? <Badge tone="accent" label={t('coverageSelfPaid')} /> : null}
      </View>
      {box.expiresOn ? (
        <Text style={styles.muted}>
          {t('expiresOn')}: {formatLocalDate(box.expiresOn, locale)}
        </Text>
      ) : null}
      {box.holderName ? (
        <Text style={styles.muted}>
          {t('holder')}: {box.holderName}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { padding: 16, gap: 12, paddingBottom: 48 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
  titleRow: { flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'space-between', gap: 12 },
  titleBody: { flex: 1 },
  medication: { fontSize: 18, fontWeight: '800', color: palette.ink },
  total: { fontSize: 16, fontWeight: '800', color: palette.accentInk },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  box: { borderTopWidth: 1, borderTopColor: palette.line, paddingTop: 10, gap: 6 },
  retired: { opacity: 0.6 },
  boxHeader: { flexDirection: 'row', justifyContent: 'space-between', gap: 12 },
  boxName: { fontSize: 15, fontWeight: '700', color: palette.ink, flex: 1 },
  boxAmount: { fontSize: 15, fontWeight: '700', color: palette.ink },
});
