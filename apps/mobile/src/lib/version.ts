import appConfig from '../../app.json';

/**
 * The version this build of the phone app is (ADR 0017).
 *
 * The number is the server version the app is at parity with: `1.0.1` here means the
 * phone understands everything the `v1.0.1` server sends for the screens it has. The
 * release itself is tagged `mobile-v1.0.1` once a build has been verified on a device.
 */
export const APP_VERSION: string = appConfig.expo.version;

export function versionLabel(): string {
  return `v${APP_VERSION}`;
}
