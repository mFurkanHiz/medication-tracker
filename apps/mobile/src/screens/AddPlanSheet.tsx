import { useState } from 'react';
import { Modal, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import * as Crypto from 'expo-crypto';
import { useSQLiteContext } from 'expo-sqlite';
import { enqueueCommand } from '../data/outbox';
import type { MedicationStock, PersonRow } from '../data/snapshot';
import {
  DAY_PERIODS,
  MEAL_RELATIONS,
  SCHEDULES,
  SCHEDULE_KEYS,
  buildPlan,
  type PlanForm,
} from '../lib/commands';
import { enumKey } from '../lib/i18n';
import { localDate } from '../lib/local-date';
import { WEEKDAY_KEYS } from '../lib/plans';
import { Choices } from '../ui/Choices';
import { sheetStyles as styles } from '../ui/sheet';
import { Button, Card, Notice, palette, useTranslate } from '../ui/theme';

/**
 * Who takes which medicine, and how — the web's plan form on a phone sheet, with the
 * same rules: one pattern owns exactly its fields; a schedule is pinned to a clock time
 * or to a part of the day, never both; an as-needed plan has no clock but may still
 * record a preference and how the dose relates to food. What is typed is checked here
 * with the server's rules, so a mistake is heard now and not as a refusal after the
 * next sync.
 *
 * The plan exists on the phone the moment the sheet closes, its reminders with it. The
 * Today list is the server's and shows the plan after the sync; the card says so.
 */
export function AddPlanSheet({
  people,
  medications,
  onClose,
  onQueued,
}: {
  people: PersonRow[];
  medications: MedicationStock[];
  onClose: () => void;
  onQueued: () => void;
}) {
  const { t } = useTranslate();
  const db = useSQLiteContext();

  const [form, setForm] = useState<PlanForm>({
    personId: people[0]?.id ?? '',
    medicationId: medications[0]?.id ?? '',
    dose: '1',
    schedule: 'Daily',
    weekdayMask: 0,
    intervalDays: '2',
    dayOfMonth: '1',
    intervalMonths: '1',
    localTime: '08:00',
    dayPeriod: '',
    mealRelation: '',
    // From the day it is made — the web's rule, in the phone's own calendar.
    effectiveFrom: localDate(),
    effectiveTo: '',
  });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const set = <K extends keyof PlanForm>(key: K, value: PlanForm[K]) =>
    setForm((current) => ({ ...current, [key]: value }) as PlanForm);

  const scheduled = form.schedule !== 'AsNeeded';
  const medication = medications.find((row) => row.id === form.medicationId);
  const unit = medication ? medication.unit.toLowerCase() : '';
  const incomplete = people.length === 0 || medications.length === 0;

  async function submit() {
    const outcome = buildPlan(form, Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC');
    if (!outcome.ok) {
      setError(t(outcome.error));
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const id = Crypto.randomUUID();
      await enqueueCommand(
        db,
        {
          kind: 'plan.create',
          targetId: id,
          medicationId: outcome.body.medicationDefinitionId,
          body: { id, ...outcome.body },
        },
        new Date().toISOString(),
      );
      onQueued();
    } catch {
      setError(t('errorGeneric'));
      setBusy(false);
    }
  }

  const periodOptions = (noneLabel: string) => [
    { value: '', label: noneLabel },
    ...DAY_PERIODS.map((period) => {
      const key = enumKey(period);
      return { value: period as string, label: key ? t(key) : period };
    }),
  ];

  return (
    <Modal visible animationType="slide" onRequestClose={onClose} transparent={false}>
      <ScrollView contentContainerStyle={styles.container}>
        <Text accessibilityRole="header" style={styles.title}>
          {t('addPlan')}
        </Text>

        {error ? <Notice tone="danger" message={error} /> : null}
        {incomplete ? <Notice tone="warning" message={t('planNeedsPersonAndMedication')} /> : null}

        <Card>
          <Text style={styles.label}>{t('person')}</Text>
          <Choices
            options={people.map((person) => ({ value: person.id, label: person.name }))}
            value={form.personId}
            onChange={(value) => set('personId', value)}
            accessibilityLabel={t('person')}
          />

          <Text style={styles.label}>{t('medication')}</Text>
          <Choices
            options={medications.map((row) => ({
              value: row.id,
              label: row.strength ? `${row.name} ${row.strength}` : row.name,
            }))}
            value={form.medicationId}
            onChange={(value) => set('medicationId', value)}
            accessibilityLabel={t('medication')}
          />

          <Text style={styles.label}>
            {t('dose')}
            {unit ? ` (${unit})` : ''}
          </Text>
          <TextInput
            style={styles.input}
            value={form.dose}
            onChangeText={(value) => set('dose', value)}
            keyboardType="decimal-pad"
            accessibilityLabel={t('dose')}
          />
        </Card>

        <Card>
          <Text style={styles.label}>{t('schedule')}</Text>
          <Choices
            options={SCHEDULES.map((choice) => ({ value: choice, label: t(SCHEDULE_KEYS[choice]) }))}
            value={form.schedule}
            onChange={(value) => set('schedule', value)}
            accessibilityLabel={t('schedule')}
          />

          {form.schedule === 'SelectedWeekdays' ? (
            <View style={local.row} accessibilityLabel={t('scheduleWeekdays')}>
              {WEEKDAY_KEYS.map((key, bit) => {
                const checked = (form.weekdayMask & (1 << bit)) !== 0;

                return (
                  <Button
                    key={key}
                    tone={checked ? 'primary' : 'secondary'}
                    label={t(key)}
                    accessibilityRole="checkbox"
                    accessibilityState={{ checked }}
                    onPress={() => set('weekdayMask', form.weekdayMask ^ (1 << bit))}
                    style={local.chip}
                  />
                );
              })}
            </View>
          ) : null}

          {form.schedule === 'EveryNDays' ? (
            <>
              <Text style={styles.label}>{t('intervalDays')}</Text>
              <TextInput
                style={styles.input}
                value={form.intervalDays}
                onChangeText={(value) => set('intervalDays', value)}
                keyboardType="number-pad"
                accessibilityLabel={t('intervalDays')}
              />
            </>
          ) : null}

          {form.schedule === 'DayOfMonth' ? (
            <>
              <Text style={styles.label}>{t('dayOfMonth')}</Text>
              <TextInput
                style={styles.input}
                value={form.dayOfMonth}
                onChangeText={(value) => set('dayOfMonth', value)}
                keyboardType="number-pad"
                accessibilityLabel={t('dayOfMonth')}
              />
              <Text style={styles.muted}>{t('dayOfMonthHint')}</Text>
            </>
          ) : null}

          {form.schedule === 'EveryNMonths' ? (
            <>
              <Text style={styles.label}>{t('intervalMonths')}</Text>
              <TextInput
                style={styles.input}
                value={form.intervalMonths}
                onChangeText={(value) => set('intervalMonths', value)}
                keyboardType="number-pad"
                accessibilityLabel={t('intervalMonths')}
              />
              <Text style={styles.muted}>{t('intervalMonthsHint')}</Text>
            </>
          ) : null}
        </Card>

        <Card>
          {scheduled ? (
            <>
              <Text style={styles.label}>{t('whenInDay')}</Text>
              <Choices
                options={periodOptions(t('useExactTime'))}
                value={form.dayPeriod}
                onChange={(value) => set('dayPeriod', value)}
                accessibilityLabel={t('whenInDay')}
              />
              {form.dayPeriod === '' ? (
                <>
                  <Text style={styles.label}>{t('exactTime')}</Text>
                  <TextInput
                    style={styles.input}
                    value={form.localTime}
                    onChangeText={(value) => set('localTime', value)}
                    keyboardType="numbers-and-punctuation"
                    placeholder="08:00"
                    placeholderTextColor={palette.inkFaint}
                    accessibilityLabel={t('exactTime')}
                  />
                </>
              ) : null}
            </>
          ) : (
            <>
              {/* No clock here on purpose: a dose fixed to a time is a scheduled dose,
                  and that is a choice above. A preference is not a time. */}
              <Text style={styles.label}>
                {t('preferredTime')} · {t('optional')}
              </Text>
              <Choices
                options={periodOptions(t('noTimePreference'))}
                value={form.dayPeriod}
                onChange={(value) => set('dayPeriod', value)}
                accessibilityLabel={t('preferredTime')}
              />
              <Text style={styles.muted}>{t('preferredTimeHint')}</Text>
              <Text style={styles.muted}>{t('asNeededExplainer')}</Text>
            </>
          )}

          <Text style={styles.label}>
            {t('mealRelation')} · {t('optional')}
          </Text>
          <Choices
            options={[
              { value: '', label: t('none') },
              ...MEAL_RELATIONS.map((relation) => {
                const key = enumKey(relation);
                return { value: relation as string, label: key ? t(key) : relation };
              }),
            ]}
            value={form.mealRelation}
            onChange={(value) => set('mealRelation', value)}
            accessibilityLabel={t('mealRelation')}
          />
          <Text style={styles.muted}>{t('mealRelationHint')}</Text>
        </Card>

        <Card>
          <Text style={styles.label}>{t('effectiveFrom')}</Text>
          <TextInput
            style={styles.input}
            value={form.effectiveFrom}
            onChangeText={(value) => set('effectiveFrom', value)}
            keyboardType="numbers-and-punctuation"
            placeholder="2026-10-09"
            placeholderTextColor={palette.inkFaint}
            accessibilityLabel={t('effectiveFrom')}
          />

          <Text style={styles.label}>
            {t('effectiveTo')} · {t('optional')}
          </Text>
          <TextInput
            style={styles.input}
            value={form.effectiveTo}
            onChangeText={(value) => set('effectiveTo', value)}
            keyboardType="numbers-and-punctuation"
            placeholder="2027-01-31"
            placeholderTextColor={palette.inkFaint}
            accessibilityLabel={t('effectiveTo')}
          />
        </Card>

        <Text style={styles.muted}>{t('queuedOffline')}</Text>

        <View style={styles.actions}>
          <Button tone="secondary" label={t('cancel')} onPress={onClose} disabled={busy} />
          <Button label={t('save')} onPress={() => void submit()} disabled={busy || incomplete} />
        </View>
      </ScrollView>
    </Modal>
  );
}

const local = StyleSheet.create({
  row: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  chip: { minHeight: 40, paddingHorizontal: 12 },
});
