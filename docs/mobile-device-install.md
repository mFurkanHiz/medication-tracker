# Installing the mobile app on the owner's Android phone

The owner chose, on 2026-10-05, to test the phone app over USB from their own laptop
rather than through EAS Build (which has a build quota). This page is the runbook for the
session that does it.

**Which session.** A cloud session cannot see a phone plugged into the owner's computer.
The install is done by a session running *on that computer* — the Claude Desktop app, or
`claude remote-control` in the repository folder — and that session does the work; the
owner plugs the phone in and approves the prompts the phone itself shows. Everything the
cloud session can prepare (the code, this page, the version) is prepared here.

## What the computer needs

- Node 22 and pnpm (the versions `package.json` pins), a JDK, and the Android SDK with
  `platform-tools` (that is where `adb` lives), with `ANDROID_HOME` set. Android Studio
  installs all of it; the command-line tools alone also do. Use the JDK and SDK versions
  the installed Expo SDK's own documentation lists — `apps/mobile/AGENTS.md` points at
  the exact version, and nothing in this page should be preferred to it.
- On the phone: developer options on, **USB debugging** on, and on some vendors also
  "install via USB" and "USB debugging (security settings)". The first connection asks
  the phone to trust the computer; say yes and tick "always".

## Steps

1. **See the phone.** `adb devices` must list it as `device`, not `unauthorized` (trust
   prompt still open on the phone) or `offline` (replug the cable).
2. **Remove the earlier app, and only that one.** An older build named *MedicineTracker*
   may be on the phone from a previous tool. List what is installed —

   ```text
   adb shell pm list packages | findstr /i medic        (Windows)
   adb shell pm list packages | grep -i medic           (macOS, Linux)
   ```

   — and uninstall the old package by the name that listing prints: `adb uninstall
   <that.package.name>`. Ours is `com.rapidconfigs.medicationtracker` (from
   `apps/mobile/app.json`) and is not installed yet; nothing else on the phone is touched.
3. **Build, install and launch** from the repository root:

   ```text
   pnpm install
   pnpm --filter mobile android:release-device
   ```

   This runs `expo run:android --device --variant release`: Expo generates the native
   project under `apps/mobile/android` (git-ignored), Gradle builds a release APK with the
   JavaScript bundled in — so the app runs on its own, no Metro server needed — signs it
   with the computer's own debug keystore, installs it over USB and opens it. The first
   build downloads Gradle and the Android dependencies and can take a long time; later
   builds are minutes. With several devices attached the command asks which one.
4. **Point it at the server.** The app talks to the production site by default
   (`DEFAULT_API_URL` in `apps/mobile/src/lib/api.ts`). To use another server, put
   `EXPO_PUBLIC_API_URL=https://...` in `apps/mobile/.env.local` (git-ignored) before
   building; the value is baked in at build time.
5. **Check it.** Sign in, pull the Today list, tap *Enable reminders* and accept the
   notification permission, then background the app. `adb logcat -s ReactNativeJS` shows
   the JavaScript log if something looks wrong.

## Updating

Run step 3 again. The same computer signs with the same debug keystore, so the new build
installs over the old one and keeps its data. A build from a *different* computer has a
different signature and Android refuses it as an update: uninstall ours first
(`adb uninstall com.rapidconfigs.medicationtracker`), which also discards the device's
local database and any queued doses — sync before doing that.

## Development loop, when needed

`pnpm --filter mobile android:device` builds the debug variant and starts Metro on the
computer; the phone loads JavaScript from it over USB and reloads on save. That is for
changing the app, not for carrying it around: a debug build with Metro closed shows a red
screen.

## What this does not give

- No store listing, no EAS build, no internal distribution: the app reaches exactly the
  phones somebody plugs into a computer with this repository on it. That is the channel
  the owner chose for now; ADR 0015's reason for deferring mobile is answered only to that
  extent.
- No exact alarms: the notification library does not request `SCHEDULE_EXACT_ALARM`, so
  a reminder may be deferred under Doze. These are adherence reminders, not alarms.
- The physical-device acceptance rows (28, 29 in `docs/v1-acceptance.md`) stay
  `DEFERRED` until they are actually run on the phone: reboot, permission revocation,
  time-zone change, daylight-saving transition, Doze.
