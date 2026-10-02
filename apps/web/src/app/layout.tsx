import type { Metadata } from 'next';
import './globals.css';
import { tr } from '@/lib/i18n';

export const metadata: Metadata = {
  title: tr.appName,
  description: tr.appDescription,
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
