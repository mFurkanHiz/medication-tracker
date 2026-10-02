import * as SecureStore from 'expo-secure-store';
import { ApiError, DEFAULT_API_URL, NetworkError, api } from '../lib/api';

/**
 * The signed-in session.
 *
 * The bearer token lives in the device keystore, never in SQLite and never in the
 * snapshot, so a database file copied off the device carries no credential.
 */

const STORAGE_KEY = 'medication-session-v2';

export type Session = {
  accountId: string;
  householdId: string;
  accessToken: string;
  apiUrl: string;
};

export async function restoreSession(): Promise<Session | null> {
  const stored = await SecureStore.getItemAsync(STORAGE_KEY);
  if (!stored) {
    return null;
  }

  try {
    const session = JSON.parse(stored) as Session;

    // A session minted against a different server is not this app's session. Guards
    // against a stale token surviving a change of EXPO_PUBLIC_API_URL.
    const usable =
      session.apiUrl === DEFAULT_API_URL &&
      typeof session.accessToken === 'string' &&
      /^[0-9a-f-]{36}$/i.test(session.householdId);

    return usable ? session : null;
  } catch {
    return null;
  }
}

export async function signIn(
  mode: 'login' | 'register',
  email: string,
  password: string,
  confirmPassword?: string,
): Promise<Session> {
  const apiUrl = DEFAULT_API_URL;
  const auth = await api.authenticate(apiUrl, mode, email.trim(), password, confirmPassword);

  if (!auth.accessToken) {
    throw new ApiError(500, 'missing_access_token');
  }

  const config = { apiUrl, accessToken: auth.accessToken };
  const profile = await api.session(config);
  const household = profile.households[0];

  if (!household) {
    throw new ApiError(403, 'forbidden');
  }

  const session: Session = {
    accountId: profile.accountId,
    householdId: household.id,
    accessToken: auth.accessToken,
    apiUrl,
  };

  await SecureStore.setItemAsync(STORAGE_KEY, JSON.stringify(session));
  return session;
}

/**
 * Ends the session.
 *
 * The local credential is cleared even when the server cannot be reached: the user
 * asked to sign out, and leaving a usable token on the device because the network was
 * down would be the wrong way to fail.
 */
export async function signOut(session: Session): Promise<void> {
  try {
    await api.signOut({ apiUrl: session.apiUrl, accessToken: session.accessToken });
  } catch (caught) {
    if (!(caught instanceof NetworkError) && !(caught instanceof ApiError)) {
      throw caught;
    }
  } finally {
    await SecureStore.deleteItemAsync(STORAGE_KEY);
  }
}

export async function forgetSession(): Promise<void> {
  await SecureStore.deleteItemAsync(STORAGE_KEY);
}
