import { useCallback, useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, SafeAreaView, StatusBar, StyleSheet, Text, View } from 'react-native';
import { SQLiteProvider } from 'expo-sqlite';
import { databaseNameFor, migrateDatabase } from './src/data/database';
import { forgetSession, restoreSession, signOut, type Session } from './src/data/session';
import { LOCALES, dictionaries, type Locale, type MessageKey } from './src/lib/i18n';
import { HistoryScreen } from './src/screens/HistoryScreen';
import { PeopleScreen } from './src/screens/PeopleScreen';
import { PlansScreen } from './src/screens/PlansScreen';
import { SignInScreen } from './src/screens/SignInScreen';
import { StockScreen } from './src/screens/StockScreen';
import { TodayScreen } from './src/screens/TodayScreen';
import { versionLabel } from './src/lib/version';
import { TabBar, type Tab } from './src/ui/TabBar';
import { Button, LocaleProvider, palette } from './src/ui/theme';

/**
 * The application shell: locale, session, and the per-household database.
 *
 * The database is keyed on the household, so switching accounts cannot mix one
 * household's doses into another's cache.
 */
export default function App() {
  const [locale, setLocale] = useState<Locale>('tr');
  const [session, setSession] = useState<Session | null>(null);
  const [checked, setChecked] = useState(false);
  const [tab, setTab] = useState<Tab>('today');

  const t = useMemo(() => (key: MessageKey) => dictionaries[locale][key], [locale]);

  useEffect(() => {
    restoreSession()
      .then(setSession)
      .catch(() => setSession(null))
      .finally(() => setChecked(true));
  }, []);

  const endSession = useCallback(async () => {
    if (session) {
      // Never leave a usable credential behind because the network was down.
      await signOut(session).catch(() => forgetSession());
    }

    setSession(null);
  }, [session]);

  const context = useMemo(() => ({ locale, t, setLocale }), [locale, t]);

  return (
    <LocaleProvider value={context}>
      <SafeAreaView style={styles.safe}>
        <StatusBar barStyle="dark-content" />

        <View style={styles.header}>
          <View>
            <Text style={styles.brand}>{t('appName')}</Text>
            <Text style={styles.version}>{versionLabel()}</Text>
          </View>
          <View style={styles.locales}>
            {LOCALES.map((option) => (
              <Button
                key={option}
                tone={option === locale ? 'primary' : 'secondary'}
                label={option.toUpperCase()}
                accessibilityState={{ selected: option === locale }}
                onPress={() => setLocale(option)}
                style={styles.localeButton}
              />
            ))}
          </View>
        </View>

        {!checked ? (
          <View style={styles.center}>
            <ActivityIndicator color={palette.accent} />
            <Text style={styles.muted}>{t('loading')}</Text>
          </View>
        ) : session ? (
          <SQLiteProvider
            key={session.householdId}
            databaseName={databaseNameFor(session.householdId)}
            onInit={migrateDatabase}
          >
            <View style={styles.body}>
              {tab === 'today' ? (
                <TodayScreen session={session} onSignedOut={() => void endSession()} />
              ) : tab === 'stock' ? (
                <StockScreen session={session} />
              ) : tab === 'plans' ? (
                <PlansScreen session={session} />
              ) : tab === 'people' ? (
                <PeopleScreen session={session} />
              ) : (
                <HistoryScreen session={session} />
              )}
            </View>
            <TabBar tab={tab} onChange={setTab} />
          </SQLiteProvider>
        ) : (
          <SignInScreen onSignedIn={setSession} />
        )}
      </SafeAreaView>
    </LocaleProvider>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: palette.surface },
  body: { flex: 1 },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 10,
  },
  brand: { fontSize: 18, fontWeight: '800', color: palette.ink },
  version: { fontSize: 12, color: palette.inkFaint },
  locales: { flexDirection: 'row', gap: 6 },
  localeButton: { minHeight: 40, paddingHorizontal: 12 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 10 },
  muted: { color: palette.inkMuted },
});
