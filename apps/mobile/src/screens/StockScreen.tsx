import { useCallback, useEffect, useMemo, useState } from 'react';
import { Alert, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import { listCommands, type CommandRow } from '../data/command-queue';
import { dismissCommand, enqueueCommand } from '../data/outbox';
import type { Session } from '../data/session';
import { readStockWithPackages, type StockPackage, type StockRow } from '../data/snapshot';
import { syncNow } from '../data/sync';
import { describeCommand } from '../lib/commands';
import { formatLocalDate, localDate } from '../lib/local-date';
import type { MessageKey } from '../lib/i18n';
import { formatQuantity, isPositive } from '../lib/quantity';
import { RefusedCommands } from '../ui/RefusedCommands';
import { Badge, Button, Card, Notice, SectionTitle, palette, useTranslate } from '../ui/theme';
import { AddStockSheet } from './AddStockSheet';
import { CountingSheet } from './CountingSheet';

/**
 * What the household has, box by box — the same picture the web's medications screen
 * gives, read from the cached snapshot so it renders with no network at all.
 *
 * Adding stock, making a box the active one and marking a box lost or disposed queue a
 * command: the phone shows the decision at once and the server hears on the next sync.
 * Added stock has no box until the server names it, so it waits as a pending line under
 * the medicine. Everything else about a box still goes through the web.
 */
export function StockScreen({ session }: { session: Session }) {
  const { t, locale } = useTranslate();
  const db = useSQLiteContext();

  const [rows, setRows] = useState<StockRow[]>([]);
  const [queued, setQueued] = useState<CommandRow[]>([]);
  const [refused, setRefused] = useState<CommandRow[]>([]);
  const [adding, setAdding] = useState<StockRow | null>(null);
  const [counting, setCounting] = useState(false);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [offline, setOffline] = useState(false);

  const config = useMemo(
    () => ({ apiUrl: session.apiUrl, accessToken: session.accessToken }),
    [session.apiUrl, session.accessToken],
  );

  const isStockCommand = (row: CommandRow) => row.kind !== 'plan.pause';

  const reload = useCallback(async () => {
    setRows(await readStockWithPackages(db));
    setQueued((await listCommands(db, 'queued')).filter(isStockCommand));
    setRefused((await listCommands(db, 'rejected')).filter(isStockCommand));
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

  async function afterQueued(reloadFirst = true) {
    if (reloadFirst) {
      await reload();
    }
    void refresh();
  }

  async function pin(row: StockRow, box: StockPackage) {
    await enqueueCommand(db, { kind: 'package.pin', targetId: box.id, medicationId: row.id, body: {} }, new Date().toISOString());
    await afterQueued();
  }

  function retire(row: StockRow, box: StockPackage, state: 'Lost' | 'Disposed') {
    Alert.alert(t(state === 'Lost' ? 'markLost' : 'markDisposed'), t('retireConfirm'), [
      { text: t('cancel'), style: 'cancel' },
      {
        text: t('confirm'),
        style: 'destructive',
        onPress: () => {
          void enqueueCommand(
            db,
            { kind: 'package.retire', targetId: box.id, medicationId: row.id, body: { state } },
            new Date().toISOString(),
          ).then(() => afterQueued());
        },
      },
    ]);
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

  return (
    <>
      <ScrollView
        contentContainerStyle={styles.container}
        refreshControl={<RefreshControl refreshing={syncing} onRefresh={() => void refresh()} />}
      >
        <SectionTitle>{t('stock')}</SectionTitle>
        <Text style={styles.muted}>{t('otherEditsOnWeb')}</Text>
        {offline ? <Notice tone="warning" message={t('offline')} /> : null}

        <RefusedCommands rows={refused} onDismiss={(key) => void dismiss(key)} />

        {rows.length > 0 ? <Button tone="secondary" label={t('countNow')} onPress={() => setCounting(true)} /> : null}

        {queued
          .filter((command) => command.kind === 'inventory.count')
          .map((command) => (
            <Card key={command.idempotencyKey}>
              <View style={styles.pendingRow}>
                <Text style={styles.pendingText}>{describeCommand(command.kind, JSON.parse(command.payload), t)}</Text>
                <Badge tone="warning" label={t('queuedCommand')} />
              </View>
            </Card>
          ))}

        {rows.length === 0 ? (
          <Card>
            <Text style={styles.muted}>{t('stockEmpty')}</Text>
          </Card>
        ) : (
          rows.map((row) => (
            <MedicationCard
              key={row.id}
              row={row}
              locale={locale}
              queued={queued.filter((command) => command.medicationId === row.id)}
              onAddStock={() => setAdding(row)}
              onPin={(box) => void pin(row, box)}
              onRetire={(box, state) => retire(row, box, state)}
            />
          ))
        )}
      </ScrollView>

      {counting ? (
        <CountingSheet
          rows={rows}
          householdId={session.householdId}
          onClose={() => setCounting(false)}
          onQueued={() => {
            setCounting(false);
            void afterQueued();
          }}
        />
      ) : null}

      {adding ? (
        <AddStockSheet
          row={adding}
          onClose={() => setAdding(null)}
          onQueued={() => {
            setAdding(null);
            void afterQueued();
          }}
        />
      ) : null}
    </>
  );
}

function MedicationCard({
  row,
  locale,
  queued,
  onAddStock,
  onPin,
  onRetire,
}: {
  row: StockRow;
  locale: string;
  queued: CommandRow[];
  onAddStock: () => void;
  onPin: (box: StockPackage) => void;
  onRetire: (box: StockPackage, state: 'Lost' | 'Disposed') => void;
}) {
  const { t } = useTranslate();
  const unit = row.unit.toLowerCase();
  const pendingStock = queued.filter((command) => command.kind === 'stock.add');

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
        <PackageRow
          key={box.id}
          box={box}
          unit={unit}
          medicationCoverage={row.coverage}
          locale={locale}
          pending={queued.filter((command) => command.targetId === box.id)}
          onPin={() => onPin(box)}
          onRetire={(state) => onRetire(box, state)}
        />
      ))}

      {isPositive(row.loose) ? (
        <Text style={styles.muted}>
          {t('looseStock')}: {formatQuantity(row.loose)} {unit}
        </Text>
      ) : null}

      {pendingStock.map((command) => (
        <View key={command.idempotencyKey} style={styles.pendingRow}>
          <Text style={styles.pendingText}>{describeCommand(command.kind, JSON.parse(command.payload), t)}</Text>
          <Badge tone="warning" label={t('queuedCommand')} />
        </View>
      ))}

      <Button tone="secondary" label={t('addStock')} onPress={onAddStock} />
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
  pending,
  onPin,
  onRetire,
}: {
  box: StockPackage;
  unit: string;
  medicationCoverage: string | null;
  locale: string;
  pending: CommandRow[];
  onPin: () => void;
  onRetire: (state: 'Lost' | 'Disposed') => void;
}) {
  const { t } = useTranslate();
  const [showOther, setShowOther] = useState(false);
  const stateKey = STATE_KEYS[box.state];
  const retired = box.state === 'Disposed' || box.state === 'Lost';
  const available = box.state === 'Sealed' || box.state === 'Opened';
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
        {pending.map((command) => (
          <Badge
            key={command.idempotencyKey}
            tone="warning"
            label={`${describeCommand(command.kind, JSON.parse(command.payload), t)} · ${t('queuedCommand')}`}
          />
        ))}
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

      {available ? (
        <View style={styles.actions}>
          {!box.isPinned ? <Button tone="quiet" label={t('makeActive')} onPress={onPin} /> : null}
          <Button
            tone="quiet"
            label={t('otherActions')}
            accessibilityState={{ expanded: showOther }}
            onPress={() => setShowOther((value) => !value)}
          />
        </View>
      ) : null}

      {available && showOther ? (
        // Lost and disposed are the rare, hard-to-undo actions, so they sit behind a
        // disclosure rather than beside the everyday ones — the web's reasoning, kept.
        <View style={styles.actions}>
          <Button tone="danger" label={t('markLost')} onPress={() => onRetire('Lost')} />
          <Button tone="danger" label={t('markDisposed')} onPress={() => onRetire('Disposed')} />
        </View>
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
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  pendingRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  pendingText: { flex: 1, fontSize: 14, fontWeight: '700', color: palette.ink },
});
