import * as SecureStore from 'expo-secure-store';

export const apiUrl = process.env.EXPO_PUBLIC_API_URL || 'https://medicationtracker.rapidconfigs.com';
export type MobileSession = { accountId: string; householdId: string; accessToken: string; apiUrl: string };
const key = 'medication-session';
export async function restoreSession(): Promise<MobileSession | null> {
  const value = await SecureStore.getItemAsync(key);
  if (!value) return null;
  const session = JSON.parse(value) as MobileSession;
  return session.apiUrl === apiUrl && /^[a-f0-9-]{36}$/i.test(session.householdId) ? session : null;
}
export async function signIn(email: string, password: string, register: boolean, confirmPassword?: string) {
  const response = await fetch(`${apiUrl}/api/auth/${register ? 'register' : 'login'}`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Medication-Client': '1' }, body: JSON.stringify({ email, password, mobile: true, ...(register ? { confirmPassword } : {}) }) });
  if (!response.ok) throw new Error('login_failed');
  const result = await response.json() as { accountId: string; accessToken: string };
  const profile = await fetch(`${apiUrl}/api/auth/session`, { headers: { Authorization: `Bearer ${result.accessToken}` } });
  if (!profile.ok) throw new Error('session_failed');
  const data = await profile.json() as { households: { id: string }[] };
  if (!data.households.length) throw new Error('household_missing');
  const session: MobileSession = { ...result, apiUrl, householdId: data.households[0].id };
  await SecureStore.setItemAsync(key, JSON.stringify(session));
  return session;
}
export async function signOut(session: MobileSession) {
  const response = await fetch(`${apiUrl}/api/auth/logout`, { method: 'POST', headers: { Authorization: `Bearer ${session.accessToken}`, 'X-Medication-Client': '1' } });
  if (!response.ok) throw new Error('logout_failed');
  await SecureStore.deleteItemAsync(key);
}
