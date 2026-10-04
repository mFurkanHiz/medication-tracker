import * as Notifications from 'expo-notifications';
import { Platform } from 'react-native';
import type { SQLiteDatabase } from 'expo-sqlite';
import { META, readMeta, writeMeta } from '../data/database';
import type { Translate } from '../lib/i18n';

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

/** How many occurrences of an interval plan to schedule ahead, topped up on each reconcile. */
const INTERVAL_HORIZON = 14;

export type ReminderPlan = {
  planVersionId: string;
  medicationName: string;
  personName: string;
  doseLabel: string;
  kind: 'Scheduled' | 'AsNeeded';
  pattern: 'Daily' | 'SelectedWeekdays' | 'EveryNDays';
  weekdayMask: number | null;
  intervalDays: number | null;
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
 * match and the OS still holds the expected number of notifications.
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
    const intervalPending = await db.getFirstAsync<{ count: number }>(
      "SELECT count(*) AS count FROM reminders WHERE trigger_kind = 'date' AND fires_at > ?",
      now.toISOString(),
    );

    const needsTopUp = (intervalPending?.count ?? 0) === 0
      && plans.some((plan) => plan.pattern === 'EveryNDays');

    if (stillScheduled.length === known.length && !needsTopUp) {
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

    // A repeating OS trigger is only correct when the plan's zone is the device's: the
    // OS fires on device wall-clock, so a plan anchored to another zone has to be
    // converted and scheduled as individual instants instead.
    const sameZone = plan.timeZoneId === deviceZone;

    if (sameZone && plan.pattern === 'Daily') {
      scheduled += await schedule(db, plan, content, 'daily', signature, null, {
        type: Notifications.SchedulableTriggerInputTypes.DAILY,
        channelId: CHANNEL_ID,
        hour,
        minute,
      });
      continue;
    }

    if (sameZone && plan.pattern === 'SelectedWeekdays' && plan.weekdayMask !== null) {
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

    // Everything else — every-N-days, or a plan anchored to a different zone — is
    // scheduled as a bounded run of exact instants and topped up on later reconciles.
    for (const instant of upcomingInstants(plan, hour, minute, now)) {
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

function parseLocalTime(value: string): [number, number] {
  const [hour, minute] = value.split(':');
  return [Number(hour), Number(minute)];
}

/** The domain's Monday-first mask, as Expo's Sunday-first 1–7 weekday numbers. */
function expoWeekdays(mask: number): number[] {
  const days: number[] = [];
  for (let bit = 0; bit < 7; bit++) {
    if ((mask & (1 << bit)) !== 0) {
      // bit 0 = Monday in the domain; Expo wants 1 = Sunday.
      days.push(((bit + 1) % 7) + 1);
    }
  }

  return days;
}

/**
 * The next instants a plan is due at, bounded by {@link INTERVAL_HORIZON}.
 *
 * Used for every-N-days plans, and for any plan whose own time zone is not the
 * device's — in both cases a repeating OS trigger would fire at the wrong moment.
 */
function upcomingInstants(plan: ReminderPlan, hour: number, minute: number, now: Date): Date[] {
  const zone = plan.timeZoneId;
  const anchor = plan.effectiveFrom ? parseDate(plan.effectiveFrom) : null;
  const until = plan.effectiveTo ? parseDate(plan.effectiveTo) : null;
  const interval = plan.pattern === 'EveryNDays' ? (plan.intervalDays ?? 1) : 1;

  const instants: Date[] = [];
  const today = startOfDayParts(now, zone);

  for (let offset = 0; offset < INTERVAL_HORIZON * Math.max(interval, 1) && instants.length < INTERVAL_HORIZON; offset++) {
    const day = addDays(today, offset);

    if (anchor && compareDays(day, anchor) < 0) {
      continue;
    }

    if (until && compareDays(day, until) > 0) {
      break;
    }

    if (!isDue(plan, day, anchor, interval)) {
      continue;
    }

    const instant = instantForWallClock(day, hour, minute, zone);
    if (instant.getTime() > now.getTime()) {
      instants.push(instant);
    }
  }

  return instants;
}

type DayParts = { year: number; month: number; day: number };

function isDue(plan: ReminderPlan, day: DayParts, anchor: DayParts | null, interval: number): boolean {
  if (plan.pattern === 'Daily') {
    return true;
  }

  if (plan.pattern === 'SelectedWeekdays') {
    if (plan.weekdayMask === null) {
      return false;
    }
    // Monday is bit 0, matching the server's rule.
    const weekday = (new Date(Date.UTC(day.year, day.month - 1, day.day)).getUTCDay() + 6) % 7;
    return (plan.weekdayMask & (1 << weekday)) !== 0;
  }

  if (!anchor) {
    return false;
  }

  const elapsed = dayNumber(day) - dayNumber(anchor);
  return elapsed >= 0 && elapsed % interval === 0;
}

function dayNumber(parts: DayParts): number {
  return Math.floor(Date.UTC(parts.year, parts.month - 1, parts.day) / 86_400_000);
}

function compareDays(left: DayParts, right: DayParts): number {
  return dayNumber(left) - dayNumber(right);
}

function addDays(parts: DayParts, days: number): DayParts {
  const shifted = new Date(Date.UTC(parts.year, parts.month - 1, parts.day + days));
  return {
    year: shifted.getUTCFullYear(),
    month: shifted.getUTCMonth() + 1,
    day: shifted.getUTCDate(),
  };
}

function parseDate(value: string): DayParts {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);
  return { year, month, day };
}

function startOfDayParts(at: Date, zone: string): DayParts {
  const parts = zonedParts(at, zone);
  return { year: parts.year, month: parts.month, day: parts.day };
}

/**
 * The instant at which a wall-clock time occurs in a named zone.
 *
 * Two passes: guess the instant as if the wall clock were UTC, read the zone's real
 * offset at that guess, then correct. A second pass settles the case where the
 * correction itself crosses a daylight-saving boundary.
 */
function instantForWallClock(day: DayParts, hour: number, minute: number, zone: string): Date {
  const naive = Date.UTC(day.year, day.month - 1, day.day, hour, minute);
  let instant = naive;

  for (let pass = 0; pass < 2; pass++) {
    instant = naive - zoneOffsetMs(new Date(instant), zone);
  }

  return new Date(instant);
}

function zoneOffsetMs(at: Date, zone: string): number {
  const parts = zonedParts(at, zone);
  const asUtc = Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute, parts.second);
  return asUtc - at.getTime();
}

function zonedParts(at: Date, zone: string) {
  try {
    const formatter = new Intl.DateTimeFormat('en-US', {
      timeZone: zone,
      hour12: false,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });

    const parts: Record<string, string> = {};
    for (const part of formatter.formatToParts(at)) {
      parts[part.type] = part.value;
    }

    return {
      year: Number(parts.year),
      month: Number(parts.month),
      day: Number(parts.day),
      // Some engines render midnight as hour 24.
      hour: Number(parts.hour) % 24,
      minute: Number(parts.minute),
      second: Number(parts.second),
    };
  } catch {
    // A runtime without full Intl time-zone data falls back to the device's own clock.
    // The reminder is then anchored to device-local time, which is right whenever the
    // phone is in the plan's zone and visibly wrong when it is not.
    return {
      year: at.getFullYear(),
      month: at.getMonth() + 1,
      day: at.getDate(),
      hour: at.getHours(),
      minute: at.getMinutes(),
      second: at.getSeconds(),
    };
  }
}

export function resolveDeviceZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
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
        plan.localTime ?? '',
        plan.effectiveFrom ?? '',
        plan.effectiveTo ?? '',
        plan.timeZoneId,
      ].join(':'),
    )
    .sort()
    .join('|');
}
