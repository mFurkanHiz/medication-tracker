import { useCallback, useEffect, useMemo, useState } from 'react';
import { Alert, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import { listCommands, type CommandRow } from '../data/command-queue';
import { dismissCommand, enqueueCommand } from '../data/outbox';
import type { Session } from '../data/session';
import { readPeople, readStockWithPackages, type PersonRow, type StockPackage, type StockRow } from '../data/snapshot';
import { syncNow } from '../data/sync';
import { describeCommand } from '../lib/commands';
import { formatLocalDate, localDate } from '../lib/local-date';
import type { MessageKey } from '../lib/i18n';
import { formatQuantity, isPositive } from '../lib/quantity';
import { RefusedCommands } from '../ui/RefusedCommands';
import { Badge, Button, Card, Notice, SectionTitle, palette, useTranslate } from '../ui/theme';
import { AddStockSheet } from './AddStockSheet';
import { CountingSheet } from './CountingSheet';
import { EditPackageSheet } from './EditPackageSheet';

/**
 * What the household has, box by box — the same picture the web's medications screen
 * gives, read from the cached snapshot so it renders with no network at all.
 *
 * Every box decision the web offers is here, queued as a command: the phone shows the
 * decision at once and the server hears on the next sync. Added stock has no box until
 * the server names it, so it waits as a pending line under the medicine; a loan has no id
 * until the server gives one, so a box lent from the phone cannot be returned from the
 * phone until that sync. Counting opens its own sheet.
 */
export function StockScreen({ session }: { session: Session }) {
  const { t, locale } = useTranslate();
  const db = useSQLiteContext();

  const [rows, setRows] = useState<StockRow[]>([]);
  const [people, setPeople] = useState<PersonRow[]>([]);
  const [queued, setQueued] = useState<CommandRow[]>([]);
  const [refused, setRefused] = useState<CommandRow[]>([]);
  const [adding, setAdding] = useState<StockRow | null>(null);
  const [editing, setEditing] = useState<{ row: StockRow; box: StockPackage } | null>(null);
  const [counting, setCounting] = useState(false);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [offline, setOffline] = useState(false);

  const config = useMemo(
    () => ({ apiUrl: session.apiUrl, accessToken: session.accessToken }),
    [session.apiUrl, session.accessToken],
  );

  const isStockCommand = (row: CommandRow) => !row.kind.startsWith('plan.');

  const reload = useCallback(async () => {
    setRows(await readStockWithPackages(db));
    setPeople((await readPeople(db)).filter((person) => !person.isArchived));
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

  async function afterQueued() {
    await reload();
    void refresh();
  }

  /** One box command, queued and shown. */
  async function queueBox(
    row: StockRow,
    box: StockPackage,
    kind: 'package.pin' | 'package.unpin' | 'package.reinstate',
  ) {
    await enqueueCommand(db, { kind, targetId: box.id, medicationId: row.id, body: {} }, new Date().toISOString());
    await afterQueued();
  }

  async function assign(row: StockRow, box: StockPackage, personId: string | null) {
    await enqueueCommand(
      db,
      { kind: 'package.assign', targetId: box.id, medicationId: row.id, body: { personId } },
      new Date().toISOString(),
    );
    await afterQueued();
  }

  async function lend(row: StockRow, box: StockPackage, borrowerPersonId: string) {
    await enqueueCommand(
      db,
      { kind: 'package.lend', targetId: box.id, medicationId: row.id, body: { borrowerPersonId } },
      new Date().toISOString(),
    );
    await afterQueued();
  }

  async function returnLoan(row: StockRow, box: StockPackage) {
    if (!box.activeLoanId || box.activeLoanId === 'pending') {
      return;
    }

    await enqueueCommand(
      db,
      { kind: 'loan.return', targetId: box.activeLoanId, medicationId: row.id, body: { packageId: box.id } },
      new Date().toISOString(),
    );
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
              people={people}
              queued={queued.filter((command) => command.medicationId === row.id)}
              onAddStock={() => setAdding(row)}
              onBox={{
                pin: (box) => void queueBox(row, box, 'package.pin'),
                unpin: (box) => void queueBox(row, box, 'package.unpin'),
                reinstate: (box) => void queueBox(row, box, 'package.reinstate'),
                edit: (box) => setEditing({ row, box }),
                assign: (box, personId) => void assign(row, box, personId),
                lend: (box, borrower) => void lend(row, box, borrower),
                returnLoan: (box) => void returnLoan(row, box),
                retire: (box, state) => retire(row, box, state),
              }}
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

      {editing ? (
        <EditPackageSheet
          box={editing.box}
          medicationId={editing.row.id}
          onClose={() => setEditing(null)}
          onQueued={() => {
            setEditing(null);
            void afterQueued();
          }}
        />
      ) : null}
    </>
  );
}

/** Everything a box can be asked to do, handed down from the screen that queues it. */
type BoxActions = {
  pin: (box: StockPackage) => void;
  unpin: (box: StockPackage) => void;
  reinstate: (box: StockPackage) => void;
  edit: (box: StockPackage) => void;
  assign: (box: StockPackage, personId: string | null) => void;
  lend: (box: StockPackage, borrowerPersonId: string) => void;
  returnLoan: (box: StockPackage) => void;
  retire: (box: StockPackage, state: 'Lost' | 'Disposed') => void;
};

function MedicationCard({
  row,
  locale,
  people,
  queued,
  onAddStock,
  onBox,
}: {
  row: StockRow;
  locale: string;
  people: PersonRow[];
  queued: CommandRow[];
  onAddStock: () => void;
  onBox: BoxActions;
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
          people={people}
          pending={queued.filter((command) => command.targetId === box.id || command.targetId === box.activeLoanId)}
          onBox={onBox}
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
  people,
  pending,
  onBox,
}: {
  box: StockPackage;
  unit: string;
  medicationCoverage: string | null;
  locale: string;
  people: PersonRow[];
  pending: CommandRow[];
  onBox: BoxActions;
}) {
  const { t } = useTranslate();
  const [showOther, setShowOther] = useState(false);
  const stateKey = STATE_KEYS[box.state];
  const retired = box.state === 'Disposed' || box.state === 'Lost';
  const available = box.state === 'Sealed' || box.state === 'Opened';
  const onLoan = box.activeLoanId !== null;
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
        {box.activeLoanId === 'pending' ? (
          <Badge tone="warning" label={t('loanPending')} />
        ) : onLoan ? (
          <Badge tone="accent" label={t('onLoan')} />
        ) : null}
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
      {box.ownerName ? (
        <Text style={styles.muted}>
          {t('owner')}: {box.ownerName}
        </Text>
      ) : null}
      {box.holderName && box.holderName !== box.ownerName ? (
        <Text style={styles.muted}>
          {t('holder')}: {box.holderName}
        </Text>
      ) : null}
      {box.storageLocation ? (
        <Text style={styles.muted}>
          {t('storageLocation')}: {box.storageLocation}
        </Text>
      ) : null}

      {available ? (
        <View style={styles.actions}>
          {box.isPinned ? (
            <Button tone="quiet" label={t('unpin')} onPress={() => onBox.unpin(box)} />
          ) : (
            <Button tone="quiet" label={t('makeActive')} onPress={() => onBox.pin(box)} />
          )}
          <Button
            tone="quiet"
            label={t('otherActions')}
            accessibilityState={{ expanded: showOther }}
            onPress={() => setShowOther((value) => !value)}
          />
        </View>
      ) : null}

      {available && showOther ? (
        // The rarer decisions sit behind a disclosure rather than beside the everyday
        // ones — the web's reasoning, kept. Lost and disposed last, and hard to undo.
        <View style={styles.other}>
          <Button tone="secondary" label={t('editPackage')} onPress={() => onBox.edit(box)} />

          {!onLoan ? (
            <View style={styles.picker}>
              <Text style={styles.pickerTitle}>{t('assignWho')}</Text>
              <View style={styles.actions}>
                {people.map((person) => (
                  <Button
                    key={person.id}
                    tone={box.ownerPersonId === person.id ? 'primary' : 'secondary'}
                    label={person.name}
                    accessibilityState={{ selected: box.ownerPersonId === person.id }}
                    onPress={() => onBox.assign(box, person.id)}
                    style={styles.small}
                  />
                ))}
                <Button
                  tone={box.ownerPersonId === null ? 'primary' : 'secondary'}
                  label={t('noOwner')}
                  accessibilityState={{ selected: box.ownerPersonId === null }}
                  onPress={() => onBox.assign(box, null)}
                  style={styles.small}
                />
              </View>
            </View>
          ) : null}

          {box.ownerPersonId && !onLoan && people.some((person) => person.id !== box.ownerPersonId) ? (
            <View style={styles.picker}>
              <Text style={styles.pickerTitle}>{t('lendTo')}</Text>
              <View style={styles.actions}>
                {people
                  .filter((person) => person.id !== box.ownerPersonId)
                  .map((person) => (
                    <Button
                      key={person.id}
                      tone="secondary"
                      label={person.name}
                      onPress={() => onBox.lend(box, person.id)}
                      style={styles.small}
                    />
                  ))}
              </View>
            </View>
          ) : null}

          {onLoan && box.activeLoanId !== 'pending' ? (
            <Button tone="secondary" label={t('returnLoan')} onPress={() => onBox.returnLoan(box)} />
          ) : null}

          <View style={styles.actions}>
            <Button tone="danger" label={t('markLost')} onPress={() => onBox.retire(box, 'Lost')} />
            <Button tone="danger" label={t('markDisposed')} onPress={() => onBox.retire(box, 'Disposed')} />
          </View>
        </View>
      ) : null}

      {retired ? (
        <View style={styles.other}>
          <Button tone="secondary" label={t('reinstatePackage')} onPress={() => onBox.reinstate(box)} />
          <Text style={styles.muted}>{t('reinstateHint')}</Text>
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
  other: { gap: 10, paddingTop: 4 },
  picker: { gap: 6 },
  pickerTitle: { fontSize: 13, fontWeight: '700', color: palette.inkMuted },
  small: { minHeight: 40, paddingHorizontal: 12 },
  pendingRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  pendingText: { flex: 1, fontSize: 14, fontWeight: '700', color: palette.ink },
});
