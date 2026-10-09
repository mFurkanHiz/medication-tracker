import { Pressable, StyleSheet, Text, View } from 'react-native';
import { palette, useTranslate } from './theme';

export type Tab = 'today' | 'stock' | 'plans' | 'people' | 'history' | 'reports';

/** The places the phone can show; a tab is a tab to a screen reader too. */
export function TabBar({ tab, onChange }: { tab: Tab; onChange: (next: Tab) => void }) {
  const { t } = useTranslate();
  const items: { key: Tab; label: string }[] = [
    { key: 'today', label: t('today') },
    { key: 'stock', label: t('stock') },
    { key: 'plans', label: t('plans') },
    { key: 'people', label: t('people') },
    { key: 'history', label: t('history') },
    { key: 'reports', label: t('reports') },
  ];

  return (
    <View style={styles.bar} accessibilityRole="tablist">
      {items.map((item) => {
        const selected = item.key === tab;
        return (
          <Pressable
            key={item.key}
            accessibilityRole="tab"
            accessibilityState={{ selected }}
            onPress={() => onChange(item.key)}
            style={[styles.tab, selected ? styles.selected : null]}
          >
            <Text style={[styles.label, selected ? styles.selectedLabel : null]}>{item.label}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  bar: {
    flexDirection: 'row',
    borderTopWidth: 1,
    borderTopColor: palette.line,
    backgroundColor: palette.raised,
  },
  tab: { flex: 1, minHeight: 52, alignItems: 'center', justifyContent: 'center' },
  selected: { borderTopWidth: 3, borderTopColor: palette.accent },
  label: { fontSize: 12, fontWeight: '700', color: palette.inkMuted },
  selectedLabel: { color: palette.accentInk },
});
