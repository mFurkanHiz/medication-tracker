import { StyleSheet, View } from 'react-native';
import { Button } from './theme';

/**
 * One choice among a few, as a row of chips: the phone's stand-in for the web's select
 * box on the creation sheets. The chosen chip is the primary one and says so to a screen
 * reader.
 */
export function Choices<T extends string>({
  options,
  value,
  onChange,
  accessibilityLabel,
}: {
  options: { value: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
  accessibilityLabel: string;
}) {
  return (
    <View style={styles.row} accessibilityRole="radiogroup" accessibilityLabel={accessibilityLabel}>
      {options.map((option) => {
        const selected = option.value === value;

        return (
          <Button
            key={option.value}
            tone={selected ? 'primary' : 'secondary'}
            label={option.label}
            accessibilityRole="radio"
            accessibilityState={{ selected, checked: selected }}
            onPress={() => onChange(option.value)}
            style={styles.chip}
          />
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  chip: { minHeight: 40, paddingHorizontal: 12 },
});
