import { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import { listCommands, type CommandRow } from '../data/command-queue';
import { dismissCommand, enqueueCommand } from '../data/outbox';
import type { Session } from '../data/session';
import {
  readPeople,
  readPlans,
  readReminderPlans,
  readStock,
  type MedicationStock,
  type PersonRow,
  type PlanRow,
} from '../data/snapshot';
import { syncNow } from '../data/sync';
import { describeCommand } from '../lib/commands';
import { enumKey } from '../lib/i18n';
import { formatLocalDate, localDate } from '../lib/local-date';
import { STATUS_KEYS, describeSchedule, planStatus, type PlanStatus } from '../lib/plans';
import { formatQuantity } from '../lib/quantity';
import { reconcileReminders } from '../notifications/reminders';
import { startOfDayParts } from '../notifications/schedule';
import { DateSheet } from '../ui/DateSheet';
import { RefusedCommands } from '../ui/RefusedCommands';
import { Badge, Button, Card, Notice, SectionTitle, palette, useTranslate } from '../ui/theme';
import { AddPlanSheet } from './AddPlanSheet';

/**
 * Every plan the household has, grouped by person, with the schedule in words and
 * where it stands today — the web's plans screen, read from the cached snapshot.
 *
 * Adding, pausing, resuming, ending and restarting queue a command: the plan changes on
 * the phone at once, its reminders follow, and the server hears on the next sync. A plan
 * added here is provisional until then, and its card says so. Editing a plan's dose or
 * schedule still goes through the web, and the screen says so.
 */
export function PlansScreen({ session }: { session: Session }) {
  const { t, locale } = useTranslate();
  const db = useSQLiteContext();

  const [rows, setRows] = useState<PlanRow[]>([]);
  const [activePeople, setActivePeople] = useState<PersonRow[]>([]);
  const [medications, setMedications] = useState<MedicationStock[]>([]);
  const [adding, setAdding] = useState(false);
  const [queued, setQueued] = useState<CommandRow[]>([]);
  const [refused, setRefused] = useState<CommandRow[]>([]);
  const [decision, setDecision] = useState<{ kind: 'end' | 'restart'; row: PlanRow } | null>(null);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [offline, setOffline] = useState(false);

  const config = useMemo(
    () => ({ apiUrl: session.apiUrl, accessToken: session.accessToken }),
    [session.apiUrl, session.accessToken],
  );

  const reload = useCallback(async () => {
    setRows(await readPlans(db));
    setActivePeople((await readPeople(db)).filter((person) => !person.isArchived));
    setMedications(await readStock(db));
    setQueued((await listCommands(db, 'queued')).filter((row) => row.kind.startsWith('plan.')));
    setRefused((await listCommands(db, 'rejected')).filter((row) => row.kind.startsWith('plan.')));
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

  async function togglePause(row: PlanRow) {
    await enqueueCommand(
      db,
      { kind: 'plan.pause', targetId: row.id, medicationId: row.medicationId, body: { isPaused: !row.isPaused } },
      new Date().toISOString(),
    );

    // The phone's own reminders follow the decision now, not after the next sync.
    await reconcileReminders(db, await readReminderPlans(db), t, new Date());
    await reload();
    void refresh();
  }

  /** The last day, or the first day again: a version appended on the server, a date here. */
  async function decide(row: PlanRow, kind: 'end' | 'restart', day: string) {
    await enqueueCommand(
      db,
      kind === 'end'
        ? { kind: 'plan.end', targetId: row.id, medicationId: row.medicationId, body: { endsOn: day } }
        : { kind: 'plan.restart', targetId: row.id, medicationId: row.medicationId, body: { startsOn: day } },
      new Date().toISOString(),
    );

    setDecision(null);
    await reconcileReminders(db, await readReminderPlans(db), t, new Date());
    await reload();
    void refresh();
  }

  /** A plan added on the phone: its reminders are set now, like any other plan's. */
  async function afterAdded() {
    setAdding(false);
    await reconcileReminders(db, await readReminderPlans(db), t, new Date());
    await reload();
    void refresh();
  }

  async function dismiss(idempotencyKey: string) {
    await dismissCommand(db, idempotencyKey);
    await reload();
  }

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

  const pendingFor = (planId: string) => queued.filter((row) => row.targetId === planId);

  return (
    <>
    <ScrollView
      contentContainerStyle={styles.container}
      refreshControl={<RefreshControl refreshing={syncing} onRefresh={() => void refresh()} />}
    >
      <SectionTitle>{t('plans')}</SectionTitle>
      <Text style={styles.muted}>{t('otherEditsOnWeb')}</Text>
      {offline ? <Notice tone="warning" message={t('offline')} /> : null}

      <RefusedCommands rows={refused} onDismiss={(key) => void dismiss(key)} />

      <Button label={t('addPlan')} onPress={() => setAdding(true)} />

      {rows.length === 0 ? (
        <Card>
          <Text style={styles.muted}>{t('plansEmpty')}</Text>
        </Card>
      ) : null}

      {[...people.entries()].map(([personName, entries]) => (
        <View key={personName} style={styles.group}>
          <Text style={styles.person}>{personName}</Text>
          {entries.map(({ row, status }) => (
            <PlanCard
              key={row.versionId}
              row={row}
              status={status}
              locale={locale}
              pending={pendingFor(row.id)}
              onTogglePause={() => void togglePause(row)}
              onEnd={() => setDecision({ kind: 'end', row })}
            />
          ))}
        </View>
      ))}

      {past.length > 0 ? (
        <View style={styles.group}>
          <Text style={styles.person}>{t('pastPlans')}</Text>
          {past.map(({ row, status }) => (
            <PlanCard
              key={row.versionId}
              row={row}
              status={status}
              locale={locale}
              pending={pendingFor(row.id)}
              onRestart={() => setDecision({ kind: 'restart', row })}
            />
          ))}
        </View>
      ) : null}
    </ScrollView>

    {decision ? (
      <DateSheet
        title={`${t(decision.kind === 'end' ? 'endPlan' : 'restartPlan')} — ${decision.row.medicationName}`}
        label={t(decision.kind === 'end' ? 'lastDayLabel' : 'restartsOn')}
        hint={t(decision.kind === 'end' ? 'endPlanHint' : 'restartPlanHint')}
        confirmLabel={t(decision.kind === 'end' ? 'endPlan' : 'restartPlan')}
        onClose={() => setDecision(null)}
        onConfirm={(day) => decide(decision.row, decision.kind, day)}
      />
    ) : null}

    {adding ? (
      <AddPlanSheet
        people={activePeople}
        medications={medications}
        onClose={() => setAdding(false)}
        onQueued={() => void afterAdded()}
      />
    ) : null}
    </>
  );
}

function PlanCard({
  row,
  status,
  locale,
  pending,
  onTogglePause,
  onEnd,
  onRestart,
}: {
  row: PlanRow;
  status: PlanStatus;
  locale: string;
  pending: CommandRow[];
  onTogglePause?: () => void;
  onEnd?: () => void;
  onRestart?: () => void;
}) {
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

      {pending.length > 0 ? (
        <View style={styles.badges}>
          {pending.map((command) => (
            <Badge
              key={command.idempotencyKey}
              tone="warning"
              label={`${describeCommand(command.kind, JSON.parse(command.payload), t)} · ${t('queuedCommand')}`}
            />
          ))}
        </View>
      ) : null}

      {row.versionId.startsWith('pending:') ? <Text style={styles.muted}>{t('pendingPlanHint')}</Text> : null}

      {status !== 'ended' ? (
        <View style={styles.badges}>
          {onTogglePause ? (
            <Button tone="secondary" label={t(row.isPaused ? 'resumePlan' : 'pausePlan')} onPress={onTogglePause} />
          ) : null}
          {onEnd ? <Button tone="danger" label={t('endPlan')} onPress={onEnd} /> : null}
        </View>
      ) : onRestart ? (
        <Button tone="secondary" label={t('restartPlan')} onPress={onRestart} />
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
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  ended: { opacity: 0.7 },
});
