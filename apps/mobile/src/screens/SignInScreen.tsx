import { useState } from 'react';
import { ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { ApiError, NetworkError } from '../lib/api';
import { errorKey } from '../lib/i18n';
import { signIn, type Session } from '../data/session';
import { Button, Card, Notice, palette, useTranslate } from '../ui/theme';

/** Registration and sign-in. The password never leaves this screen except to the API. */
export function SignInScreen({ onSignedIn }: { onSignedIn: (session: Session) => void }) {
  const { t } = useTranslate();
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const registering = mode === 'register';
  const canSubmit =
    email.trim().length > 3 &&
    password.length >= (registering ? 12 : 1) &&
    (!registering || password === confirm);

  async function submit() {
    setBusy(true);
    setError(null);

    try {
      onSignedIn(await signIn(mode, email, password, confirm));
    } catch (caught) {
      if (caught instanceof NetworkError) {
        setError(t('errorNetwork'));
      } else if (caught instanceof ApiError) {
        setError(t(caught.status === 401 ? 'errorInvalidCredentials' : errorKey(caught.code)));
      } else {
        setError(t('errorGeneric'));
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <ScrollView contentContainerStyle={styles.container} keyboardShouldPersistTaps="handled">
      <Text accessibilityRole="header" style={styles.title}>
        {registering ? t('signUpTitle') : t('signInTitle')}
      </Text>

      <Card>
        {error ? <Notice tone="danger" message={error} /> : null}

        <View>
          <Text style={styles.label} nativeID="email-label">
            {t('email')}
          </Text>
          <TextInput
            accessibilityLabelledBy="email-label"
            accessibilityLabel={t('email')}
            value={email}
            onChangeText={setEmail}
            autoCapitalize="none"
            autoComplete="email"
            keyboardType="email-address"
            style={styles.input}
          />
        </View>

        <View>
          <Text style={styles.label} nativeID="password-label">
            {t('password')}
          </Text>
          <TextInput
            accessibilityLabelledBy="password-label"
            accessibilityLabel={t('password')}
            value={password}
            onChangeText={setPassword}
            secureTextEntry
            autoComplete={registering ? 'new-password' : 'current-password'}
            style={styles.input}
          />
          {registering ? <Text style={styles.hint}>{t('passwordHint')}</Text> : null}
        </View>

        {registering ? (
          <View>
            <Text style={styles.label} nativeID="confirm-label">
              {t('confirmPassword')}
            </Text>
            <TextInput
              accessibilityLabelledBy="confirm-label"
              accessibilityLabel={t('confirmPassword')}
              value={confirm}
              onChangeText={setConfirm}
              secureTextEntry
              autoComplete="new-password"
              style={styles.input}
            />
            {confirm.length > 0 && password !== confirm ? (
              <Text style={styles.error}>{t('passwordMismatch')}</Text>
            ) : null}
          </View>
        ) : null}

        <Button
          label={busy ? t('loading') : registering ? t('signUp') : t('signIn')}
          disabled={busy || !canSubmit}
          onPress={() => void submit()}
        />

        <Button
          tone="quiet"
          label={registering ? t('haveAccount') : t('needAccount')}
          onPress={() => {
            setMode(registering ? 'login' : 'register');
            setError(null);
          }}
        />
      </Card>

      <Text style={styles.footer}>{t('demoNotice')}</Text>
      <Text style={styles.footer}>{t('safetyNotice')}</Text>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: { padding: 20, gap: 16 },
  title: { fontSize: 26, fontWeight: '800', color: palette.ink },
  label: { fontSize: 14, fontWeight: '700', color: palette.ink, marginBottom: 6 },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderColor: palette.line,
    borderRadius: 12,
    paddingHorizontal: 12,
    backgroundColor: palette.raised,
    color: palette.ink,
    fontSize: 16,
  },
  hint: { marginTop: 6, color: palette.inkMuted, fontSize: 13 },
  error: { marginTop: 6, color: palette.danger, fontSize: 13, fontWeight: '700' },
  footer: { color: palette.inkFaint, fontSize: 13, lineHeight: 19 },
});
