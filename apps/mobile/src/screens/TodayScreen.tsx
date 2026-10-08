import { useCallback, useEffect, useMemo, useState } from 'react';
import { AppState, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useSQLiteContext } from 'expo-sqlite';
import { META, readMeta } from '../data/database';
import {
  acknowledgeRejection,
  discardRejected,
  enqueueDose,
  openRejections,
  pendingCount,
  type Rejection,
} from '../data/outbox';
import { readReminderPlans, readToday, type LocalDueDose } from '../data/snapshot';
import type { Session } from '../data/session';
import { syncNow } from '../data/sync';
import { enumKey, errorKey } from '../lib/i18n';
import { formatMoment, localDate } from '../lib/local-date';
import { formatQuantity } from '../lib/quantity';
import {
  hasPermissionAsync,
  reconcileReminders,
  requestPermissionAsync,
} from '../notifications/reminders';
import { Badge, Button, Card, Notice, SectionTitle, palette, useTranslate } from '../ui/theme';
import { DoseDetailsSheet } from './DoseDetailsSheet';

/**
 * The daily screen.
 *
 * Everything on it comes from SQLite, so it renders with no network at all. Recording
 * a dose writes locally and queues; the network is an optimisation, never a
 * precondition.
 */
export function TodayScreen({ session, onSignedOut }: { session: Session; onSignedOut: () => void }) {
  const { t, locale } = useTranslate();
  const db = useSQLiteContext();

  const [doses, setDoses] = useState<LocalDueDose[]>([]);
  const [rejections, setRejections] = useState<Rejection[]>([]);
  const [pending, setPending] = useState(0);
  const [lastSynced, setLastSynced] = useState<string | null>(null);
  const [status, setStatus] = useState<{ tone: 'warning' | 'danger' | 'positive'; message: string } | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [loading, setLoading] = useState(true);
  const [reminderCount, setReminderCount] = useState<number | null>(null);
  const [details, setDetails] = useState<LocalDueDose | null>(null);

  const config = useMemo(
    () => ({ apiUrl: session.apiUrl, accessToken: session.accessToken }),
    [session.apiUrl, session.accessToken],
  );

  const reload = useCallback(async () => {
    const today = localDate();
    setDoses(await readToday(db, today));
    setRejections(await openRejections(db));
    setPending(await pendingCount(db));
    setLastSynced(await readMeta(db, META.lastSyncedAt));
    setLoading(false);
  }, [db]);

  const refreshReminders = useCallback(async () => {
    const plans = await readReminderPlans(db);
    const outcome = await reconcileReminders(db, plans, t, new Date());
    setReminderCount(outcome.permitted ? outcome.scheduled : null);
  }, [db, t]);

  /** Pushes, pulls, then brings the notification schedule back in line. */
  const sync = useCallback(async () => {
    setSyncing(true);

    try {
      const outcome = await syncNow(db, config, session.householdId, localDate(), new Date().toISOString());

      if (outcome.unauthenticated) {
        setStatus({ tone: 'danger', message: t('errorUnauthenticated') });
        onSignedOut();
        return;
      }

      setStatus(
        outcome.offline
          ? { tone: 'warning', message: t('offline') }
          : outcome.rejected > 0
            ? { tone: 'danger', message: t('rejectedTitle') }
            : outcome.pending === 0
              ? { tone: 'positive', message: t('allSynced') }
              : null,
      );

      await reload();
      await refreshReminders();
    } finally {
      setSyncing(false);
    }
  }, [db, config, session.householdId, t, onSignedOut, reload, refreshReminders]);

  useEffect(() => {
    void reload().then(() => sync());
  }, [reload, sync]);

  // Coming back to the foreground is the moment to drain the queue and, just as
  // importantly, to notice that the device moved to another time zone — the
  // notification library has no receiver for that.
  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state) => {
      if (state === 'active') {
        void sync();
      }
    });

    return () => subscription.remove();
  }, [sync]);

  async function record(dose: LocalDueDose, outcome: 'Taken' | 'Skipped') {
    const now = new Date().toISOString();

    await enqueueDose(
      db,
      {
        planVersionId: dose.planVersionId,
        personId: dose.personId,
        medicationDefinitionId: dose.medicationId,
        outcome,
        source: 'Automatic',
        packageId: null,
        quantity: outcome === 'Skipped' ? null : dose.dose,
        scheduledFor: dose.scheduledFor,
        occurredAt: now,
        note: null,
      },
      now,
    );

    // The record exists the moment this returns. Syncing is best-effort.
    setStatus({ tone: 'positive', message: t('queuedOffline') });
    await reload();
    void sync();
  }

  async function enableReminders() {
    const granted = (await hasPermissionAsync()) || (await requestPermissionAsync());

    if (!granted) {
      setStatus({ tone: 'warning', message: t('reminderPermissionDenied') });
      return;
    }

    await refreshReminders();
    setStatus({ tone: 'positive', message: t('reminderRescheduled') });
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
        refreshControl={<RefreshControl refreshing={syncing} onRefresh={() => void sync()} />}
      >
        <View style={styles.headerRow}>
          <SectionTitle>{t('today')}</SectionTitle>
          <Button tone="quiet" label={t('signOut')} onPress={onSignedOut} />
        </View>

        <Card>
          <View style={styles.statusRow}>
            <Text style={styles.muted}>
              {t('lastSynced')}: {lastSynced ? formatMoment(lastSynced, locale) : t('never')}
            </Text>
            {pending > 0 ? <Badge tone="warning" label={`${pending} ${t('pendingCount')}`} /> : null}
          </View>

          <View style={styles.statusRow}>
            <Text style={styles.muted}>
              {reminderCount === null
                ? t('remindersOff')
                : `${t('remindersOn')} · ${reminderCount} ${t('scheduledCount')}`}
            </Text>
            {reminderCount === null ? (
              <Button tone="secondary" label={t('enableReminders')} onPress={() => void enableReminders()} />
            ) : null}
          </View>

          <Button
            tone="secondary"
            label={syncing ? t('syncing') : t('syncNow')}
            disabled={syncing}
            onPress={() => void sync()}
          />
        </Card>

        {status ? <Notice tone={status.tone} message={status.message} /> : null}

        {rejections.map((rejection) => (
          <Card key={rejection.idempotencyKey} style={styles.conflictCard}>
            <Text style={styles.conflictTitle}>{t('rejectedTitle')}</Text>
            <Text style={styles.muted}>{t(errorKey(rejection.code))}</Text>
            <Text style={styles.muted}>{t('conflictHint')}</Text>
            <View style={styles.actions}>
              <Button
                tone="secondary"
                label={t('conflictKeepServer')}
                onPress={async () => {
                  await discardRejected(db, rejection.idempotencyKey, new Date().toISOString());
                  await reload();
                }}
              />
              <Button
                tone="quiet"
                label={t('close')}
                onPress={async () => {
                  await acknowledgeRejection(db, rejection.idempotencyKey, new Date().toISOString());
                  await reload();
                }}
              />
            </View>
          </Card>
        ))}

        {doses.length === 0 ? (
          <Card>
            <Text style={styles.muted}>{t('todayEmpty')}</Text>
            <Text style={styles.muted}>{t('todayEmptyHint')}</Text>
          </Card>
        ) : (
          doses.map((dose) => (
            <DoseRow
              key={`${dose.planVersionId}-${dose.scheduledFor ?? 'prn'}`}
              dose={dose}
              onTaken={() => void record(dose, 'Taken')}
              onSkipped={() => void record(dose, 'Skipped')}
              onDetails={() => setDetails(dose)}
            />
          ))
        )}

        <Text style={styles.footer}>{t('safetyNotice')}</Text>
      </ScrollView>

      {details ? (
        <DoseDetailsSheet
          dose={details}
          onClose={() => setDetails(null)}
          onRecorded={async () => {
            setDetails(null);
            setStatus({ tone: 'positive', message: t('queuedOffline') });
            await reload();
            void sync();
          }}
        />
      ) : null}
    </>
  );
}

function DoseRow({
  dose,
  onTaken,
  onSkipped,
  onDetails,
}: {
  dose: LocalDueDose;
  onTaken: () => void;
  onSkipped: () => void;
  onDetails: () => void;
}) {
  const { t } = useTranslate();
  const outcome = dose.localOutcome ?? dose.serverOutcome;
  const recorded = outcome !== null;
  const period = enumKey(dose.dayPeriod);
  const meal = enumKey(dose.mealRelation);

  return (
    <Card>
      <View style={styles.doseHeader}>
        {/* An as-needed dose always reads "Gerektiğinde", even when it carries a
            preferred part of the day: the big label is read first and must not turn a
            preference into an apparent appointment. A scheduled plan with a named period
            and no clock used to fall through to an em dash — the phone knew the period
            and showed nothing. */}
        <Text style={styles.time}>
          {dose.kind === 'AsNeeded'
            ? t('asNeeded')
            : dose.localTime
              ? dose.localTime.slice(0, 5)
              : period
                ? t(period)
                : '—'}
        </Text>
        <View style={styles.doseBody}>
          <Text style={styles.medication}>{dose.medicationName}</Text>
          {/* The server has always sent these and the phone has always cached them; it
              simply never showed them. A household member on the phone was the only one
              not told to take it on a full stomach. */}
          <Text style={styles.muted}>
            {dose.personName} · {formatQuantity(dose.dose)} {dose.unit.toLowerCase()}
            {dose.kind === 'AsNeeded' && period ? ` · ${t('preferably')} ${t(period)}` : ''}
            {meal ? ` · ${t(meal)}` : ''}
          </Text>
        </View>
      </View>

      <View style={styles.badges}>
        {dose.isConflicted ? <Badge tone="danger" label={t('conflictTitle')} /> : null}
        {recorded && !dose.isConflicted ? (
          <Badge
            tone="positive"
            label={outcome === 'Skipped' ? t('recordedSkipped') : t('recordedTaken')}
          />
        ) : null}
        {dose.isPending ? <Badge tone="warning" label={t('queuedOffline')} /> : null}
        {!recorded && !dose.hasEnoughStock ? <Badge tone="danger" label={t('notEnoughStock')} /> : null}
      </View>

      {dose.isConflicted ? <Text style={styles.muted}>{t('conflictHint')}</Text> : null}

      {/* The household's own "do not take with" tags, matched by the server among this
          person's doses today (ADR 0016). Read back in red with the words that matched
          and whose tag it was; the buttons stay — a warning is not a block. */}
      {dose.conflicts.map((conflict) => (
        <View key={`${conflict.notedOnMedicationDefinitionId}-${conflict.medicationDefinitionId}`} style={styles.warning}>
          <Text style={styles.warningTitle}>
            {t('doNotTakeWith').replace('{name}', conflict.medicationName)}
          </Text>
          <Text style={styles.warningDetail}>
            {t('doNotTakeWithReason')}: {conflict.matched.join(', ')}
          </Text>
          <Text style={styles.warningDetail}>
            {t('doNotTakeWithFrom').replace('{name}', conflict.notedOn)}
          </Text>
        </View>
      ))}

      <View style={styles.actions}>
        {recorded ? null : (
          <>
            <Button label={t('taken')} onPress={onTaken} />
            <Button tone="secondary" label={t('skip')} onPress={onSkipped} />
          </>
        )}
        <Button tone="quiet" label={t('details')} onPress={onDetails} />
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  container: { padding: 16, gap: 12, paddingBottom: 48 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  headerRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  statusRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 10 },
  doseHeader: { flexDirection: 'row', gap: 14, alignItems: 'flex-start' },
  doseBody: { flex: 1 },
  time: { fontSize: 20, fontWeight: '800', color: palette.accentInk, minWidth: 64 },
  medication: { fontSize: 18, fontWeight: '800', color: palette.ink },
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  conflictCard: { borderColor: palette.danger },
  conflictTitle: { fontSize: 16, fontWeight: '800', color: palette.danger },
  warning: { backgroundColor: palette.dangerSoft, borderRadius: 10, padding: 10, gap: 2 },
  warningTitle: { color: palette.danger, fontSize: 15, fontWeight: '800' },
  warningDetail: { color: palette.danger, fontSize: 13, lineHeight: 18 },
  footer: { color: palette.inkFaint, fontSize: 12, lineHeight: 18, marginTop: 8 },
});
