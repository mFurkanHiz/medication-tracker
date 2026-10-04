#!/usr/bin/env node
// Audits the real web screens with axe-core, in a real browser.
//
// WHY THIS EXISTS
// Acceptance row 34 asks that core workflows be usable at large font sizes, with adequate
// touch targets and meaningful screen-reader labels — and recorded, for weeks, that "no
// formal audit has been run". Reading the markup is not an audit: the accessibility tree a
// screen reader actually gets is computed from the rendered page, so the only honest check
// is one made against a rendered page.
//
// WHAT IT NEEDS
// A running stack on one origin — the static export served with /api proxied to the API,
// which is what apps/web/nginx.conf does in production. Pass the base URL as the first
// argument; it defaults to http://127.0.0.1:3000.
//
// axe-core and playwright-core are NOT repository dependencies, deliberately. CI cannot
// run this without standing up the whole stack, so making every CI install pay for a
// browser driver it never uses would be a cost with no return. Install them where you run
// the audit:
//
//     npm install --no-save axe-core playwright-core
//     node tools/check-accessibility.mjs http://127.0.0.1:3000
//
// Set AXE_CORE_PATH or PLAYWRIGHT_CORE_PATH to point at them elsewhere.
//
// WHAT IT CHECKS
// Every screen behind the sign-in, in both locales, at phone width and at desktop, against
// WCAG 2.0/2.1/2.2 A and AA — which in 2.2 includes target size, the rule this product's
// phone-width layout is most likely to break. Then, separately, the thing axe cannot see:
// the page at double the root font size, which is how somebody who needs large text reads
// it, asserting the layout does not spill sideways.

import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const BASE = process.argv[2] ?? 'http://127.0.0.1:3000';

const axePath = process.env.AXE_CORE_PATH
  ?? require.resolve('axe-core/axe.min.js', { paths: [process.cwd(), import.meta.dirname] });
const playwrightPath = process.env.PLAYWRIGHT_CORE_PATH ?? 'playwright-core';

// Resolved through both shapes on purpose: the package's own entry is an ES module that
// exports chromium by name, but a path pointed straight at its CommonJS file arrives under
// `default` instead, and PLAYWRIGHT_CORE_PATH invites exactly that.
const playwright = await import(playwrightPath);
const chromium = playwright.chromium ?? playwright.default?.chromium;

if (chromium === undefined) {
  throw new Error(`No chromium export from ${playwrightPath}.`);
}

const axeSource = readFileSync(axePath, 'utf8');

/** The tabs in the shell, in order, so a locale change cannot break the selector. */
const SCREENS = ['today', 'inventory', 'counting', 'plans', 'people', 'history', 'reports'];

const VIEWPORTS = [
  { name: 'phone', width: 375, height: 760 },
  { name: 'desktop', width: 1280, height: 900 },
];

const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

const findings = [];

function report(where, violations) {
  if (violations.length === 0) {
    console.log(`  ok   ${where}`);
    return;
  }

  for (const violation of violations) {
    console.log(`  FAIL ${where} — ${violation.id} (${violation.impact}): ${violation.help}`);

    for (const node of violation.nodes.slice(0, 3)) {
      console.log(`         ${node.target.join(' ')}`);
    }

    findings.push(`${where}: ${violation.id}`);
  }
}

/** Enough of a household that the screens are not empty; an empty screen hides faults. */
async function seed(page) {
  return page.evaluate(async () => {
    const post = async (path, body) => {
      const response = await fetch(path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Medication-Client': '1' },
        body: JSON.stringify(body),
      });

      if (!response.ok) {
        throw new Error(`${path} -> ${response.status} ${await response.text()}`);
      }

      return response.json();
    };

    const session = await (await fetch('/api/auth/session')).json();
    const household = session.households[0].id;

    const person = await post(`/api/households/${household}/people`, { name: 'Sentetik Kişi' });
    const definition = await post(`/api/households/${household}/medication-definitions`, {
      name: 'Sentetikol 500 mg',
      form: 'Tablet',
      unit: 'Tablet',
      cautionDoNotTakeWith: 'Sentetik kan sulandırıcı',
      cautionWarning: 'Sentetik eczacı uyarısı',
    });

    await post(`/api/households/${household}/inventory/${definition.id}/stock`, {
      fullPackages: 2,
      capacityNumerator: 20,
      capacityDenominator: 1,
      openedPackages: [],
    });

    await post(`/api/households/${household}/plans`, {
      personId: person.id,
      medicationDefinitionId: definition.id,
      doseNumerator: 1,
      doseDenominator: 2,
      timeZoneId: 'Europe/Istanbul',
      kind: 'Scheduled',
      pattern: 'Daily',
      localTime: '08:00:00',
      dayPeriod: 'Morning',
      mealRelation: 'AfterFood',
      minimumIntervalMinutes: 360,
      instructions: 'Sentetik talimat',
    });

    await post(`/api/households/${household}/administrations`, {
      personId: person.id,
      medicationDefinitionId: definition.id,
      outcome: 'Taken',
      actualQuantityNumerator: 1,
      actualQuantityDenominator: 2,
      occurredAt: new Date().toISOString(),
    });

    return household;
  });
}

async function openScreen(page, index) {
  const buttons = page.locator('nav button');
  await buttons.nth(index).click();
  await page.waitForTimeout(500);
}

async function audit(page, where) {
  const result = await page.evaluate(
    async (tags) => window.axe.run(document, { runOnly: { type: 'tag', values: tags } }),
    TAGS,
  );

  report(where, result.violations);
}

// CHROMIUM_PATH, when set, wins over whatever browser build this playwright-core expects.
// A container that ships one Chromium and a driver that wants a different build number is
// the ordinary case, not an exception.
const browser = await chromium.launch(
  process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {});

try {
  const context = await browser.newContext({ viewport: VIEWPORTS[1] });
  const page = await context.newPage();

  const consoleErrors = [];
  page.on('pageerror', (error) => consoleErrors.push(String(error)));

  await page.goto(BASE, { waitUntil: 'networkidle' });
  await page.addInitScript(axeSource);
  await page.addScriptTag({ content: axeSource });

  // The sign-in screen is the one every user meets first, and the only one an
  // unauthenticated person can reach at all.
  console.log('Signed out');
  await audit(page, 'sign-in (desktop, tr)');

  await page.getByRole('button', { name: /Kayıt olun/ }).click();
  await page.waitForTimeout(400);
  await audit(page, 'register (desktop, tr)');

  const email = `a11y-${Date.now()}@example.invalid`;
  await page.locator('input[type=email]').fill(email);
  const passwords = page.locator('input[type=password]');

  for (let field = 0; field < (await passwords.count()); field += 1) {
    await passwords.nth(field).fill('synthetic-password-1');
  }

  await page.locator('form button[type=submit], button:has-text("Hesap oluştur")').first().click();
  await page.waitForSelector('nav button', { timeout: 20000 });

  await seed(page);
  await page.reload({ waitUntil: 'networkidle' });
  await page.addScriptTag({ content: axeSource });

  for (const locale of ['TR', 'EN']) {
    await page.getByRole('button', { name: locale, exact: true }).click();
    await page.waitForTimeout(300);

    for (const viewport of VIEWPORTS) {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      console.log(`\n${locale} at ${viewport.name} (${viewport.width}px)`);

      for (const [index, screen] of SCREENS.entries()) {
        await openScreen(page, index);
        await audit(page, `${screen} (${viewport.name}, ${locale.toLowerCase()})`);
      }
    }
  }

  // ----------------------------------------------------------- large text ----
  // axe cannot see this one. Somebody who needs large text gets it by raising the
  // browser's font size, and a layout built on fixed pixels answers by spilling
  // sideways — which on a phone means content nobody can reach.
  console.log('\nAt twice the root font size, 375px wide');
  await page.setViewportSize({ width: 375, height: 760 });
  await page.evaluate(() => {
    document.documentElement.style.fontSize = '32px';
  });

  for (const [index, screen] of SCREENS.entries()) {
    await openScreen(page, index);

    const spill = await page.evaluate(() => {
      const root = document.documentElement;
      return root.scrollWidth - root.clientWidth;
    });

    if (spill > 1) {
      console.log(`  FAIL ${screen} spills ${spill}px sideways at 32px root text`);
      findings.push(`${screen}: horizontal overflow at 200% text`);
    } else {
      console.log(`  ok   ${screen} fits`);
    }
  }

  if (consoleErrors.length > 0) {
    console.log(`\nPage errors during the audit:\n${consoleErrors.join('\n')}`);
  }
} finally {
  await browser.close();
}

console.log('');

if (findings.length > 0) {
  console.error(`${findings.length} accessibility finding(s).`);
  process.exit(1);
}

console.log('No WCAG A/AA violations, and the layout holds at twice the root font size.');
