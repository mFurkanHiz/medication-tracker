import { StyleSheet, Text } from 'react-native';
import type { CommandRow } from '../data/command-queue';
import { describeCommand, describeTarget } from '../lib/commands';
import { errorKey } from '../lib/i18n';
import { Button, Card, palette, useTranslate } from './theme';

/**
 * Commands the server refused, each with its reason and a way to dismiss it.
 *
 * Shown rather than retried forever or dropped: the household decided something and
 * the server said no, and the person who decided must hear that. Dismissing deletes the
 * command; nothing happened on the server, so there is nothing to keep.
 */
export function RefusedCommands({ rows, onDismiss }: { rows: CommandRow[]; onDismiss: (idempotencyKey: string) => void }) {
  const { t } = useTranslate();

  return (
    <>
      {rows.map((row) => {
        const target = describeTarget(row, t);

        return (
          <Card key={row.idempotencyKey} style={styles.card}>
            <Text style={styles.title}>{t('commandRejected')}</Text>
            <Text style={styles.body}>
              {describeCommand(row.kind, JSON.parse(row.payload), t)}
              {target ? ` · ${target}` : ''}
            </Text>
            <Text style={styles.muted}>{t(errorKey(row.rejectedCode ?? 'request_failed'))}</Text>
            <Button tone="secondary" label={t('dismiss')} onPress={() => onDismiss(row.idempotencyKey)} />
          </Card>
        );
      })}
    </>
  );
}

const styles = StyleSheet.create({
  card: { borderColor: palette.danger, backgroundColor: palette.dangerSoft },
  title: { fontSize: 15, fontWeight: '800', color: palette.danger },
  body: { fontSize: 15, fontWeight: '700', color: palette.ink },
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
});
