import type { Metadata } from 'next';
import './globals.css';
import { APP_DESCRIPTION, APP_NAME } from '@/lib/document-metadata';

/**
 * Read from a module that is not `'use client'`, deliberately. Sourcing these from
 * `i18n.ts` compiled, type-checked and emitted no `<title>` at all — see
 * `document-metadata.ts`.
 */
export const metadata: Metadata = {
  title: APP_NAME.tr,
  description: APP_DESCRIPTION.tr,
};

/**
 * `lang` starts at the default locale and is updated by the client once the reader's
 * stored choice is known, so assistive technology always has a language to announce.
 */
export default function RootLayout({ children }: LayoutProps<'/'>) {
  return (
    <html lang="tr">
      <body>{children}</body>
    </html>
  );
}
