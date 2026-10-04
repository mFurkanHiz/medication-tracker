/**
 * The product's name and one-line description, in a module with no `'use client'`.
 *
 * This file exists because of a silent failure. `layout.tsx` exported
 * `metadata: { title: tr.appName }`, which reads correct and shipped **no `<title>` at
 * all**: `i18n.ts` is a client module, so Next cannot evaluate the value while rendering
 * metadata, and rather than failing the build it emits nothing. Every page in the export
 * went out untitled — a browser tab with no name, and nothing for a screen reader to
 * announce when the document loads. The accessibility audit is what found it.
 *
 * `i18n.ts` imports these same constants for its `appName` and `appDescription` keys, so
 * the strings have one source and the document title cannot drift from the heading.
 */

export const APP_NAME = {
  tr: 'İlaç Takip',
  en: 'Medication Tracker',
} as const;

export const APP_DESCRIPTION = {
  tr: 'Ev ilaç düzeni, kutu bazlı stok ve denetlenebilir kullanım geçmişi',
  en: 'Household medication organisation, package-level stock, and an auditable history',
} as const;
