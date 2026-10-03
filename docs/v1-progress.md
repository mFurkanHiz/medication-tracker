# V1 execution checkpoint

Short by design. It exists so a new session can continue the V1 programme without
reconstructing the product history from a long conversation.

## Current state

- Owner-accepted V1: **NOT COMPLETE**. See `docs/v1-acceptance.md` for the authority
  on scope and `docs/adr/0013-v1-domain-rebuild-strategy.md` for why the domain was
  rebuilt rather than extended.
- Production runs main `8ebe4c60a9660fb4cdb57fb31f6b51aa351c2d9f`, deployed 2026-10-03
  from CI run `37125695168` — the first deployment GitHub Actions performed by itself.
  Reports, export and counting are **live**.
- PR #11 (the rebuild), #12 (deployment tooling) and #13 (backup verification) are
  merged into main.
- The API, the web client and the mobile client are all rebuilt against the new model.
- Reports, export and counting (rows 18, 31, 32) are implemented, merged **and
  deployed**.
- PR #9 is **subsumed** by PR #11, not abandoned. Its recurrence rules are carried
  forward onto `TreatmentPlanVersion`. Do not merge PR #9 separately.
- PR #10 (packaged migration SQL fix) is already on main.

## What the rebuild has delivered

Domain, application, persistence and API layers for the package-first model:

- `MedicationDefinition` — catalog only, never person-owned, no longer tablet-only.
- `MedicationPackage` — one row per physical container, with snapshotted capacity and
  unit, a real lifecycle, expiry/lot/barcode/location, separate owner and holder, and
  emptiness derived from the ledger.
- `AdministrationAllocation` — which package paid for a dose, as a queryable fact.
- `AdministrationAllocationCorrection` — the owner-critical "I actually used Box 2"
  path, as an appended reversal plus re-charge that conserves the total.
- Untracked/external administrations, manual source selection, partial and extra doses.
- `PackageConsumptionPolicy` — the deterministic, unit-tested selection order.
- `MedicationRefillPolicy` and `RefillForecast` — low stock, official refill
  eligibility and the refill-gap warning, with depletion walked over real due days.
- Module boundaries replacing `SprintOneEntities.cs`.
- Identity read from the validated principal rather than a rewritten request header.
- A hand-assembled, data-preserving migration plus a CI gate that asserts row by row
  that the production-shaped baseline survived it.

## What is still open

These are `OPEN` or `PARTIAL` in the acceptance contract and have **not** been
narrowed or moved out of V1:

1. **Physical-device acceptance.** Offline mobile and local reminders are implemented
   and type-checked, but reboot, permission revocation, time-zone change, DST and Doze
   behaviour can only be proven on a real Android phone. Nothing here has run on one.
2. **Accessibility** has no formal audit and no screen-reader pass on a device.
3. **Synthetic demo seed** for safe public demonstration.
4. **Mobile has no reports, export or counting surface.** These slices added all three
   to the web client only. Rows 18, 31 and 32 are satisfied — a user can reach every
   behaviour — but the mobile client does not yet show them.
5. **Twelve accounts and households survive the purge, not one.** The care data is
   gone, but every account was kept because keeping the user is what was asked for.
   Eleven of them are almost certainly smoke-test registrations. Narrowing that needs
   the owner to say which account is theirs.

## Next exact action

The synthetic demo seed, then the mobile surfaces.

- Deployment is armed and proven: a merge to `main` deploys by itself, and the care
  data purge has run. The purge stays available for a future reset — commit
  `deploy/purge-care-data.request` to `main` with the line `ERASE-CARE-DATA` in it, or
  dispatch the workflow and type the same, then remove the file again.
- **Synthetic demo seed** (row 35) is the last non-device, non-mobile item. It should
  reuse the shape the browser checks already seed: a household, two people, a scheduled
  and an as-needed medication, three packages, three weeks of mixed outcomes.
- After that, mirroring reports, export and counting onto mobile.

Physical-device acceptance for rows 28 and 29 needs the owner's Android phone and
cannot be produced here.

## Resume protocol

When the owner says **"continue" / "kaldığın yerden devam et"**:

1. Read `AGENTS.md`, `docs/v1-acceptance.md`, and this file.
2. Inspect `git status` and recent commits. Preserve existing work; never restart from
   scratch.
3. Resume the next `OPEN`/`PARTIAL` acceptance row, starting from *Next exact action*.
4. Read only the code and docs the current slice needs.
5. Run targeted tests while editing; run the full gate before a checkpoint or merge.
6. Commit coherently, update this file with evidence and the next exact action, then
   report.
7. Never redefine a required V1 item as "later" without explicit owner approval.

## How the owner wants this project run

Standing instructions, given 2026-10-03. They outlive any one session, which is why
they are here rather than in a conversation.

- **Do the work.** Merges, pushes, branches, deploys and the care-data purge are the
  agent's to perform, not the owner's. Do not ask them to click Merge or to dispatch a
  workflow; if a route is closed, find another that keeps the same safeguards —
  `deploy/purge-care-data.request` is the worked example.
- **Ask when something is genuinely missing or ambiguous**, including when the
  instruction itself has a gap. Do not ask for the sake of asking.
- **The owner does the things only they can do**: generating a key, adding a secret,
  a DNS record, a third-party account. Tell them exactly how, then carry on.
- **This VPS, GitHub account and Notion workspace are shared with other projects.**
  Touch nothing outside this project. Where a shared surface has to be used, use it in
  the way that cannot disturb the others, and prove it afterwards — the deploy's
  outsider-container check is the pattern.
- **Notion is the durable project record.** The owner keeps project management there
  so a different agent or a person can take this over without losing anything.
  `ProjectSample` in the Project Management space is the guide to follow; `Psicologa`
  and `SargasmGames` may be read once to learn the conventions and never touched
  again. Write only inside the **Medication Tracker** project.
- **Address the owner as Furkan**, reply in Turkish, and end a turn with a short
  summary of what was done since their last message.

### Where Notion holds what

`🧩 Project Management` carries four data sources: **Projects** (one page per
project), **Sprints**, **Tasks** and **Research**. Tasks and Research are shared
across every project, so every record must set `Project = Medication Tracker` and no
other project's rows are ever touched.

Populated 2026-10-03: the `💊 Medication Tracker` project page (properties plus a
"Güncel durum" section), a `Secrets & Config (HASSAS)` sub-page, and three Production
tasks covering the deploy automation, the care-data purge, and reports/export/counting.

**The owner has deliberately overridden §G for this project.** ProjectSample's rule is
that Notion holds secret names and locations but never values. Asked directly on
2026-10-03, the owner said to write the real values into the `Secrets & Config
(HASSAS)` sub-page, because that page is how they track them. The rule is theirs and
so is the exception: **do not "correct" that page by stripping its values.** Fill it.

Two consequences follow, and both are written on the page itself. Anyone with access
to that Notion page has access to the server, so sharing the page or adding someone to
the workspace hands over that access too. And git is still out of bounds — the
repository is public, so `docs/secrets.md` keeps the valueless version and
`deploy/known_hosts` holds only the server's public host key.

Notion is read-only history until a session actually has the connector: connectors are
read **when a session starts**, so one enabled mid-conversation does not load.

## Evidence checkpoint

- 2026-10-02: Takeover audit of `a1c761a`. Measured the product at roughly 1,500 lines
  of C# plus 650 of client code outside migration scaffolding; identified CI's packaged
  migration gate, the production deploy path, session identity, `ExactQuantity`,
  advisory-lock serialisation, revisioned counts and PR #9's recurrence rules as worth
  keeping, and the single ambiguous `Medication` entity, the balance-pair package, the
  absent allocation record and the absent correction path as the core defects. Recorded
  in ADR 0013 and ADR 0014; acceptance contract reconciled with the owner's redefined
  scope and rows whose evidence depended on the superseded model re-opened.
- 2026-10-02: Verified the `X-Account-Id` header is **not** an authentication bypass —
  the middleware strips the client's copy before re-deriving it from the session, and
  `AuthenticationBoundaryTests` covers it. It was fragile design, not a live hole, and
  endpoints now read the principal directly instead.
- 2026-10-02: Domain core committed (`3f4d0cc`) with the consumption policy, allocation
  correction planner and extended exact-quantity type. 72 tests passing locally.
- 2026-10-02: Rebuild committed (`a18b8e1`). 99 tests pass locally; 25 PostgreSQL tests
  skipped because this workstation has neither Docker nor a PostgreSQL server, so all
  database, concurrency and migration evidence comes from CI. Web lint, mobile
  typecheck and web production build pass.
- 2026-10-02: Found that the scaffolded migration would have **dropped five populated
  tables** and mis-assigned two `administration_events` column renames by position,
  writing plan-version identifiers into a medication column. Replaced with a
  hand-assembled create-copy-drop plus thirteen backfills; `tests/migrations/`
  now seeds a production-shaped baseline (all pre-rebuild migrations) and asserts
  preservation of every affected table and of the medication total.
- 2026-10-02: Found that the superseded count flow wrote a zero-delta ledger entry
  whenever a count matched. A zero delta is now legal for count adjustments only, and
  the rebuilt count flow still records "counted and matched" rather than discarding it.
- 2026-10-02: `deploy-production.sh` now stops this project's api and web containers
  before applying migrations, because a table rename cannot run safely underneath the
  previous release, and verifies the backup is readable before anything changes. No
  other compose project is touched.
- 2026-10-02: PR #11 CI run **`37036017899`** passed with **124 tests, 0 failed, 0
  skipped** — the first run in which every PostgreSQL integration test executed rather
  than being skipped. Both jobs green. That run covers:
  - the mandatory acceptance scenario end to end (48 total across three packages, the
    opened box chosen automatically, an explicit box for the next dose, the correction
    restoring Box C and leaving the total unchanged, and the next package opening when
    Box C empties);
  - allocation correction refused onto a source without the stock;
  - two concurrent doses contending for one tablet, exactly one succeeding, balance
    never negative;
  - a replayed idempotency key recording one dose and consuming stock once;
  - an untracked external dose writing no ledger entry and no negative stock;
  - cross-household authorisation on every rebuilt read and write;
  - a catalog capacity edit leaving existing packages at their snapshotted capacity;
  - a dose split across packages summing exactly to the amount administered;
  - retiring a package removing its stock through a typed ledger entry;
  - the refill-gap warning (depletion day 5, eligibility day 14, nine-day gap);
  - a count that matched still being recorded, and a correction creating a revision
    while the original stays untouched;
  - weekday/interval schedules including DST boundaries, and off-schedule doses refused;
  - plan edits appending a version with historical administrations unchanged;
  - web lint, mobile typecheck, web production build, both Docker builds with matching
    revision labels;
  - the packaged `migrations.sql` applied twice to a blank PostgreSQL 18 database, and
    twice to a production-shaped baseline of all eleven pre-rebuild migrations seeded
    with synthetic rows, followed by `verify-upgrade.sql` asserting row by row that
    definitions, plans, plan versions, packages, ledger entries, counts, audit history,
    the promoted allocations and the medication total all survived.
- 2026-10-02: Two defects found and fixed only because the PostgreSQL suite actually
  ran: the migration inserted historical allocations before their table existed, and
  the rebuilt harness never applied migrations so every integration test had been
  failing with a 500. A third, smaller one followed — the POST test helper could not
  accept the empty body a 204 correctly carries.
- 2026-10-02: Web client rebuilt against the new API. The 153-line single-component
  tracker is replaced by a typed API client, an exact-quantity module that never
  converts an amount to a float, complete TR/EN dictionaries typed so a missing
  translation fails the build, and screens per concern. Verified in a browser at desktop
  and 375px: sign-in renders, the TR/EN toggle switches the whole interface, touch
  targets and focus rings hold up. Three defects found by running it rather than
  assuming — an unlayered `button { color: inherit }` reset that beat Tailwind's layered
  utilities and left dark-on-dark button labels, three fetch effects that set state
  synchronously, and a locale read copied out of localStorage by an effect instead of
  through `useSyncExternalStore`. The authenticated screens are typed against the API
  client but were not exercised in a browser, because this workstation has no PostgreSQL
  to run the API against.
- 2026-10-02: Final CI on the branch head, run **`37038800495`**, passed with 124 tests,
  0 failed, 0 skipped and both jobs green, covering the rebuilt API, the rebuilt web
  client's lint and production build, both Docker builds and the full migration gate.
  The branch is now self-consistent: API and web agree. Mobile does not.
- 2026-10-02: Mobile client rebuilt offline-first (`df808eb`). A SQLite snapshot, a
  durable outbox whose idempotency key is committed before the request is attempted,
  serialised sync, a Today screen that renders and records with no network, advanced
  source selection, and device-local reminders. Conflicts are surfaced rather than
  resolved. CI run `37044185829` green on both jobs.
- 2026-10-02: Reading the installed `expo-sqlite` rather than trusting documentation
  changed the design three times: `withExclusiveTransactionAsync` opens a second native
  connection so per-connection PRAGMAs do not carry into it and `ON DELETE CASCADE`
  cannot be relied on there; the library sets no `busy_timeout`, so that second
  connection deadlocks against a read on the first; and an unmatched named parameter
  binds silently as NULL. Writes use the shared connection, sync runs are serialised in
  JS, and queue rows are deleted explicitly.
- 2026-10-02: The SDK 57 notification documentation revealed that without
  `setNotificationHandler` a triggered notification is not presented at all. Reminders
  would have silently never shown.
- 2026-10-02: Found and fixed an API gap the mobile work exposed: a second device
  recording the same scheduled slot under a different idempotency key hit the unique
  index and produced a 500. It now replays when the outcome agrees and returns
  `slot_already_recorded` when it does not, with a PostgreSQL test for both.
- 2026-10-02: **Released to production.** main `371447da76f29e336ae3a00e1cc8864e5613d242`
  from CI run `37046495484`; image archive SHA-256
  `ed242eb1318164112b3e02ba5b2cb03b49a81114c4f16ed34e18c9b9defb1ac9`, verified identical
  after transfer. Both running images carry that commit as their revision label.
  Migration result, against the counts read before deploying: 12 migrations applied,
  11 medication definitions (2 archived), 30 packages (5 opened, 25 sealed), 55 ledger
  entries with none left untyped, the 6 zero-quantity acquisitions intact, 15 plans,
  15 plan versions, 10 administrations, and 10 historical consumptions promoted to
  allocations. The five superseded tables are gone. The public smoke test passed,
  covering three packages totalling 48, a one-tap dose drawing from the opened box, a
  correction that restored the wrong box and left the total unchanged while retaining
  the superseded allocation, an untracked dose, the refill gap, and the workspace and
  activity reads. All 26 containers on the VPS were running at the final check; only
  this project's three were touched.
- 2026-10-02: **Reports and export (rows 31 and 32) delivered**, with the API, the web
  screen and the tests in one slice. Adherence is not a count of administration rows: the
  schedule is replayed over the period from the effective-dated plan versions, so a
  missed dose is distinguishable from a day the plan never asked for. Three decisions
  came out of that and are each pinned by a test — the version in force on a day governs
  that day rather than the newest one; a plan stopped mid-period keeps the slots it
  placed before it stopped, so archiving a medication cannot rewrite last week; and an
  as-needed plan places no slots at all, so it can never be reported as missed. A dose
  and the slot it answers are placed in the period separately, because a dose recorded
  after midnight answers the previous day's slot — counting it in today's totals is
  right, calling yesterday's slot missed is not.
- 2026-10-02: This workstation had **neither a .NET SDK nor PostgreSQL** at the start of
  the session, and the .NET download host is blocked by the environment's egress policy.
  Both came from Ubuntu's own repositories instead (`dotnet-sdk-10.0`, `postgresql`), so
  for the first time the whole suite ran locally rather than only in CI: **153 tests, 0
  failed, 0 skipped**, including every PostgreSQL integration test. Local PostgreSQL is
  16 where CI is 18, so CI remains the authority on the packaged-migration gate.
- 2026-10-02: Two defects were caught by compiling and running rather than by reading.
  The export serialises by hand instead of through the usual result, so it did not
  inherit the web defaults and was emitting `Id` where every other endpoint emits `id`;
  it now uses `JsonSerializerDefaults.Web`. And a test asserted twelve slots where the
  schedule really places eleven — the test was wrong, not the walker.
- 2026-10-02: The reports screen was **driven in a real browser against a live API**,
  which the previous session could not do for any authenticated screen for want of a
  database. Signed in, switched locales, and read the rendered numbers back: 21 planned,
  12 on schedule, 5 not recorded, 4 skipped, 4 partial, 57% (12/21) — matching the seeded
  history exactly — the as-needed medication showing "nothing scheduled" and no invented
  misses, the refill-gap badge, and a real 39 KB file downloaded from the export button.
  Checked at 1280px and at 375px, in Turkish and English, with no untranslated string
  left behind and no console error beyond the expected pre-sign-in 401.
- 2026-10-02: **Inventory counting surfaced (row 18, and the last gap in row 30).** The
  write path already existed and was proven; nothing could read a count back, so the
  client had no way to know which batch was still correctable. A
  `GET /inventory/count-sessions` was added, reporting each accepted count with what the
  ledger expected, what was found, the signed difference, the box ordinal where one was
  counted, and `isRevisable` — the same linear-chain rule the write path enforces, so
  the client never offers a correction the server is about to refuse. Five tests cover
  it, cross-household refusal included.
- 2026-10-02: The counting screen counts a whole medication by default and reconciles
  individual boxes under an advanced disclosure. Browser-driven against a live API: an
  unreadable amount is refused before anything is written, 38 → 35 was recorded,
  corrected to 36 as revision 2 with the original left showing its own −3 and marked
  superseded, then Box 1 reconciled 18 → 19 and Box 2 20 → 18, with the household total
  following to 37. Two usability defects were found by looking at the rendered page
  rather than the code: the advanced disclosure's summary and the button inside it
  carried the same words while doing different things, and three box inputs all
  announced as "Saydığınız" with nothing to tell them apart. Both fixed; each box field
  is now addressable by its own ordinal.
- 2026-10-02: **This session could not deploy.** The environment's network policy denies
  the VPS SSH port and `medicationtracker.rapidconfigs.com` itself, and the session has
  no `.env.production`, no image artifact and no SSH key. The preflight is prepared in
  `docs/preflight.md` for the owner to execute; nothing was deployed and nothing on the
  VPS was touched.
- 2026-10-02: **Deployment automated.** A merge to `main` now runs CI's `deploy` job,
  which ships the artifact it just built together with the deploy script and compose
  file from that same commit — so an older script can never run against a newer image,
  and nothing depends on the state of a checkout on the host. The host key comes from a
  secret and an unknown one aborts rather than prompting; the archive's SHA-256 is
  compared on both sides and a mismatch deploys nothing; only one deploy runs at a
  time and a running one is never cancelled.
- 2026-10-02: The owner's constraint — this VPS carries other people's containers and
  sites behind one nginx — was treated as a thing to prove, not to assert. The script
  was rehearsed end to end twice with Docker mocked, and every call it makes was
  audited: each `docker compose` is scoped by `--project-name`, the only stop is
  `stop api web`, every other `docker` call names a `medication-tracker-*` resource or
  a container the script itself created, and there is no `prune`, no `system`, no
  network or volume removal, and no mention of nginx. The script now also snapshots
  the containers that are not ours before it starts and fails, naming the container,
  if one of them is gone by the end; a new container appearing is not damage. Seven
  cases of that guard were unit-tested, and the two full rehearsals — healthy and
  collateral — both behaved correctly.
- 2026-10-02: The deploy job is inert until the owner sets `DEPLOY_ENABLED`, so a
  merge is never red for a deployment nobody configured. The SSH steps have been
  syntax-checked but **never run against the real host**, which is the largest
  remaining unknown and the reason the first deploy should be approved live through
  the `production` environment.
- 2026-10-03: **A one-off purge of the superseded care data was written and proven.**
  The owner asked for the pre-rebuild rows to go — they describe the product that was
  rejected, and reading them beside package-first data misleads — while keeping the
  accounts that sign in. `deploy/purge-care-data.sql` and its wrapper erase people, the
  catalog, packages, the ledger, plans, doses, allocations, corrections, counts, refill
  settings and sync receipts, and keep accounts, sessions, households, memberships and
  subscriptions.
  - The foreign-key graph was read out of the database rather than assumed. Every key
    between these tables is `RESTRICT` and two are self-referencing, so a row-by-row
    delete would have to unpick the order by hand; one `TRUNCATE` over the complete set
    does not. `CASCADE` is deliberately omitted, so a future table referencing one of
    these fails the statement by name instead of being erased by accident.
  - Nothing that survives references anything that goes: accounts and households are
    the *parents* of care data, never its children. That is what makes the purge safe.
  - Proven against a real PostgreSQL database named as production is, seeded through
    the API with two households, 18 administrations, 22 ledger entries and a count:
    every care table went to zero, accounts/sessions/households/memberships were
    untouched, and the application then signed in with the original password and
    created a person, a medication, stock, a plan and a dose, with counting, reports
    and export all working. The database guard was confirmed by watching it refuse a
    wrongly-named database.
  - The wrapper was rehearsed with Docker mocked in three scenarios: success, a failing
    transaction, and an unrelated container disappearing. The failing case is the one
    that matters — the site comes back up and the message says the data is unchanged.
  - **Not yet run against production**, which this session cannot reach.
- 2026-10-02: The first deployment attempt halted at backup verification and changed
  nothing — `pg_restore -l /dev/stdin` cannot read a custom-format archive from a pipe.
  The backup it had already written was confirmed sound (188 entries) and the outgoing
  images had been tagged `:previous`, so the rollback path was intact throughout. Fixed
  in PR #13 by dumping and checking inside the container against a real file.
- 2026-10-03: **GitHub Actions deployed to the VPS by itself, for the first time.**
  CI run `37125695168` on `8ebe4c60` ran all four jobs green and put reports, export
  and counting live. Evidence from the deploy job's own log:
  - The image archive's SHA-256 matched on both sides of the transfer, so what ran on
    the host is what the runner built.
  - The pre-migration backup was dumped and its table of contents read back inside the
    database container before the schema was touched.
  - The packaged migration applied (`DO … COMMIT`), then `api` and `web` were recreated
    from the new images and the readiness check passed. `database` stayed up throughout
    — three weeks old and healthy — because the script stops only `api web`.
  - `Deploy complete. deploy-production.sh left 23 container(s) from other projects
    untouched.` The owner's other sites and containers were the stated constraint, and
    this is the line that answers it: the outsider snapshot taken before the deploy
    matched the one taken after.
  - From outside: `GET / -> 200` and `GET /api/auth/session -> 401`. The 401 is the
    useful one — it proves the request reached the API through nginx, where 200 or 502
    would mean it had not.
  - `deploy/smoke-test.ps1` did **not** run: it is behind the `run_smoke_test` dispatch
    input, and a session cannot dispatch (403). One manual run with that box ticked
    would cover the behaviours end to end on the live site, at the cost of one
    synthetic household.
- 2026-10-03: Getting there took three corrections, each a real defect rather than a
  retry:
  - All five settings had been stored as **secrets**, but three were read only from the
    `vars` context. `DEPLOY_ENABLED` sat in a job-level `if:`, where GitHub does not
    expose `secrets` at all, so the deploy job would have **skipped in silence** with
    nothing in the log to explain it. A `gate` job now reads the switch in a step,
    where both contexts are visible, and publishes a plain yes/no. Its answer is not
    the switch's own value on purpose: a secret holding `true` is masked everywhere,
    so an output of `true` would print as `***`.
  - `VPS_HOST`, `VPS_USER` and `PUBLIC_URL` now read `vars.X || secrets.X`. An address
    and an on/off switch are not credentials; insisting on the tidier place would have
    been a correction to the owner rather than to the code.
  - There was no host key, and no way for the owner to read one: their Windows
    `ssh-keyscan` cannot negotiate with this server. `deploy/authorise-ssh.sh` now takes
    the key from the secret, else from `deploy/known_hosts` in the same commit, and with
    neither it reads what the server offers, prints it with its fingerprint, and fails
    **before** transferring anything. It never falls back to trusting the network:
    `StrictHostKeyChecking=yes` means nothing once any key is accepted. Run
    `37125185792` printed the key that way, and it is pinned as
    `SHA256:xbaKZHF43tdM9iRATyJ+tUzGr9GPcC1A7411tdPcMRQ`, stored without a host field
    so the script writes the connected address in front of it.
  - That pin is trust-on-first-use, read by a runner. The one comparison it cannot make
    for itself is recorded in the file: `ssh-keygen -lf
    /etc/ssh/ssh_host_ed25519_key.pub` on the server.
- 2026-10-03: **The superseded care data is gone from production.** Purge run
  `37128421790`, confirmed by `deploy/purge-care-data.request` on `main`, with the
  before/after table from the SQL itself as the evidence:

  | table | before | after | |
  | --- | --- | --- | --- |
  | accounts | 12 | 12 | kept |
  | households | 12 | 12 | kept |
  | memberships | 12 | 12 | kept |
  | people | 11 | 0 | purged |
  | medications | 12 | 0 | purged |
  | packages | 33 | 0 | purged |
  | ledger entries | 61 | 0 | purged |
  | plans | 16 | 0 | purged |
  | administrations | 12 | 0 | purged |

  The backup `.deploy/backups/pre-purge-20261003T140544Z.dump` was dumped and read
  back before anything was removed, and is the only way back. `api` and `web` were
  stopped for the few seconds the locks were held and came back; `database` stayed up.
  Afterwards the site answered `200` and the anonymous session read `401`.
  - **Twelve accounts and twelve households survive, not one.** The owner asked for
    the user to be kept, so the purge kept every account — but eleven of these are
    almost certainly synthetic households left by smoke-test runs, each of which
    registers one. Removing them is a narrower, different deletion than the one that
    was asked for, so it was not done. It needs the owner to say which account is
    theirs.
