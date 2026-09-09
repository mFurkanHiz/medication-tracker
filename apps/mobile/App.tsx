import { StatusBar } from 'expo-status-bar';
import { SQLiteProvider, useSQLiteContext } from 'expo-sqlite';
import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, SafeAreaView, ScrollView, StyleSheet, Text, View } from 'react-native';
import { messages } from './src/i18n';
import { createSamplePlan, getSummary, getToday, markTaken, migrateDatabase, type TodayDose } from './src/storage';

const t = messages.tr;

function TodayScreen() {
  const db = useSQLiteContext();
  const [doses, setDoses] = useState<TodayDose[]>([]);
  const [summary, setSummary] = useState({ stock: '0', pending: 0 });
  const [loading, setLoading] = useState(true);
  const refresh = useCallback(async () => { setDoses(await getToday(db)); setSummary(await getSummary(db)); setLoading(false); }, [db]);
  useEffect(() => { void refresh(); }, [refresh]);
  if (loading) return <View style={styles.center}><ActivityIndicator color="#29675b" /><Text style={styles.muted}>{t.loading}</Text></View>;
  return <SafeAreaView style={styles.safe}><ScrollView contentContainerStyle={styles.container}>
    <View style={styles.badge}><View style={styles.dot} /><Text style={styles.badgeText}>{t.offline}</Text></View>
    <Text style={styles.eyebrow}>{t.eyebrow}</Text><Text style={styles.title}>{t.title}</Text><Text style={styles.description}>{t.description}</Text>
    <View style={styles.metrics}><View style={styles.metric}><Text style={styles.metricLabel}>{t.stock}</Text><Text style={styles.metricValue}>{summary.stock}</Text><Text style={styles.metricHint}>{t.exact}</Text></View><View style={styles.metric}><Text style={styles.metricLabel}>{t.pending}</Text><Text style={styles.metricValue}>{summary.pending}</Text></View></View>
    {doses.length === 0 ? <View style={styles.card}><Text style={styles.empty}>{t.empty}</Text><Pressable accessibilityRole="button" style={styles.primary} onPress={async () => { await createSamplePlan(db, t.samplePerson, t.sampleMedication); await refresh(); }}><Text style={styles.primaryText}>{t.createDemo}</Text></Pressable></View> : doses.map(dose => <View style={styles.card} key={dose.regimenVersionId}><View style={styles.row}><View><Text style={styles.person}>{dose.personName}</Text><Text style={styles.medication}>{dose.medicationName}</Text></View><Text style={[styles.status, dose.taken && styles.statusTaken]}>{dose.taken ? t.taken : t.due}</Text></View><Text style={styles.dose}>{dose.doseNumerator}/{dose.doseDenominator} {t.tablet} · 09:00</Text>{!dose.taken && <Pressable accessibilityRole="button" style={styles.primary} onPress={async () => { await markTaken(db, dose); await refresh(); }}><Text style={styles.primaryText}>{t.markTaken}</Text></Pressable>}</View>)}
  </ScrollView><StatusBar style="dark" /></SafeAreaView>;
}

export default function App() { return <SQLiteProvider databaseName="medication-tracker.db" onInit={migrateDatabase}><TodayScreen /></SQLiteProvider>; }

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: '#f3f0e8' }, container: { padding: 24, paddingTop: 42, gap: 16 }, center: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 12, backgroundColor: '#f3f0e8' },
  badge: { alignSelf: 'flex-start', flexDirection: 'row', alignItems: 'center', gap: 7, paddingHorizontal: 11, paddingVertical: 7, borderRadius: 99, backgroundColor: '#dcebe4' }, dot: { width: 8, height: 8, borderRadius: 4, backgroundColor: '#29675b' }, badgeText: { color: '#23584f', fontWeight: '700', fontSize: 12 },
  eyebrow: { marginTop: 12, color: '#9d4c2f', fontSize: 13, fontWeight: '800', letterSpacing: 1.5, textTransform: 'uppercase' }, title: { color: '#19332f', fontSize: 38, lineHeight: 43, fontWeight: '800' }, description: { color: '#596a65', fontSize: 16, lineHeight: 24 },
  metrics: { flexDirection: 'row', gap: 12 }, metric: { flex: 1, borderRadius: 20, backgroundColor: '#19332f', padding: 18, minHeight: 112 }, metricLabel: { color: '#bad1ca', fontSize: 12, fontWeight: '700' }, metricValue: { color: '#fffaf0', fontSize: 32, fontWeight: '800', marginTop: 5 }, metricHint: { color: '#bad1ca', fontSize: 11 },
  card: { backgroundColor: '#fffaf0', borderRadius: 24, padding: 20, gap: 16, borderWidth: 1, borderColor: '#e1dbce' }, row: { flexDirection: 'row', justifyContent: 'space-between', gap: 12 }, person: { color: '#6b7773', fontSize: 13 }, medication: { color: '#19332f', fontSize: 22, fontWeight: '800', marginTop: 3 }, status: { color: '#9d4c2f', fontWeight: '800', backgroundColor: '#f4ded3', paddingHorizontal: 10, paddingVertical: 6, borderRadius: 99, alignSelf: 'flex-start' }, statusTaken: { color: '#29675b', backgroundColor: '#dcebe4' }, dose: { color: '#596a65', fontSize: 16 }, empty: { color: '#596a65', fontSize: 16, lineHeight: 24 }, primary: { backgroundColor: '#d5653d', minHeight: 50, borderRadius: 15, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 16 }, primaryText: { color: '#fffaf0', fontSize: 15, fontWeight: '800' }, muted: { color: '#596a65' },
});
