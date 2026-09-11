import { StatusBar } from 'expo-status-bar';
import { SQLiteProvider, useSQLiteContext } from 'expo-sqlite';
import { createContext, useContext, useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, AppState, Pressable, SafeAreaView, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { restoreSession, signIn, signOut, type MobileSession } from './src/auth';
import { messages } from './src/i18n';
import { createPlan, getInventorySummary, getSummary, getToday, migrateDatabase, recordOutcome, syncPending, type SyncConfig, type TodayDose } from './src/storage';

const LocaleContext = createContext<'tr' | 'en'>('tr');
const useMessages = () => messages[useContext(LocaleContext)];
function TodayScreen({ configuredSync, onLogout }: { configuredSync: SyncConfig; onLogout: () => Promise<void> }) {
  const t = useMessages();
  const db = useSQLiteContext();
  const [doses, setDoses] = useState<TodayDose[]>([]);
  const [summary, setSummary] = useState({ stock: '0', pending: 0, depletionDays: 0 });
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [syncError, setSyncError] = useState(false);
  const [adding, setAdding] = useState(false);
  const [inventory, setInventory] = useState<{ id: string; name: string; stock: string }[]>([]);
  const refresh = useCallback(async () => { setDoses(await getToday(db)); setSummary(await getSummary(db)); setInventory(await getInventorySummary(db)); setLoading(false); }, [db]);
  useEffect(() => { void refresh(); }, [refresh]);
  const runSync = useCallback(async () => {
    if (!configuredSync) return;
    setSyncing(true); setSyncError(false);
    try { await syncPending(db, configuredSync); await refresh(); } catch { setSyncError(true); } finally { setSyncing(false); }
  }, [db, refresh, configuredSync]);
  useEffect(() => {
    if (configuredSync) void runSync();
    const subscription = AppState.addEventListener('change', state => { if (state === 'active' && configuredSync) void runSync(); });
    return () => subscription.remove();
  }, [runSync]);
  if (loading) return <View style={styles.center}><ActivityIndicator color="#29675b" /><Text style={styles.muted}>{t.loading}</Text></View>;
  return <SafeAreaView style={styles.safe}><ScrollView contentContainerStyle={styles.container}>
    <Pressable accessibilityRole="button" style={styles.secondary} onPress={() => { void onLogout().catch(() => setSyncError(true)); }}><Text>{t.logout}</Text></Pressable>
    <View style={styles.badge}><View style={styles.dot} /><Text style={styles.badgeText}>{t.offline}</Text></View>
    <Text style={styles.eyebrow}>{t.eyebrow}</Text><Text style={styles.title}>{t.title}</Text><Text style={styles.description}>{t.description}</Text>
    <View style={styles.metrics}><View style={styles.metric}><Text style={styles.metricLabel}>{t.pending}</Text><Text style={styles.metricValue}>{summary.pending}</Text></View></View>
    {inventory.map(item => <View key={item.id} style={styles.card}><Text style={styles.medication}>{item.name}</Text><Text>{t.stock}: {item.stock} {t.tablet}</Text></View>)}
    {configuredSync && summary.pending > 0 && <Pressable accessibilityRole="button" disabled={syncing} style={styles.syncButton} onPress={runSync}><Text style={styles.syncButtonText}>{syncing ? t.syncing : t.syncNow}</Text></Pressable>}{syncError && <Text style={styles.error}>{t.syncError}</Text>}
    <Pressable accessibilityRole="button" style={styles.primary} onPress={() => setAdding(!adding)}><Text style={styles.primaryText}>{adding ? t.cancel : t.addMedication}</Text></Pressable>
    {adding && <PlanForm onSave={async (person, medication, stock, dose, time) => { await createPlan(db, person, medication, stock, dose, time); setAdding(false); await refresh(); }} />}
    {doses.length === 0 ? <View style={styles.card}><Text style={styles.empty}>{t.empty}</Text></View> : doses.map(dose => <View style={styles.card} key={dose.regimenVersionId}><View style={styles.row}><View><Text style={styles.person}>{dose.personName}</Text><Text style={styles.medication}>{dose.medicationName}</Text></View><Text style={[styles.status, dose.outcome !== 'due' && styles.statusTaken]}>{dose.outcome === 'taken' ? t.taken : dose.outcome === 'skipped' ? t.skipped : t.due}</Text></View><Text style={styles.dose}>{dose.doseNumerator}/{dose.doseDenominator} {t.tablet} · {new Date(dose.scheduledFor).toLocaleTimeString('tr', { hour: '2-digit', minute: '2-digit' })}</Text>{dose.outcome === 'due' && <View style={styles.actions}><Pressable accessibilityRole="button" style={styles.primary} onPress={async () => { try { await recordOutcome(db, dose, 'taken'); await refresh(); } catch { setSyncError(true); } }}><Text style={styles.primaryText}>{t.markTaken}</Text></Pressable><Pressable accessibilityRole="button" style={styles.secondary} onPress={async () => { try { await recordOutcome(db, dose, 'skipped'); await refresh(); } catch { setSyncError(true); } }}><Text style={styles.secondaryText}>{t.markSkipped}</Text></Pressable></View>}</View>)}
  </ScrollView><StatusBar style="dark" /></SafeAreaView>;
}

function PlanForm({ onSave }: { onSave: (person: string, medication: string, stock: string, dose: string, time: string) => Promise<void> }) {
  const t = useMessages();
  const [values, setValues] = useState({ personName: '', medicationName: '', stockInput: '', doseInput: '', timeInput: '09:00' });
  const [busy, setBusy] = useState(false); const [error, setError] = useState(false);
  return <View style={styles.card}>{(Object.keys(values) as (keyof typeof values)[]).map(key => <View key={key}><Text>{t[key]}</Text><TextInput accessibilityLabel={t[key]} value={values[key]} onChangeText={value => setValues({ ...values, [key]: value })} style={styles.input} /></View>)}{error && <Text style={styles.error}>{t.invalidPlan}</Text>}<Pressable disabled={busy} style={styles.primary} onPress={async () => { setBusy(true); setError(false); try { await onSave(values.personName, values.medicationName, values.stockInput, values.doseInput, values.timeInput); } catch { setError(true); } finally { setBusy(false); } }}><Text style={styles.primaryText}>{t.save}</Text></Pressable></View>;
}

function AppContent() {
  const t = useMessages();
  const [session, setSession] = useState<MobileSession | null>(null);
  const [ready, setReady] = useState(false); const [email, setEmail] = useState(''); const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [register, setRegister] = useState(false); const [busy, setBusy] = useState(false); const [error, setError] = useState(false);
  useEffect(() => { restoreSession().then(setSession).catch(() => setError(true)).finally(() => setReady(true)); }, []);
  if (!ready) return <View style={styles.center}><ActivityIndicator /></View>;
  if (session) return <SQLiteProvider key={session.householdId} databaseName={`household-${session.householdId}.db`} onInit={migrateDatabase}><TodayScreen configuredSync={session} onLogout={async () => { await signOut(session); setSession(null); }} /></SQLiteProvider>;
  return <SafeAreaView style={styles.safe}><ScrollView contentContainerStyle={styles.container}><Text style={styles.title}>{register ? t.register : t.login}</Text><Text>{t.email}</Text><TextInput accessibilityLabel={t.email} value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" style={styles.card} /><Text>{t.password}</Text><TextInput accessibilityLabel={t.password} value={password} onChangeText={setPassword} secureTextEntry style={styles.card} />{register && <><Text>{t.confirmPassword}</Text><TextInput accessibilityLabel={t.confirmPassword} value={confirmPassword} onChangeText={setConfirmPassword} secureTextEntry style={styles.card} /></>}{register && confirmPassword.length > 0 && password !== confirmPassword && <Text style={styles.error}>{t.passwordMismatch}</Text>}{error && <Text style={styles.error}>{t.loginError}</Text>}<Pressable disabled={busy || (register && (password !== confirmPassword || password.length < 12))} style={styles.primary} onPress={async () => { setBusy(true); setError(false); try { setSession(await signIn(email, password, register, confirmPassword)); setPassword(''); setConfirmPassword(''); } catch { setError(true); } finally { setBusy(false); } }}><Text style={styles.primaryText}>{busy ? t.loading : register ? t.register : t.login}</Text></Pressable><Pressable style={styles.secondary} onPress={() => setRegister(!register)}><Text>{register ? t.login : t.register}</Text></Pressable></ScrollView></SafeAreaView>;
}

export default function App() {
  const [locale, setLocale] = useState<'tr' | 'en'>('tr');
  return <LocaleContext.Provider value={locale}><View style={{ flex: 1, backgroundColor: '#f3f0e8', paddingTop: 30 }}><Pressable accessibilityRole="button" onPress={() => setLocale(locale === 'tr' ? 'en' : 'tr')} style={{ padding: 12, alignSelf: 'flex-end' }}><Text>{locale === 'tr' ? 'English' : 'Türkçe'}</Text></Pressable><AppContent /></View></LocaleContext.Provider>;
}

const styles = StyleSheet.create({
  input: { borderWidth: 1, borderColor: '#b4c2bb', borderRadius: 9, padding: 12, marginTop: 6, color: '#19332f' },
  safe: { flex: 1, backgroundColor: '#f3f0e8' }, container: { padding: 24, paddingTop: 42, gap: 16 }, center: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 12, backgroundColor: '#f3f0e8' },
  badge: { alignSelf: 'flex-start', flexDirection: 'row', alignItems: 'center', gap: 7, paddingHorizontal: 11, paddingVertical: 7, borderRadius: 99, backgroundColor: '#dcebe4' }, dot: { width: 8, height: 8, borderRadius: 4, backgroundColor: '#29675b' }, badgeText: { color: '#23584f', fontWeight: '700', fontSize: 12 },
  eyebrow: { marginTop: 12, color: '#9d4c2f', fontSize: 13, fontWeight: '800', letterSpacing: 1.5, textTransform: 'uppercase' }, title: { color: '#19332f', fontSize: 38, lineHeight: 43, fontWeight: '800' }, description: { color: '#596a65', fontSize: 16, lineHeight: 24 },
  metrics: { flexDirection: 'row', gap: 8 }, metric: { flex: 1, borderRadius: 20, backgroundColor: '#19332f', padding: 14, minHeight: 112 }, metricLabel: { color: '#bad1ca', fontSize: 11, fontWeight: '700' }, metricValue: { color: '#fffaf0', fontSize: 28, fontWeight: '800', marginTop: 5 }, metricHint: { color: '#bad1ca', fontSize: 11 },
  card: { backgroundColor: '#fffaf0', borderRadius: 24, padding: 20, gap: 16, borderWidth: 1, borderColor: '#e1dbce' }, row: { flexDirection: 'row', justifyContent: 'space-between', gap: 12 }, person: { color: '#6b7773', fontSize: 13 }, medication: { color: '#19332f', fontSize: 22, fontWeight: '800', marginTop: 3 }, status: { color: '#9d4c2f', fontWeight: '800', backgroundColor: '#f4ded3', paddingHorizontal: 10, paddingVertical: 6, borderRadius: 99, alignSelf: 'flex-start' }, statusTaken: { color: '#29675b', backgroundColor: '#dcebe4' }, dose: { color: '#596a65', fontSize: 16 }, empty: { color: '#596a65', fontSize: 16, lineHeight: 24 }, actions: { gap: 9 }, primary: { backgroundColor: '#d5653d', minHeight: 50, borderRadius: 15, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 16 }, primaryText: { color: '#fffaf0', fontSize: 15, fontWeight: '800' }, secondary: { minHeight: 46, borderRadius: 15, borderWidth: 1, borderColor: '#c9c1b2', alignItems: 'center', justifyContent: 'center' }, secondaryText: { color: '#4e625c', fontWeight: '800' }, syncButton: { minHeight: 46, borderRadius: 15, borderWidth: 1, borderColor: '#29675b', alignItems: 'center', justifyContent: 'center' }, syncButtonText: { color: '#29675b', fontWeight: '800' }, error: { color: '#9d4c2f' }, muted: { color: '#596a65' },
});
