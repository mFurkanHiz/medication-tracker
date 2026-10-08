/**
 * The version this build of the web client is, as the footer shows it (ADR 0017).
 *
 * Both values are baked in at build time by next.config.ts from the repository's VERSION
 * file and the image build's GIT_SHA. A development build has no commit and says so.
 */
export const APP_VERSION = process.env.NEXT_PUBLIC_APP_VERSION || 'dev';
export const GIT_SHA = process.env.NEXT_PUBLIC_GIT_SHA || '';

/** `v1.0.1 · 9bf217c`, or just `v1.0.1` when the build did not know its commit. */
export function versionLabel(version: string = APP_VERSION, commit: string = GIT_SHA): string {
  const short = commit.trim().slice(0, 7);
  return short ? `v${version} · ${short}` : `v${version}`;
}
