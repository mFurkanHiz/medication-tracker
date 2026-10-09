import { StyleSheet } from 'react-native';
import { palette } from './theme';

/** The styles every full-screen sheet shares: the add-stock and edit-box sheets set them, the creation sheets reuse them. */
export const sheetStyles = StyleSheet.create({
  container: { padding: 20, gap: 14, paddingBottom: 48, backgroundColor: palette.surface, flexGrow: 1 },
  title: { fontSize: 22, fontWeight: '800', color: palette.ink },
  muted: { color: palette.inkMuted, fontSize: 14, lineHeight: 20 },
  label: { fontSize: 14, fontWeight: '700', color: palette.inkMuted },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderColor: palette.line,
    borderRadius: 12,
    paddingHorizontal: 14,
    fontSize: 18,
    color: palette.ink,
    backgroundColor: palette.raised,
  },
  actions: { flexDirection: 'row', gap: 10, justifyContent: 'flex-end' },
});
