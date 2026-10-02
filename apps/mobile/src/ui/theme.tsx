import { createContext, useContext, type ReactNode } from 'react';
import { Pressable, StyleSheet, Text, View, type PressableProps, type ViewStyle } from 'react-native';
import { dictionaries, type Locale, type MessageKey, type Translate } from '../lib/i18n';

/** Shared colours and primitives, so screens describe intent rather than repeat styling. */

export const palette = {
  surface: '#f3f1ea',
  raised: '#fffdf8',
  sunken: '#e9e5da',
  ink: '#17322d',
  inkMuted: '#5c6f69',
  inkFaint: '#8a9690',
  line: '#d9d3c6',
  accent: '#c8532f',
  accentSoft: '#f6e0d7',
  accentInk: '#8f3a1f',
  positive: '#2f7a63',
  positiveSoft: '#d8e9e1',
  warning: '#9a6514',
  warningSoft: '#f8ecd3',
  danger: '#a2322c',
  dangerSoft: '#f7dcda',
} as const;

type LocaleValue = { locale: Locale; t: Translate; setLocale: (next: Locale) => void };

const LocaleContext = createContext<LocaleValue>({
  locale: 'tr',
  t: (key) => dictionaries.tr[key],
  setLocale: () => {},
});

export const LocaleProvider = LocaleContext.Provider;

export function useTranslate(): LocaleValue {
  return useContext(LocaleContext);
}

export function Card({ children, style }: { children: ReactNode; style?: ViewStyle }) {
  return <View style={[styles.card, style]}>{children}</View>;
}

type ButtonTone = 'primary' | 'secondary' | 'quiet' | 'danger';

export function Button({
  label,
  tone = 'primary',
  style,
  ...props
}: PressableProps & { label: string; tone?: ButtonTone; style?: ViewStyle }) {
  const toneStyle = {
    primary: styles.primary,
    secondary: styles.secondary,
    quiet: styles.quiet,
    danger: styles.danger,
  }[tone];

  const textStyle = {
    primary: styles.primaryText,
    secondary: styles.secondaryText,
    quiet: styles.quietText,
    danger: styles.dangerText,
  }[tone];

  return (
    <Pressable
      accessibilityRole="button"
      // A disabled control must say so, not just look faded.
      accessibilityState={{ disabled: !!props.disabled }}
      {...props}
      style={({ pressed }) => [
        styles.button,
        toneStyle,
        props.disabled ? styles.disabled : null,
        pressed ? styles.pressed : null,
        style,
      ]}
    >
      <Text style={[styles.buttonText, textStyle]}>{label}</Text>
    </Pressable>
  );
}

export function Badge({ label, tone = 'neutral' }: { label: string; tone?: 'neutral' | 'positive' | 'warning' | 'danger' | 'accent' }) {
  const toneStyle = {
    neutral: { backgroundColor: palette.sunken, color: palette.ink },
    positive: { backgroundColor: palette.positiveSoft, color: palette.positive },
    warning: { backgroundColor: palette.warningSoft, color: palette.warning },
    danger: { backgroundColor: palette.dangerSoft, color: palette.danger },
    accent: { backgroundColor: palette.accentSoft, color: palette.accentInk },
  }[tone];

  return (
    <View style={[styles.badge, { backgroundColor: toneStyle.backgroundColor }]}>
      <Text style={[styles.badgeText, { color: toneStyle.color }]}>{label}</Text>
    </View>
  );
}

export function Notice({
  message,
  tone = 'warning',
}: {
  message: string;
  tone?: 'warning' | 'danger' | 'positive';
}) {
  const toneStyle = {
    warning: { backgroundColor: palette.warningSoft, color: palette.warning },
    danger: { backgroundColor: palette.dangerSoft, color: palette.danger },
    positive: { backgroundColor: palette.positiveSoft, color: palette.positive },
  }[tone];

  return (
    <View
      // Announced by a screen reader without taking focus away from what the user is doing.
      accessibilityRole="alert"
      accessibilityLiveRegion="polite"
      style={[styles.notice, { backgroundColor: toneStyle.backgroundColor }]}
    >
      <Text style={[styles.noticeText, { color: toneStyle.color }]}>{message}</Text>
    </View>
  );
}

export function SectionTitle({ children }: { children: string }) {
  return (
    <Text accessibilityRole="header" style={styles.sectionTitle}>
      {children}
    </Text>
  );
}

export function keyOf(key: MessageKey): MessageKey {
  return key;
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: palette.raised,
    borderRadius: 18,
    borderWidth: 1,
    borderColor: palette.line,
    padding: 16,
    gap: 12,
  },
  button: {
    // 48dp comfortably clears the Android accessibility minimum for a touch target.
    minHeight: 48,
    borderRadius: 14,
    borderWidth: 1,
    paddingHorizontal: 18,
    alignItems: 'center',
    justifyContent: 'center',
  },
  primary: { backgroundColor: palette.ink, borderColor: palette.ink },
  secondary: { backgroundColor: palette.raised, borderColor: palette.line },
  quiet: { backgroundColor: 'transparent', borderColor: 'transparent' },
  danger: { backgroundColor: 'transparent', borderColor: palette.danger },
  disabled: { opacity: 0.5 },
  pressed: { opacity: 0.85 },
  buttonText: { fontSize: 16, fontWeight: '700' },
  primaryText: { color: palette.raised },
  secondaryText: { color: palette.ink },
  quietText: { color: palette.accentInk },
  dangerText: { color: palette.danger },
  badge: { borderRadius: 999, paddingHorizontal: 10, paddingVertical: 4, alignSelf: 'flex-start' },
  badgeText: { fontSize: 12, fontWeight: '800' },
  notice: { borderRadius: 12, paddingHorizontal: 12, paddingVertical: 10 },
  noticeText: { fontSize: 14, fontWeight: '600' },
  sectionTitle: { fontSize: 18, fontWeight: '800', color: palette.ink },
});
