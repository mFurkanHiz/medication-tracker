#!/usr/bin/env node
// A reproducible synthetic demo household, built entirely through the public API.
//
// WHY THROUGH THE API AND NOT SQL
// Writing rows directly would let this script create states the domain itself cannot
// reach — a package whose balance disagrees with its ledger, a plan version that no
// endpoint would accept — and a fixture that drifts from the product is worse than no
// fixture, because it looks like evidence. Everything below goes through the same
// validation, the same ledger and the same audit trail a person's clicks would.
//
// WHY IT CANNOT DAMAGE ANYTHING
// The seed never touches existing data. It registers its own account with a fresh
// address in the .invalid top-level domain — reserved by RFC 2606, so it can never be
// anybody's real mailbox — and writes only inside the household that account owns. There
// is no path through this file that updates or deletes a row it did not create.
// Loopback is the only host it will talk to unless somebody deliberately overrides that.
//
// NO REAL HEALTH DATA. Every name, medicine, dose and note below is invented, and the
// people are labelled "Demo" so nobody can mistake a screenshot for a real household.
//
//   node tools/demo-seed.mjs                      # against http://127.0.0.1:8080
//   node tools/demo-seed.mjs --url http://...     # loopback only, unless overridden

const DEFAULT_URL = 'http://127.0.0.1:8080';
const CONFIRM = 'I-UNDERSTAND-THIS-WRITES-DEMO-DATA';

const args = process.argv.slice(2);
const urlArg = args.indexOf('--url');
const baseUrl = (urlArg >= 0 ? args[urlArg + 1] : DEFAULT_URL).replace(/\/$/, '');
const allowRemote = args.includes('--allow-remote');

function refuseUnlessSafe() {
  const { hostname } = new URL(baseUrl);
  const loopback = hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1';

  if (loopback) {
    return;
  }

  // Safe by default. A demo seed pointed at a live site would leave synthetic households
  // in somebody's production database for ever — this project already carries a cleanup
  // chore from exactly that happening with smoke tests.
  if (!allowRemote || process.env.MEDICATION_TRACKER_DEMO_SEED_CONFIRM !== CONFIRM) {
    console.error(
      `Refusing to seed ${baseUrl}: it is not loopback.\n` +
        `This writes a whole household of demo data and never deletes anything.\n` +
        `If that is genuinely what you want, pass --allow-remote and set\n` +
        `MEDICATION_TRACKER_DEMO_SEED_CONFIRM=${CONFIRM}`,
    );
    process.exit(1);
  }
}

let cookie = '';

async function call(method, path, body) {
  const response = await fetch(`${baseUrl}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      // The API requires this header on mutations; its absence is what stops a
      // cross-site form post from reaching an endpoint at all.
      'X-Medication-Client': '1',
      ...(cookie ? { cookie } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  const setCookie = response.headers.getSetCookie?.() ?? [];
  if (setCookie.length > 0) {
    cookie = setCookie.map((value) => value.split(';')[0]).join('; ');
  }

  const text = await response.text();

  if (!response.ok) {
    throw new Error(`${method} ${path} -> ${response.status} ${text}`);
  }

  return text ? JSON.parse(text) : {};
}

const post = (path, body) => call('POST', path, body ?? {});

/** Local-midnight-anchored day offsets, so a demo always looks like it is happening now. */
function daysAgo(days, hour = 9) {
  const when = new Date();
  when.setDate(when.getDate() - days);
  when.setHours(hour, 0, 0, 0);
  return when.toISOString();
}

async function seed() {
  refuseUnlessSafe();

  const stamp = Date.now();
  const email = `demo-${stamp}@example.invalid`;
  const password = 'demo-household-password-1';

  const { householdId } = await post('/api/auth/register', {
    email,
    password,
    confirmPassword: password,
  });

  const h = `/api/households/${householdId}`;
  const id = (created) => created.id;

  // ---------------------------------------------------------------- people ----
  const adult = id(await post(`${h}/people`, { name: 'Demo Yetişkin' }));
  const child = id(await post(`${h}/people`, { name: 'Demo Çocuk' }));

  // ----------------------------------------------------------- medications ----
  // A daily tablet, taken in halves: the fractional-quantity case the whole inventory
  // model exists for.
  const tablet = id(
    await post(`${h}/medication-definitions`, {
      name: 'Demo Tansiyon Tableti',
      form: 'Tablet',
      unit: 'Tablet',
      strength: '5 mg',
      brand: 'Demo İlaç',
      defaultPackageCapacityNumerator: 28,
      defaultPackageCapacityDenominator: 1,
      category: 'Demo kategori',
      cautionThingsToDo: 'Her sabah aynı saatte, bir bardak dolusu su ile al.',
      cautionThingsToAvoid: 'Aldıktan sonra aniden ayağa kalkma.',
    }),
  );

  // A painkiller: as-needed, with the household's own six-hour gap and the warnings they
  // were given. This is the medicine that shows the Sprint 3 work.
  const painkiller = id(
    await post(`${h}/medication-definitions`, {
      name: 'Demo Ağrı Kesici',
      form: 'Tablet',
      unit: 'Tablet',
      strength: '100 mg',
      defaultPackageCapacityNumerator: 15,
      defaultPackageCapacityDenominator: 1,
      cautionDoNotTakeWith: 'Kan sulandırıcı ile birlikte alınmayacak.\nAynı gün başka bir ağrı kesici alınmayacak.',
      cautionFoodsToAvoid: 'Greyfurt ve greyfurt suyu.',
      cautionThingsToDo: 'Tok karnına al.',
      cautionWarning: 'Demo eczacı notu: mideyi rahatsız ederse bırak ve doktora sor.',
    }),
  );

  // A syrup, so the demo is not all tablets: millilitres, and a part-used bottle.
  const syrup = id(
    await post(`${h}/medication-definitions`, {
      name: 'Demo Çocuk Şurubu',
      form: 'OralLiquid',
      unit: 'Milliliter',
      strength: '100 mg/5 mL',
      defaultPackageCapacityNumerator: 100,
      defaultPackageCapacityDenominator: 1,
      cautionThingsToDo: 'Kullanmadan önce şişeyi çalkala.',
    }),
  );

  // ----------------------------------------------------------------- stock ----
  await post(`${h}/inventory/${tablet}/stock`, {
    fullPackages: 1,
    capacityNumerator: 28,
    capacityDenominator: 1,
    // A half-used blister beside a sealed one: the everyday state of a medicine cupboard.
    openedPackages: [{ remainingNumerator: 11, remainingDenominator: 1 }],
  });

  await post(`${h}/inventory/${painkiller}/stock`, {
    fullPackages: 2,
    capacityNumerator: 15,
    capacityDenominator: 1,
  });

  await post(`${h}/inventory/${syrup}/stock`, {
    fullPackages: 0,
    capacityNumerator: 100,
    capacityDenominator: 1,
    openedPackages: [{ remainingNumerator: 60, remainingDenominator: 1 }],
  });

  // ----------------------------------------------------------------- plans ----
  const morningPlan = id(
    await post(`${h}/plans`, {
      personId: adult,
      medicationDefinitionId: tablet,
      doseNumerator: 1,
      doseDenominator: 2,
      timeZoneId: 'Europe/Istanbul',
      kind: 'Scheduled',
      pattern: 'Daily',
      localTime: '08:00:00',
      mealRelation: 'BeforeFood',
      instructions: 'Demo talimat: kan basıncı ölçümünden sonra al.',
    }),
  );

  await post(`${h}/plans`, {
    personId: adult,
    medicationDefinitionId: painkiller,
    doseNumerator: 1,
    doseDenominator: 1,
    timeZoneId: 'Europe/Istanbul',
    kind: 'AsNeeded',
    pattern: 'Daily',
    dayPeriod: 'Evening',
    mealRelation: 'FullStomach',
    minimumIntervalMinutes: 360,
  });

  // A child's course that has been set aside — the Sprint 2 pause, shown rather than
  // described.
  const syrupPlan = id(
    await post(`${h}/plans`, {
      personId: child,
      medicationDefinitionId: syrup,
      doseNumerator: 5,
      doseDenominator: 1,
      timeZoneId: 'Europe/Istanbul',
      kind: 'Scheduled',
      pattern: 'SelectedWeekdays',
      weekdayMask: 0b0111110,
      dayPeriod: 'Night',
      instructions: 'Demo talimat: kürü bitirmeden bırakma.',
    }),
  );

  // --------------------------------------------------------------- history ----
  // Enough past doses for the adherence report to have a shape: mostly taken, one
  // skipped, and one day simply missing — which is what a real week looks like, and what
  // a report that only ever shows 100% would hide.
  for (const day of [6, 5, 4, 2, 1]) {
    await post(`${h}/administrations`, {
      personId: adult,
      medicationDefinitionId: tablet,
      planVersionId: undefined,
      outcome: 'Taken',
      actualQuantityNumerator: 1,
      actualQuantityDenominator: 2,
      occurredAt: daysAgo(day, 8),
    });
  }

  await post(`${h}/administrations`, {
    personId: adult,
    medicationDefinitionId: tablet,
    outcome: 'Skipped',
    occurredAt: daysAgo(3, 8),
  });

  await post(`${h}/administrations`, {
    personId: adult,
    medicationDefinitionId: painkiller,
    outcome: 'ExtraDose',
    actualQuantityNumerator: 1,
    occurredAt: daysAgo(0, Math.max(0, new Date().getHours() - 1)),
  });

  await post(`${h}/plans/${syrupPlan}/paused`, { isPaused: true });

  // ------------------------------------------------- a box lost, then found ----
  // The Sprint 4 slice, left in its finished state so the ledger shows the pair.
  const workspace = await call('GET', `${h}/workspace`);
  const painkillerRow = workspace.medications.find((row) => row.id === painkiller);
  const spareBox = painkillerRow.packages[painkillerRow.packages.length - 1].view.id;

  await post(`${h}/inventory/packages/${spareBox}/retire`, {
    state: 'Lost',
    reason: 'Demo: çantada bulunamadı',
  });
  await post(`${h}/inventory/packages/${spareBox}/reinstate`, {
    reason: 'Demo: arabada bulundu',
  });

  console.log('Synthetic demo household seeded.');
  console.log(`  url       ${baseUrl}`);
  console.log(`  email     ${email}`);
  console.log(`  password  ${password}`);
  console.log(`  household ${householdId}`);
  console.log('All data is synthetic. Sign in with the address above to see it.');

  // Referenced so a future edit that drops the plan still fails loudly rather than
  // silently seeding a household with nothing to show on Today.
  if (!morningPlan) {
    throw new Error('The morning plan was not created.');
  }
}

seed().catch((error) => {
  console.error(error.message ?? error);
  process.exit(1);
});
