import * as Notifications from 'expo-notifications';
import { Platform } from 'react-native';
import type { SQLiteDatabase } from 'expo-sqlite';
import { META, readMeta, writeMeta } from '../data/database';
import type { Translate } from '../lib/i18n';
import {
  canRepeatOnDevice,
  expoWeekdays,
  instantForWallClock,
  parseLocalTime,
  resolveDeviceZone,
  startOfDayParts,
  upcomingDueDays,
  type DueRule,
  type RecurrencePattern,
} from './schedule';

/**
 * Device-local medication reminders.
 *
 * What the installed `expo-notifications` (57.0.21) handles for us, verified from its
 * Android manifest rather than assumed: it declares `POST_NOTIFICATIONS` and
 * `RECEIVE_BOOT_COMPLETED` and registers a receiver for `BOOT_COMPLETED` and
 * `MY_PACKAGE_REPLACED`, so **scheduled notifications survive a reboot and an app
 * update** without the app doing anything.
 *
 * What it does **not** handle, and this module therefore must:
 *  - **Time-zone changes.** There is no `TIMEZONE_CHANGED` receiver. A repeating
 *    daily trigger fires on the device's wall clock, so moving the phone to another
 *    zone silently shifts every reminder. Reconciliation runs on every foreground and
 *    rebuilds the schedule when the device zone has changed.
 *  - **Plan changes.** A plan edited on another device arrives through sync; the
 *    schedule is rebuilt when the plan signature changes.
 *  - **A version's dates.** A repeating trigger never stops, so only a started,
 *    open-ended daily or weekly version gets one. A version with an end date, a future
 *    start, or a monthly or every-N-days pattern is scheduled as exact instants, which
 *    do stop — a plan the household ended on the web must not keep reminding here.
 *
 * The days themselves come from `./schedule`, which mirrors the server's rule and is
 * unit-tested; nothing in this file decides whether a day is due.
 *
 * Two deliberate limitations, stated rather than hidden:
 *  - The library does not request `SCHEDULE_EXACT_ALARM`, so Android may defer a
 *    reminder under Doze. These are adherence reminders, not alarms.
 *  - A plan with only a named period and no clock time gets **no** reminder. The
 *    product refuses to invent an hour for "evening", and a reminder would have to.
 */

export const CHANNEL_ID = 'medication-reminders';

/**
 * Without a handler a triggered notification is not presented at all while the app is
 * in the foreground — the SDK 57 documentation is explicit about this. Registered once
 * at module load so no screen can forget it.
 */
Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowBanner: true,
    shouldShowList: true,
    // Left on deliberately: `shouldPlaySound: false` suppresses the heads-up banner
    // entirely on Android regardless of channel importance, which would turn a
    // medication reminder into something the user never sees.
    shouldPlaySound: true,
    shouldSetBadge: false,
  }),
});

export type ReminderPlan = {
  planVersionId: string;
  medicationName: string;
  personName: string;
  doseLabel: string;
  kind: 'Scheduled' | 'AsNeeded';
  pattern: RecurrencePattern;
  weekdayMask: number | null;
  intervalDays: number | null;
  dayOfMonth: number | null;
  intervalMonths: number | null;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  /** `HH:mm:ss`. Null means a named period only, which gets no reminder. */
  localTime: string | null;
  timeZoneId: string;
  /** Set aside by the household. A paused plan promises no doses, so it asks for none. */
  isPaused: boolean;
};

export type ReminderStatus = {
  permitted: boolean;
  scheduled: number;
  /** Plans skipped because they have no clock time to remind at. */
  skippedWithoutTime: number;
};

export { resolveDeviceZone } from './schedule';

export async function ensureChannelAsync(t: Translate): Promise<void> {
  if (Platform.OS !== 'android') {
    return;
  }

  // Android refuses to display a notification that has no channel.
  await Notifications.setNotificationChannelAsync(CHANNEL_ID, {
    name: t('reminderChannelName'),
    description: t('reminderChannelDescription'),
    importance: Notifications.AndroidImportance.HIGH,
    sound: 'default',
    enableVibrate: true,
  });
}

export async function hasPermissionAsync(): Promise<boolean> {
  const status = await Notifications.getPermissionsAsync();
  return status.granted || status.ios?.status === Notifications.IosAuthorizationStatus.PROVISIONAL;
}

export async function requestPermissionAsync(): Promise<boolean> {
  const status = await Notifications.requestPermissionsAsync();
  return status.granted;
}

/**
 * Brings the scheduled notifications in line with the current plans.
 *
 * Cheap when nothing changed: a signature over the plans plus the device time zone is
 * compared against what was last scheduled, and the whole thing is skipped if they
 * match, the OS still holds the expected number of notifications, and no instant-based
 * plan has run out of scheduled occurrences.
 */
export async function reconcileReminders(
  db: SQLiteDatabase,
  plans: readonly ReminderPlan[],
  t: Translate,
  now: Date,
): Promise<ReminderStatus> {
  const permitted = await hasPermissionAsync();
  if (!permitted) {
    // Without permission there is nothing to schedule. Any stale schedule from a
    // previous grant is cleared so the device does not keep firing after a revoke.
    await clearOurs(db);
    return { permitted: false, scheduled: 0, skippedWithoutTime: 0 };
  }

  await ensureChannelAsync(t);

  const deviceZone = resolveDeviceZone();
  const signature = signatureFor(plans);
  const previousSignature = await readMeta(db, META.reminderSignature);
  const previousZone = await readMeta(db, META.reminderTimeZone);

  const known = await db.getAllAsync<{ notificationId: string }>(
    'SELECT notification_id AS notificationId FROM reminders',
  );

  if (signature === previousSignature && deviceZone === previousZone) {
    const live = await Notifications.getAllScheduledNotificationsAsync();
    const liveIds = new Set(live.map((request) => request.identifier));
    const stillScheduled = known.filter((row) => liveIds.has(row.notificationId));

    // The OS still holds everything we expect, and nothing about the plans or the
    // device's zone has changed. Leave it alone — rescheduling would churn the
    // notification list for no reason.
    if (stillScheduled.length === known.length && !(await needsTopUp(db, plans, deviceZone, now))) {
      return {
        permitted: true,
        scheduled: known.length,
        skippedWithoutTime: plans.filter(withoutTime).length,
      };
    }
  }

  await clearOurs(db);

  let scheduled = 0;
  let skippedWithoutTime = 0;

  for (const plan of plans) {
    if (!schedulable(plan)) {
      continue;
    }

    if (withoutTime(plan)) {
      skippedWithoutTime += 1;
      continue;
    }

    const [hour, minute] = parseLocalTime(plan.localTime!);
    const content = {
      title: t('reminderTitle'),
      body: `${t('reminderBodyPrefix')} ${plan.medicationName} · ${plan.doseLabel} · ${plan.personName}`,
      data: { planVersionId: plan.planVersionId },
    };

    const rule = ruleOf(plan);

    // A repeating OS trigger is only correct when the plan's zone is the device's — the
    // OS fires on device wall-clock — and when the version itself repeats without end.
    if (plan.timeZoneId === deviceZone && canRepeatOnDevice(rule, startOfDayParts(now, plan.timeZoneId))) {
      if (plan.pattern === 'Daily') {
        scheduled += await schedule(db, plan, content, 'daily', signature, null, {
          type: Notifications.SchedulableTriggerInputTypes.DAILY,
          channelId: CHANNEL_ID,
          hour,
          minute,
        });
        continue;
      }

      if (plan.pattern === 'SelectedWeekdays' && plan.weekdayMask !== null) {
        for (const weekday of expoWeekdays(plan.weekdayMask)) {
          scheduled += await schedule(db, plan, content, 'weekly', signature, null, {
            type: Notifications.SchedulableTriggerInputTypes.WEEKLY,
            channelId: CHANNEL_ID,
            weekday,
            hour,
            minute,
          });
        }
        continue;
      }
    }

    // Everything else — every-N-days, the monthly patterns, a version with an end or a
    // future start, or a plan anchored to another zone — is a bounded run of exact
    // instants, topped up on later reconciles.
    for (const instant of upcomingInstants(rule, plan.timeZoneId, hour, minute, now)) {
      scheduled += await schedule(db, plan, content, 'date', signature, instant, {
        type: Notifications.SchedulableTriggerInputTypes.DATE,
        channelId: CHANNEL_ID,
        date: instant,
      });
    }
  }

  await writeMeta(db, META.reminderSignature, signature);
  await writeMeta(db, META.reminderTimeZone, deviceZone);

  return { permitted: true, scheduled, skippedWithoutTime };
}

/** Cancels every notification this app scheduled, and forgets them. */
export async function clearOurs(db: SQLiteDatabase): Promise<void> {
  const rows = await db.getAllAsync<{ notificationId: string }>(
    'SELECT notification_id AS notificationId FROM reminders',
  );

  for (const row of rows) {
    // A notification the OS already dropped is not an error worth failing over.
    await Notifications.cancelScheduledNotificationAsync(row.notificationId).catch(() => undefined);
  }

  await db.execAsync('DELETE FROM reminders;');
}

async function schedule(
  db: SQLiteDatabase,
  plan: ReminderPlan,
  content: { title: string; body: string; data: Record<string, unknown> },
  kind: 'daily' | 'weekly' | 'date',
  signature: string,
  firesAt: Date | null,
  trigger: Notifications.NotificationTriggerInput,
): Promise<number> {
  try {
    const identifier = await Notifications.scheduleNotificationAsync({ content, trigger });

    await db.runAsync(
      'INSERT INTO reminders (notification_id, plan_version_id, trigger_kind, fires_at, signature) VALUES (?, ?, ?, ?, ?)',
      identifier,
      plan.planVersionId,
      kind,
      firesAt ? firesAt.toISOString() : null,
      signature,
    );

    return 1;
  } catch {
    // One plan failing to schedule must not stop the rest; the next reconcile retries.
    return 0;
  }
}

/**
 * Whether an instant-based plan still asks for days it has no notification for.
 *
 * A bounded run of instants is exhausted either because the last of them fired — in
 * which case the OS list is already shorter than ours and the caller rebuilds — or
 * because none were scheduled yet. This catches the second case without rebuilding
 * every foreground for a version that has simply ended.
 */
async function needsTopUp(
  db: SQLiteDatabase,
  plans: readonly ReminderPlan[],
  deviceZone: string,
  now: Date,
): Promise<boolean> {
  const pending = await db.getAllAsync<{ planVersionId: string; count: number }>(
    "SELECT plan_version_id AS planVersionId, count(*) AS count FROM reminders WHERE trigger_kind = 'date' AND fires_at > ? GROUP BY plan_version_id",
    now.toISOString(),
  );
  const pendingByPlan = new Map(pending.map((row) => [row.planVersionId, row.count]));

  return plans.some((plan) => {
    if (!schedulable(plan) || withoutTime(plan)) {
      return false;
    }

    const rule = ruleOf(plan);

    if (plan.timeZoneId === deviceZone && canRepeatOnDevice(rule, startOfDayParts(now, plan.timeZoneId))) {
      return false;
    }

    if ((pendingByPlan.get(plan.planVersionId) ?? 0) > 0) {
      return false;
    }

    const [hour, minute] = parseLocalTime(plan.localTime!);
    return upcomingInstants(rule, plan.timeZoneId, hour, minute, now).length > 0;
  });
}

/**
 * Whether this plan should hold notifications at all.
 *
 * Used by BOTH the scheduling loop and {@link signatureFor}, deliberately. The signature
 * decides whether to reconcile at all, so if the two disagreed about a plan the schedule
 * could never be corrected: pausing a plan would leave the signature unchanged, the
 * reconcile would early-return, and the phone would keep reminding for a plan the
 * household had put down. One predicate means they cannot drift apart.
 */
function schedulable(plan: ReminderPlan): boolean {
  return plan.kind === 'Scheduled' && !plan.isPaused;
}

function withoutTime(plan: ReminderPlan): boolean {
  return plan.kind === 'Scheduled' && !plan.localTime;
}

function ruleOf(plan: ReminderPlan): DueRule {
  return {
    kind: plan.kind,
    pattern: plan.pattern,
    weekdayMask: plan.weekdayMask,
    intervalDays: plan.intervalDays,
    dayOfMonth: plan.dayOfMonth,
    intervalMonths: plan.intervalMonths,
    effectiveFrom: plan.effectiveFrom,
    effectiveTo: plan.effectiveTo,
    isPaused: plan.isPaused,
  };
}

/** The next instants a version is due at, in the future only, bounded by the horizon. */
function upcomingInstants(rule: DueRule, zone: string, hour: number, minute: number, now: Date): Date[] {
  return upcomingDueDays(rule, startOfDayParts(now, zone))
    .map((day) => instantForWallClock(day, hour, minute, zone))
    .filter((instant) => instant.getTime() > now.getTime());
}

/**
 * A stable fingerprint of everything that determines the schedule, so an unchanged
 * plan set costs one string comparison instead of a full reschedule.
 */
function signatureFor(plans: readonly ReminderPlan[]): string {
  return plans
    .filter(schedulable)
    .map((plan) =>
      [
        plan.planVersionId,
        plan.pattern,
        plan.weekdayMask ?? '',
        plan.intervalDays ?? '',
        plan.dayOfMonth ?? '',
        plan.intervalMonths ?? '',
        plan.localTime ?? '',
        plan.effectiveFrom ?? '',
        plan.effectiveTo ?? '',
        plan.timeZoneId,
      ].join(':'),
    )
    .sort()
    .join('|');
}
