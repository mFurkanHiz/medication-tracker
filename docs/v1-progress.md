# V1 execution checkpoint

Short by design. It exists so a new session can continue the V1 programme without
reconstructing the product history from a long conversation.

## Current state

- Owner-accepted V1: **accepted by the owner on 2026-10-05 ("kabul"); one row left.**
  The table stands at 37 DONE, 1 PARTIAL, 2 DEFERRED, 0 OPEN; the PARTIAL row is 38, the
  final commit's CI, which closes on the commit the release is cut from. Row 37 landed in
  production on 2026-10-05 (run `37337177964`): the API connects as a confined role. The
  release is cut as `v1.0.1` next — the owner kept the retired `v1.0.0` tag untouched
  (ADR 0017). `docs/v1-acceptance.md` is the authority on scope; ADR 0013
  explains the rebuild, ADR 0015 the owner-approved mobile deferral.
- Production tracks `main`: every merge deploys by itself, so the live revision is
  whatever `main` last squashed to — `8775cf1` (PR #59, CI run `37337177804`) as of
  2026-10-05, with migrations 1–18 applied and the API on the confined database role.
  Documentation-only merges do not redeploy (`paths-ignore` on push).
- **The owner's live-test round of 2026-10-05 is closed.** Sprint 7 shipped every note
  and decision (D1–D6) in seven slices (PR #50–#55, migrations 15–18), then the general
  review the owner asked for and the mobile preparation (PR #56). Notes, evaluation and
  decisions: `docs/owner-feedback-2026-10-05.md`. Three review findings are Backlog
  (box-to-box transfer, the count-batch race answering 500, the heavy per-card
  disclosure), not defects.
- The mobile client resumed on 2026-10-05 on the owner's word ("Mobile başla"). The
  owner chose USB from their own computer over EAS Build; the first slice (schema v3 and
  the server's due-day rule on the phone, PR #58) is in; the install itself runs from a
  session on the owner's computer (`docs/mobile-device-install.md`), which a cloud
  session cannot do. Plan and progress: `docs/mobile-resume-plan.md`.

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

These are `OPEN` or `PARTIAL` in the acceptance contract and have **not** been narrowed
or moved out of V1. None is ordinary coding work:

1. **Row 38 — the final V1 commit passes CI.** A condition, not work: it closes on the
   commit `v1.0.1` is cut from.

Row 37 landed in production on 2026-10-05 (run `37337177964`, delegated by the owner).
Rows 30, 39 and 40 were accepted by the owner the same day ("kabul").

From the live test nothing is open: the defect and every candidate change shipped in
Sprint 7. Open beyond V1 and decided at acceptance: the retired `v1.0.0` tag (ADR 0017,
retire and recreate it, or name the first accepted release `v1.0.1`).

## Next exact action

1. **Finish the `v1.0.1` cut:** PR #61 carries the plumbing; once it merges and its CI is
   green, tag `v1.0.1` on that merge commit, then record row 38's run and the complete
   table in a documentation PR. Retired `v1.0.0` stays where it is.
2. **Mobile continues** on the owner's computer: the install per
   `docs/mobile-device-install.md`, then the physical-device acceptance rows (28, 29) and
   `mobile-v1.0.1` at parity.
3. **Rotation, when wanted:** re-running the *Apply least privilege* workflow (dispatch
   with the confirmation, or the request file again) gives the confined role a new
   password and restarts only the api container.

Any defect the owner reports from the live site or the phone takes precedence over new
scope.

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
- 2026-10-03: **Production now holds one account.** Getting there took four runs, and
  the first two were wrong in a way worth recording, because each wrong answer was
  caught by a guard rather than by luck.
  - The care-data purge kept all twelve accounts, as asked. The assumption that eleven
    were smoke-test leftovers was wrong: matching the smoke test's own prefix
    (`synthetic-smoke-`) removed exactly **one** (run `37132135711`).
  - Keeping only the owner's account by the digest of the address they gave refused:
    `the owner digest matched no account` (run `37132572430`). Nothing was deleted and
    the site never went down. The owner had never registered on the rebuilt production
    with that address.
  - So a read-only `REPORT-ACCOUNTS` mode was added rather than a third guess. It
    prints each account with its address **masked** — these job logs are world readable
    on a public repository — plus the full digest, which discloses nothing by itself and
    lets any candidate address be checked by hashing it. Run `37133260216` showed ten
    accounts at `@example.invalid` with four different prefixes, and one at
    `test@medtracker.com`.
  - Ten of those needed no decision from anybody: RFC 2606 reserves `.invalid` so that
    it can never be registered or routed. Matching the whole domain rather than one
    tool's prefix took production from eleven accounts to one (run `37133573790`):
    accounts, households and memberships 11→1, sessions 7→3.
  - `test@medtracker.com` survives with the medication and package created after the
    purge. Whether that account stays is the owner's call.
  - The cleanup machinery is one workflow with three confirmations — two deletions and
    the read-only report — each naming one reviewed SQL file, and refusing a request
    file that names more than one. Every deletion backs up and verifies the backup
    first, runs in one transaction, refuses to leave the system without an account or
    an account without a household, and leans on the `RESTRICT` foreign keys so that a
    household still owning care data fails the statement by name instead of being
    silently orphaned.

- 2026-10-03: The owner tested the live site and reported two defects. Both were real,
  and both were in the web client only — no API, schema or migration change.
  - **A plan could not be given a time of day.** The day-period select existed, but its
    own empty option was labelled `exactTime` — "Saat", the same word as the field
    beside it — and choosing a period merely *disabled* the neighbouring clock instead
    of removing it. The screen asked two timing questions where the domain allows one
    answer. It is now a single `whenInDay` question whose options are Kesin saat,
    Sabah, Öğle, İkindi, Akşam, Gece and Yatmadan önce, with the clock rendered only
    when the answer is a clock. An as-needed plan hid both fields silently, which reads
    as a missing feature; it now says the dose is recorded from the Today screen.
  - **Adding stock could only count whole boxes.** The opened-package rows sat inside
    `<Advanced>`, so "one full box of 20 plus an opened one with 8 left" looked
    impossible, as did adding a box found later. The rows are now in the open, the
    preview states the unit total as well as the box count, summed through
    `addQuantities` as exact fractions, and one line says the form can be reopened
    whenever stock arrives.
  - Verified on the rendered screens, not by reading the code: a local stack
    (PostgreSQL + API + the static export behind a proxy) driven with Playwright. One
    full box of 20 plus an opened box of 8 previewed as `2 kutu — 28 tablet` and saved
    as `20/20` + `8/20`; reopening the form for a found box of 13 added it without
    disturbing the first two; two rounds of both left `82 tablet / 6 kutu`, exact. The
    period select offered all six Turkish periods, choosing one removed the clock, and
    a saved evening plan read back on the card as `Akşam`. No console errors anywhere.
  - Caught while re-reading the diff: both new hint paragraphs used `text-muted`, which
    is **not** a token in this theme (`--color-ink-muted` is), so they would have
    rendered at full ink instead of secondary. Fixed before the commit, and re-shot to
    confirm. The orphaned `dayPeriod` label key was removed from both locales.
  - Gate before the commit: `dotnet test` **158 passed, 0 failed, 0 skipped** — run
    against a real PostgreSQL by setting `MEDICATION_TRACKER_TEST_POSTGRES`, so the 39
    integration tests ran rather than skipping as they do by default on a workstation
    with no database. Lint, mobile typecheck and web build all clean.

- 2026-10-03 (ikinci tur): the owner challenged the as-needed explainer shipped earlier the
  same day — "Ne demek gerektiğinde alınan ilaçlarda saat olmaz? Tok karnına alınamaz mı
  ilaç?" — and asked three further things. Five subsystems were mapped before anything was
  changed, and the mapping produced one finding worth more than the features.
  - **The as-needed restriction was never a domain rule.** `RecurrenceRule.IsValid`'s
    AsNeeded branch constrains only Pattern, WeekdayMask and IntervalDays; the plan-version
    constructor adds nothing; and of the six check constraints on `treatments.plan_versions`
    the only one naming the kind (`ck_plan_versions_schedule`) *relaxes* the requirement for
    AsNeeded rather than restricting it. The gate existed solely in `Plans.tsx`. The line I
    wrote asserted a rule the system does not have, which is worse than a missing feature.
  - What *is* real: `ScheduledSlots.Within` skips any version whose `Recurrence.Kind` is not
    `Scheduled`, and `PlanVersionSlice` carries only version number, recurrence, clock and
    zone — there is no member a day period or food relation could travel in. So guidance can
    never manufacture a missed dose. Both halves are now pinned by tests rather than left to
    a reviewer: the stored vocabulary, and the structural absence from the replay.
  - A plan of either kind now records a day period and a food relation. For a scheduled plan
    the period is the schedule; for an as-needed one it is a preference, labelled as one, with
    no clock offered — a dose fixed to a time is a scheduled dose. Food timing left the
    Advanced disclosure. On Today the big label still reads "Gerektiğinde", with the
    preference on the quiet line as "tercihen Akşam", so a preference cannot read as an
    appointment.
  - `MealRelation.FullStomach` ("Tok karnına") added. The owner is right that it is broader
    than AfterFood. **No migration**: both enums are persisted through
    `HasConversion<string>()` into `character varying(20)` with no CHECK on their values and
    no native PostgreSQL enum anywhere in the repository, so a new member is a new string in
    an existing column.
  - **Archiving is a one-way door and said so nowhere.** `CatalogEndpoints` archive sets
    `ArchivedAt` and then soft-deletes every active plan for that medication; restore clears
    `ArchivedAt` only, and no route can revive a deleted plan (`PUT /plans/{planId}` filters
    `DeletedAt == null`; there is no un-delete). Stock, packages, ledger and administrations
    all survive. The button fired on first click with no confirmation, from inside the
    "Kutuları göster" disclosure beside the per-BOX Lost/Disposed buttons — so it read as
    "archive this box". It now confirms, names the medication, and counts the standing plans
    the click will stop.
  - Gate: `dotnet test` **164 passed, 0 failed, 0 skipped** against a real PostgreSQL (six new
    tests). Lint, mobile typecheck and web build clean. Verified on the rendered screens with
    Playwright: an as-needed plan saved as Gerektiğinde + tercihen Akşam + Tok karnına, Today
    kept the Gerektiğinde label, and the archive dialog showed the live plan count.

## Open, from the owner's second-round message and the mapping

These are **not** narrowed or deferred scope; they are the remaining asks plus what the
mapping turned up. Each needs its own turn, and the three marked (migration) must not share
one: two EF migrations authored in the same turn both regenerate the model snapshot from the
same baseline and conflict, and `tests/migrations/verify-upgrade.sql` and
`verify-production-shape.sql` each hard-assert the migration count, currently 12.

1. ~~**Pause and resume a plan**~~ — **DONE**, 2026-10-04, PR #39 (migration 13). See the
   evidence entry below.
2. **Caution notes** (migration, split in two). Medicines not to combine, foods to avoid,
   things to do and not do, shown somewhere prominent. They belong on the definition, must be
   plainly the household's own notes, and must never be styled as a computed warning: the
   footer promises the app does not evaluate interactions.
3. **Reinstate a package marked Kayıp** (migration). `MedicationPackage.Reinstate()` exists
   and nothing calls it. Retiring writes a negative `Loss` ledger entry, so restoring must
   write the mirroring positive entry rather than deleting history.
4. **Archiving still destroys a paused plan.** Once (1) lands, `CatalogEndpoints` will
   cascade-delete paused plans too, because it filters only on `DeletedAt`. Pause and archive
   interact badly and the fix belongs with (1).
5. **Archiving a person is a worse one-way door, and nothing covers it.** `Person.Restore()`
   exists with no route, and because `CurrentVersionsAsync` does not filter on the person's
   archive state, an archived person's active plans keep producing doses on Today
   indefinitely, with their name and no badge. The owner did not ask; it is the same question
   one table over and it is a live defect.
6. **`minimumIntervalMinutes` is enforced by nothing.** It is validated, stored, exported and
   editable, and no code prevents a repeat dose. The form's "En az ara (dakika)" promises a
   guard that does not exist — and it is the field an as-needed medicine most needs.
7. **Mobile parity.** The Expo client ships no meal-relation or day-period labels and its
   SQLite plans table has no `meal_relation` and no `instructions` column, so a household
   member on the phone sees none of this turn's guidance.

## Sprint plan, from 2026-10-04

The owner asked for sprints and set the target version at **1.0.0**. Five sprints are
recorded in Notion (Project Management → Sprints), each carrying its tasks, all tagged
`Version = 1.0.0`:

| Sprint | Scope |
| --- | --- |
| 2 · Plan lifecycle (**active**) | pause/resume · archive-vs-paused · the person-archive defect |
| 3 · Caution notes | store them · show them · make the minimum-gap field real |
| 4 · Inventory lifecycle and demo data | reinstate a lost package · synthetic demo seed (row 35) |
| 5 · Mobile parity | reports/export/counting on the phone · meal relation, day period, instructions |
| 6 · Security, accessibility and acceptance | rows 34, 37, 28, 29, 38, 40 |

Standing rules the owner set for this phase, which bind future turns:

- **A report is written at the end of every sprint**, whether or not the owner has tested
  yet. He plans to test per sprint but may batch several and come back later; defects he
  finds then go to the front of the next sprint.
- **Never two EF migrations in one turn.** Each regenerates
  `MedicationTrackerDbContextModelSnapshot.cs` from the same baseline and the second
  conflicts. `tests/migrations/verify-upgrade.sql` and `verify-production-shape.sql` each
  hard-assert the migration count, so every migration turn moves both.
- Finishing a sprint is **not** acceptance. AGENTS.md is explicit: a tag, a deploy, a green
  CI run or a sprint closure does not by itself mean V1 is complete. Only the owner closes
  row 40.

- 2026-10-04: **Plan pause and resume** (PR #39, migration 13). `IsPaused` lives on the plan
  VERSION, so pausing appends a version like any other edit and the periods either side of a
  break keep their own meaning. The load-bearing part is not the flag but the replay:
  `ScheduledSlots.Within` skips a paused version exactly as it skips an as-needed one, so a
  deliberate break cannot read as a run of missed doses. Punishing somebody for recording the
  truth is the surest way to teach them to stop recording it.
  - A flag was needed rather than an `effectiveTo` bound because three readers take the newest
    version with **no** effective-date filter: `WorkspaceEndpoints`, `RefillEndpoints.ProjectAsync`
    and the Today list. Today and the forecast now drop a paused plan; the workspace exposes
    the flag instead, because a paused plan must stay listed or there is nothing to resume.
  - In all three, the pause is applied **after** the governing version is chosen. Filtering
    earlier would let an older, unpaused version win the group and silently resurrect the
    schedule the household had just set aside.
  - Two traps closed: `PUT` did not carry the pause forward, so editing a paused plan would
    have quietly resumed it; and pausing twice now returns the existing version rather than
    appending a second identical one, so a client retry does not read as two decisions.
  - Resuming is offered on the medication's own page as well as the plan card — "ilacın
    sayfasından", as the owner put it, which is where you look when you pick a medicine back
    up. The forecast banner stays above it rather than being replaced.
  - Found in passing and fixed: the plan audit snapshot never recorded `Instructions`, so
    editing the instruction note audited as a no-op.
  - Gate: `dotnet test` **171 passed, 0 failed, 0 skipped** against a real PostgreSQL (seven
    new tests, four integration). Lint, mobile typecheck, web build clean. Verified on the
    rendered screens: running → paused → Today empty → resumed from the medication page →
    dose back at 08:00.
  - **Mobile is not covered.** The Expo SQLite snapshot has no `is_paused` column, so a paused
    plan still fires local reminders on the phone. Sprint 5, and the ALTER path must be tested
    against an existing v1 database file rather than only a fresh install.

- 2026-10-04: **Archiving is no longer a one-way door**, for a medication or a person
  (PR #41, squashed to `a34acf6`; no migration). This closes the rest of Sprint 2.
  - Archiving a medication soft-deleted every plan for it, and nothing can revive a deleted
    plan (`PUT /plans/{planId}` filters `DeletedAt == null`; there is no un-delete). So
    archiving destroyed the dose, times, weekdays and instruction note. Archiving a person
    was worse: no cascade, no audit row, no restore route — `Person.Restore()` had existed
    with nothing calling it — and because nothing filtered on the person's archive state,
    their plans kept producing doses on Today for ever, named and unmarked.
  - Both now pause the plans through one shared cascade (`PlanDeactivation.PauseAsync`). A
    medication and a person being put away are the same event from a plan's point of view,
    so they must not drift apart. A plan the household had already paused is left alone:
    appending another paused version would record a decision nobody made.
  - Restoring does not resume. Bringing a medicine back out of the cupboard, or a person
    back into the household, is not a statement that the course has started again.
  - The Today list now also filters archived people directly — belt and braces for new
    archives, and the half that works on rows archived before the cascade existed, with no
    backfill.
  - Interface: the archive confirmation no longer promises something irreversible, so it
    says what actually happens and is not styled as danger; and a plan whose medication or
    person is still archived offers no resume button, because Today filters both and the
    button would have looked like it worked while changing nothing.
  - **Wider than the task as written**, which asked only that a *paused* plan not be
    silently destroyed. Leaving active plans deleted would have meant two different fates
    for two kinds of plan — harder to explain than either uniform rule, and it would have
    left the data-loss footgun in place. Recorded here rather than done quietly.
  - Gate: `dotnet test` **177 passed, 0 failed, 0 skipped** against a real PostgreSQL (six
    new tests; the existing archive test was updated, since the behaviour it pinned was the
    defect). Lint, mobile typecheck, web build clean. Verified on the rendered screens.
  - Deployed: CI run 37214592904 green, deploy job 111472861171 success, `GET / -> 200` and
    `GET /api/auth/session -> 401` through nginx. The shared box was left alone:
    `Deploy complete. deploy-production.sh left 21 container(s) from other projects
    untouched.` Only `medication-tracker-api-1` and `-web-1` were recreated; the database
    container was never restarted (up three weeks, healthy).
  - Acceptance row 3's evidence line ("the active plan is deactivated and the cascade is
    audited") still holds: pausing deactivates and is audited as `CascadeDeactivated`.

- 2026-10-04: **The household's own safe-use notes**, stored and shown (migration 14).
  Sprint 3's first two tasks.
  - Five free-text notes on the medication DEFINITION, not on a plan version: what not to
    take it with, what food to avoid, what to do, what not to do, and anything else worth
    a warning. "Do not take this with grapefruit" is a fact about the medicine; on a
    version it would have to be retyped for every person taking it and re-entered on every
    dose change, and the copies would drift.
  - **The load-bearing part is what this refuses to do.** The software never derives a
    word of it: nothing is parsed, matched against a drug database, cross-referenced with
    another medicine, or checked before a dose is recorded. A tracker that started guessing
    which medicines clash would be making a clinical claim it cannot stand behind, and once
    it guessed right once it would be trusted to guess always — including by its silence.
    The "Hanenin kendi notu" badge is that boundary made visible, and `AsNeededGuidanceTests`
    has a sibling: `CautionNotesTests` asserts the notes cannot reach the recording path.
  - The word "etkileşim" appears in exactly one place in the product, the footer, where it
    is a denial: "Teşhis koymaz, doz önermez ve ilaç etkileşimi değerlendirmez." No field
    label uses it.
  - Migration 14 widens `medication_definition_change_events.previous_value` / `new_value`
    from `varchar(4000)` to `text` in the SAME migration that adds the columns. The audit
    snapshot is hand-serialised JSON of every field; five prose notes put it an order of
    magnitude past 4000 characters. Shipping the columns without the widening would break
    the audit write exactly when the trail finally had something worth recording.
    `verify-upgrade.sql` now asserts the column types and that the upgrade invented no
    notes on pre-existing rows, so a regenerated migration cannot quietly drop the widening.
  - Shown on the medicine's own card AND on the Today dose row — "uyarı notları kolay
    görünebilir yerde konumlansın", and Today is where somebody is reading with the box in
    their hand. One shared projection (`CautionView`) feeds the workspace, Today and the
    export, so the three cannot drift into different shapes; `null` when empty, so a client
    never renders a warning panel with nothing in it.
  - Export `SchemaVersion` 2 → 3: a reader must be able to tell a file with no warnings
    from one written before warnings could be recorded.

- 2026-10-04: **The minimum gap finally says something true.** Sprint 3's third task; no
  migration.
  - `minimumIntervalMinutes` was validated, stored, copied forward by the pause cascade,
    exported and editable — and **no code on the recording path ever read it**. Verified by
    tracing every reference, not from the earlier note. The form said "En az ara (dakika)"
    and promised a guard that did not exist, on the field an as-needed painkiller needs
    most. A promise nothing keeps is worse than a missing feature, because somebody relies
    on it.
  - It is now advisory and says so. Today carries `lastTakenAt`, `minimumIntervalMinutes`
    and `nextDoseAllowedFrom`; the row reads "Kendi notunuza göre henüz erken … Uygulama
    engellemiyor — gerçekte ne olduysa onu kaydedin", and both buttons stay live.
  - **Advisory on purpose.** The gap is the household's own note, so acting on it is not
    the software reaching a clinical conclusion — but refusing to record a dose somebody
    actually took would make the ledger lie about the one thing it exists to remember.
    Same rule as the pause slice: punishing somebody for recording the truth is the surest
    way to teach them to stop. `MinimumGapTests` pins the refusal to refuse.
  - The clock is keyed on person + medicine, not on the plan version, so editing a plan
    cannot erase the dose taken an hour ago. A skipped dose starts no clock.
  - The arithmetic is server-side (one place); whether the instant has passed is decided
    on the screen, because that answer changes every second. `useNow` is built on
    `useSyncExternalStore` with a quantised snapshot — reading the clock during render is
    both impure and wrong: nothing would re-render when the gap passed, so the warning
    would linger until something else refreshed the page.

- 2026-10-04: **Found in passing — the test harness read a migrations history table the
  application never writes.** `ApiTestHarness.NewDbContext()` built its own options with a
  bare `UseNpgsql`, so EF used the default `public."__EFMigrationsHistory"` while the
  application records `infrastructure.__ef_migrations_history`. The suite passed only
  because CI always starts from an empty database: the harness would create the whole
  schema and record it in a table production never reads. The moment anything migrated
  with the application's own configuration — `dotnet ef database update`, or the
  deployment's packaged SQL — the harness saw an empty history beside a full schema and
  tried to create the world a second time. The harness now mirrors the application's
  persistence configuration. This is the concrete diagnosis of the "eksik
  `__EFMigrationsHistory` tablosu" item that was sitting on the project's open list.

- Gate for all of the above: `dotnet test` **197 passed, 0 failed, 0 skipped** against a
  real PostgreSQL (20 new tests). Lint, mobile typecheck and web build clean. Verified on
  the rendered screens with Playwright: the Today row shows the warning block and the
  "henüz erken" notice with `Alındı` still enabled, the medicine card carries the same
  block, and the form section opens itself when notes already exist.

- 2026-10-04: **A box you wrote off can be found again**, and **a reproducible synthetic
  demo household exists**. Sprint 4, no migration.
  - `MedicationPackage.Reinstate()` had sat in the domain since the rebuild with nothing
    calling it — no endpoint, no button — so marking a box lost was a one-way door. The
    capability was modelled and then left unconnected, which is the worst of both: it
    reads as finished in the code and is missing in the product.
  - The state flag was the easy half. Retiring writes a NEGATIVE ledger entry for whatever
    was left in the box, so reinstating writes the symmetric positive one, or the household
    stays permanently short by an amount that was never actually missing. The entry names
    the retirement it undoes through `ReversesEntryId` — a field that already existed, was
    already exported, and nothing wrote. Without the link the history reads as a loss
    followed by an unexplained windfall, which is the shape of a mistake rather than of a
    correction. Nothing is deleted: the loss really was recorded, and this says it was
    undone.
  - `Found` is the existing ledger type for "stock the household found that the ledger did
    not know about", so no new enum member and **no migration**. Verified there is no CHECK
    constraint on `entry_type` before relying on that.
  - Which retirement gets undone is chosen by the package's own `RetiredAt` rather than by
    taking the newest loss, so a box lost, found and lost again pairs each find with its
    own loss; reversing the older entry twice would conjure stock out of nothing. A box
    retired while empty brings nothing back, which `ck_ledger_entries_non_zero` also
    demands.
  - Found by driving the screen rather than reading it: a household that loses its only box
    saw `Kutuları göster (0)` — the count hid the very box they needed, inside a panel they
    now had no reason to open. The label names recoverable boxes when there are any.
  - **Demo seed** (`tools/demo-seed.mjs`, `pnpm seed:demo`) closes acceptance row 35, the
    last PARTIAL technical row. It drives the **public API**, not SQL: a SQL fixture can
    write states the domain cannot reach — a package whose balance disagrees with its
    ledger — and a fixture that drifts from the product is worse than none, because it
    looks like evidence. The first run proved the point by being rejected for an invalid
    `form`.
  - It is additive and isolated: it registers its own account at a fresh `.invalid` address
    (RFC 2606, never a real mailbox) and writes only inside that household. No path in it
    updates or deletes a row it did not create. Loopback is the only host it will talk to
    without `--allow-remote` and a typed confirmation, because a demo seed pointed at the
    live site would leave synthetic households in production for ever — the cleanup chore
    this project already carries from smoke tests.
  - Gate: `dotnet test` **204 passed, 0 failed, 0 skipped** (seven new tests). Lint, mobile
    typecheck and web build clean. Driven in a browser: 14 tablets → lost → 0 → found →
    14, conservation exact, and the seeded demo household renders with fractional half-dose
    plans, caution notes, the gap warning and a paused course.

- 2026-10-04: **The phone stops reminding for a plan the household set aside**, and
  **shows the guidance it was already holding**. Sprint 5's first two slices; mobile
  schema version 2.
  - This was the only item on the mobile list that told somebody something untrue. The
    household pauses a course on the web, the server stops producing doses, and the phone
    carries on firing local notifications — because the Expo SQLite snapshot had no
    `is_paused` column to carry the decision across.
  - **The migration structure is the load-bearing part.** A fresh install is CREATED at
    the current shape, so it must not then replay the step migrations: `CREATE_SCHEMA`
    already contains what they add, and an ALTER onto an existing column throws. That is
    the half that only breaks on a clean device — which is the half a developer always
    tests.
  - The scheduler and the schedule signature now share one predicate (`schedulable`).
    They must not drift: the signature decides whether to reconcile at all, so if it
    still counted a plan the loop had stopped scheduling, pausing would leave the
    signature unchanged, the reconcile would early-return, and the phone would keep
    reminding for a plan nobody is taking.
  - The reminder query also filters archived people, matching the server fix from the
    archive slice. The phone filtered the medication but not the person.
  - **Mobile had no test runner, so one was built for the thing that matters.**
    `tools/check-mobile-migration.mjs` compiles the real `migrateDatabase` and drives it
    against real SQLite (`node:sqlite`) down both paths: a fresh install, and a version 1
    database holding a plan row that must survive with its values intact and default to
    not paused — a phone that upgrades offline must not go silent by treating every plan
    as set aside. Proven to work by reintroducing the fresh-install bug and watching it
    fail with `duplicate column name: is_paused`, which is exactly what a user's phone
    would have hit. Wired into CI beside the mobile typecheck.
  - A server migration gets CI on a production-shaped baseline. A phone migration has no
    such net: it runs once, on somebody's device, over records the server has not seen.
  - Second slice, no schema change: the Today row now renders `dayPeriod` and
    `mealRelation`. The server has always sent them and the phone has always cached them
    in `due_doses`; only the rendering was missing. It also fixes the same defect the web
    had before the plan-timing slice — a scheduled plan with a named period and no clock
    showed an em dash while the phone knew the period.
  - Gate: `dotnet test` **204 passed, 0 failed, 0 skipped**; lint, mobile typecheck, web
    build and the new migration check all clean.
  - **Not visually verified.** `react-native-web` is not installed, so the mobile screen
    cannot be rendered in CI or in an agent container. Typecheck, the typed dictionary
    and the cached query are the evidence; a real Android device is still the acceptance
    gate (rows 28 and 29, owner-held).

- **Sprint 5, third slice — owner-approved scope change, 2026-10-04.** The owner moved
  mobile feature code to a version after V1, kept the mobile infrastructure, and asked
  for server work to continue planned so mobile stays compatible. Recorded as **ADR
  0015**, with acceptance rows 28 and 29 marked `DEFERRED` (not `DONE`) and the mobile
  halves of rows 34 and 37 recorded as deferred too.
  - The decisive fact was not a coding problem. There is no release channel, so every
    mobile slice lands on `main` and reaches nobody, while the V1 rows that do reach
    users were waiting behind it.
  - **The compatibility claim is now a test rather than an intention.**
    `MobileContractTests` pins the field set `/workspace` and `/today` must carry — the
    rows the Expo client already caches, plus `cautions`, `minimumIntervalMinutes`,
    `lastTakenAt` and `nextDoseAllowedFrom`, which the deferred slices will read and the
    server already sends. It asserts presence and type, **not** exhaustiveness, so an
    additive change stays free and only removal or renaming fails.
  - Why this specifically matters while mobile is deferred: during active development a
    dropped field is a failing build within the hour. Deferred, the same drop is silent
    for months, and the cost lands on whoever resumes the work — as unbudgeted server
    work, discovered after the mobile estimate was given.
  - Proven to catch it: `mealRelation` was removed from the Today projection and the test
    failed with *"The mobile client reads mealRelation, and the server no longer sends
    it"*. Restored, green again. A contract test that has never been seen to fail is a
    comment.
  - The app itself was not touched. It stays at on-device schema version 2, and
    `pnpm typecheck:mobile` plus `pnpm check:mobile-migration` keep running on every
    commit — deferred is not abandoned, and the next mobile version will inherit whatever
    version-2 databases exist by then.
  - Gate: `dotnet test` **208 passed, 0 failed, 0 skipped**; lint, mobile typecheck,
    migration check and web build all clean.
  - **Sprint 5 is closed** under the approved scope. The remaining mobile parity work
    (caution notes and the gap notice on the phone, and reports/export/counting, which is
    a surface the phone never had) goes with the client to a later version.

## Sprint 6 — security and accessibility

- **Row 37, server half — rate limiting was not merely incomplete.** The threat model
  recorded "rate limiting covers only the auth endpoints, whether dose recording and sync
  need their own limits is undecided". Mapping the pipeline settled it: `Authenticate` runs
  on every `/api` path and, whenever a 64-character token arrives in a cookie or a bearer
  header, looks it up in `identity.sessions` **before** any authorisation decision. An
  unauthenticated caller could drive one database round trip per request, unbounded,
  against a PostgreSQL this project shares a host with.
  - The limiter is now global rather than per-endpoint, because the endpoint somebody
    forgets to annotate is the one nobody reviewed. It partitions by remote address
    deliberately: a session-derived key is more precise for a household behind NAT but can
    be rotated every request, which defeats the limit entirely.
  - The figure is generous on purpose, and `RateLimitTests` says why: a phone offline for
    a fortnight drains its outbox one request at a time, and a sync that failed because of
    our own limiter would look to the household like lost doses. Health checks are exempt
    so a container probe cannot be throttled into reporting a healthy deployment unhealthy.
  - 429 now carries `Retry-After`. Both limits are configuration, read **per partition**
    rather than at startup — `builder.Configuration` is already built at that line, so an
    eager read silently ignores a layer added later and would look correct while using the
    default. That cost a debugging round.
- **Row 37, log redaction — the first version of the test proved nothing.** It passed with
  `EnableSensitiveDataLogging` switched on, because `SetMinimumLevel` does not override the
  category rules in `appsettings.json`, so the EF command category never reached the
  capture. The harness now layers `Trace` over those rules; with sensitive logging on the
  test fails naming the e-mail address and the person's name, and off it passes. A
  redaction test that has never been seen to fail is a comment.
- **Row 37 is not closed, and the reason is a third finding.** `compose.production.yml`
  sets `POSTGRES_USER: medication_tracker`, which the postgres image creates as the
  cluster's bootstrap **superuser**, and the connection string uses that account. A leaked
  `DATABASE_PASSWORD` or a successful injection is therefore not a "read the household
  tables" problem but a "the database container is yours" one.
  `deploy/least-privilege-database-role.sql` creates a `NOSUPERUSER` replacement that
  still owns the eight application schemas so migrations keep working, verified against a
  throwaway database — the role has none of SUPERUSER, CREATEDB, CREATEROLE or BYPASSRLS,
  it can `CREATE TABLE` and `INSERT` in its own schemas, and `COPY ... TO PROGRAM` is
  refused. **Owner-gated:** applying it changes the credential the live API authenticates
  with and needs a password only the owner holds.
- **Row 34 — the audit found three real defects rather than confirming the markup.**
  `pnpm check:a11y` drives the real screens in a real browser with axe-core against WCAG
  2.0/2.1/2.2 A and AA, in both locales, at 375px and 1280px, then re-measures every screen
  at twice the root font size.
  - **Every page shipped with no `<title>`.** `layout.tsx` read it from the `'use client'`
    i18n module, which Next cannot evaluate while rendering metadata — and rather than
    failing the build it emitted nothing. The code read correct. Fixed by moving the two
    document strings into `document-metadata.ts`, which `i18n.ts` also imports, so the
    title cannot drift from the heading.
  - **Contrast below AA.** Computed across the whole palette rather than only where axe
    happened to hit: `--ink-faint` was 2.4:1 on every background and carried the
    product-boundary disclaimer, `--ink-muted` 4.1:1 on the sunken surface and soft tints,
    `--warning` 3.8:1. Lifting faint alone would have landed it within a hair of muted and
    flattened the hierarchy, so both moved. `--accent` was left alone: nothing uses it as
    text, and darkening the one brand colour to fix a problem `--accent-ink` already solves
    would have been the wrong trade.
  - **Every screen scrolled sideways at 200% text.** Eleven rem-based `min-w-*` utilities
    exceeded a 375px viewport once the root font doubled — the layout broke for exactly the
    reader who had enlarged the text to read it. Each is now capped at the container.
  - Result: **37 checks, 0 violations**, and the layout holds at twice the root font size.
  - Deliberately **not** in CI: it needs the whole stack on one origin, and axe-core plus a
    browser driver are not repository dependencies, so every CI install would pay for a
    driver it never uses. It is a command, and the audit it produced is recorded here.
- Gate: `dotnet test` **215 passed, 0 failed, 0 skipped**; lint, mobile typecheck, mobile
  migration check and web build clean.

## After Sprint 6 — the web client has a test runner

Not a V1 row, and not presented as one: `apps/web` had no test runner at all, so its
logic was guarded only by `tsc`, lint and the API tests. The owner asked for one.

- **Vitest with React Testing Library**, which is what the installed Next version's own
  guidance recommends (`node_modules/next/dist/docs/01-app/02-guides/testing/vitest.md`).
  One deviation on the installed Vite's own advice: `vite-tsconfig-paths` is replaced by
  the native `resolve.tsconfigPaths`, one dependency fewer for the same `@/lib/...`
  resolution. `pnpm test:web`, wired into CI after `pnpm lint` — unlike the accessibility
  audit it needs no running stack, so it belongs there.
- **49 tests across five files, chosen by where being wrong hurts rather than by
  coverage.** Exact-quantity arithmetic and the fraction rendering a dose is read from;
  the locale store's two promises its comment makes (a blocked `localStorage` falls back,
  another tab's change arrives); the dictionaries' integrity beyond what the type can see
  (no blank values, every API enum mapped to a key that resolves in both languages); the
  `useNow` snapshot stability that caused an infinite re-render once before; and the
  caution panel's two rules (nothing at all when empty, the household's line breaks kept).
- **The first thing the runner found was a latent defect in `formatQuantity`.** An
  unreduced whole amount rendered as a different number: `4/2` came out as **"20/2"**,
  `6/3` as "20/3" — the whole part and a zero remainder printed side by side. Latent, not
  live: `ExactQuantity` reduces on construction, so the API cannot send `4/2` today. That
  is why it was never seen, not why it was safe; the formatter now reduces on its own.
- Two things the test suite itself needed, written down so the next person does not
  rediscover them: React Testing Library only registers its own cleanup when the runner
  exposes a global `afterEach`, which Vitest does not, so without `src/test/setup.ts`
  every render accumulates and the third test finds three panels; and jsdom hands out
  `localStorage` through a Proxy, so a spy on the instance reports zero calls while the
  code under test runs the real method — spy on `Storage.prototype`.
- `useNow` moved from `Today.tsx` to `lib/use-now.ts`: it is a clock, not a dose concern,
  and its one load-bearing property is only ever noticed when it breaks.
- Gate: `pnpm test:web` 49 passed; lint, mobile typecheck and web build clean. The API
  was not touched.

## Owner live-test feedback — 2026-10-05

The owner began the acceptance run on the live site and sent a first batch of notes
mid-test, asking for notes and an evaluation and explicitly **not** for code: *"Bunları not
al, hemen kod yazma. Planlayacağız ve planlı bir şekilde ilerleyeceğiz."* The notes, what
the code does today for each, the evaluation and a candidate slicing are in
`docs/owner-feedback-2026-10-05.md`; the later-version ideas are in `docs/roadmap.md`
under *Later*.

What the evaluation established, in short:

- **One live defect, reproduced.** Making another box active fails with *"Bir şeyler ters
  gitti"*. The API does release the previous pin in the same request; the two UPDATEs run
  in primary-key order inside one `SaveChanges`, and the filtered unique index
  `ix_packages_single_pinned` rejects the pin when it sorts first. A throwaway test on
  2026-10-05 swapped the pin in alternating orders across six fresh medicines: one of six
  returned 500 with `23505 duplicate key ... "ix_packages_single_pinned"`. Deterministic
  per pair of boxes, which is why the owner hits it every time. Not fixed yet, by the
  owner's instruction; it is the first slice when the go comes.
- **Two notes are mostly built already.** Boxes carry location, note, lot, expiry and a
  `PUT` route, but the web cannot edit a box after creation and there is no name field.
  The depletion forecast already walks real due days; the owner's "compute the official
  refill date" button is that forecast with as-needed plans counted as daily, plus a
  client action.
- **One note touches the product boundary** and needs the owner's decision and an ADR:
  caution tags matched against the person's other medicines. Admissible only as a
  household-authored, exact-match, person-scoped, never-blocking reminder of the
  household's own note, with wording that never claims an interaction.
- **One note has a trap.** Restoring an ended plan by clearing `DeletedAt` would make the
  adherence replay count the ended period as missed doses. Ending must append a version
  with `EffectiveTo`; restart appends a new version.
- The remaining notes (demote Lost/Discarded, official/unofficial flag, monthly plans) are
  ordinary changes, two of them with a migration each.

No V1 row changed. The acceptance table is unchanged at 33 DONE, 4 PARTIAL, 2 DEFERRED,
1 OPEN.

## Sprint 7 — the owner's live-test notes, closed 2026-10-05

The owner answered decisions D1–D6 on 2026-10-05 (recorded in
`docs/owner-feedback-2026-10-05.md`), finished their own test round ("şimdilik güzel
gözüküyor"), asked for a general review by the agent, and set the sequence: these slices,
then the mobile client resumes, then later features. Versions where web and mobile are at
parity share one name; the web may run ahead during development and that must stay
visible.

### Slices 1 and 2 — PR #50 → `main` `9877f8d`, deployed (run `37307889883`)

- **Active-box swap 500 fixed.** `POST /packages/{id}/pin` now saves the release of the
  previous pin before the acquire, inside one transaction. The new test swaps the pin in
  both directions and was red before the fix (one direction hit `23505` on
  `ix_packages_single_pinned`) and green after.
- **Box row demoted.** Lost, disposed and lending wait under a per-box "Diğer işlemler"
  disclosure; the active-box choice and loan return stay on the row.
- Deploy job succeeded: `GET / → 200`, `GET /api/auth/session → 401`.

### Slice 3 — PR #51 → `main` `d3c51ee` (migration 15)

- `MedicationPackage.Label`, optional, shown instead of "Kutu N" with the ordinal beside
  it. Web "Kutuyu düzenle" dialog over the existing PUT, which until now nothing in the
  web could reach; barcode and source are sent back unchanged because PUT replaces every
  detail. A label past 60 characters is refused by name. Migration-count assertions in
  `tests/migrations` now say 15.

### Slice 4 — end and restart a plan — PR #52 → `main` `7897dad`

- **The governing rule changed, deliberately.** A day used to be governed by the
  highest-numbered version *covering* it. Under that rule an older open-ended version kept
  governing every day after a newer version's end, so an end date set on an edit never
  ended anything, and ending by appending a bounded version would have handed the schedule
  back to the version it replaced. The rule is now **the latest version that had started
  by the day, provided it has not ended** — in `ScheduledSlots.Governing`, in the Today
  list's `CurrentVersionsAsync`, and in the dose-recording "superseded" check. Unit tests
  in `PlanEndTests` pin both halves, including "an end date on an edit really ends".
- **Pause starts on the day of the pause.** The paused version used to copy the current
  version's start as well, so it governed the days before the pause too and the replay
  read them as having asked for nothing: a pause silently erased recorded history. Now
  `VersionStartFor` gives the appended version today in the plan's zone (or the current
  start when that is still ahead). The archive cascade in `PlanDeactivation` uses the same
  helper. Pinned by an API test.
- **End** (`POST /plans/{id}/end`, `endsOn` defaulting to today in the plan's zone) appends
  a copy of the current version closed on the last day of doses, pause included.
  Idempotent on the same day. **Restart** (`POST /plans/{id}/restart`, `startsOn`) appends
  an open, unpaused copy starting on that day; refused with `plan_not_ended` while the
  plan runs and `before_end` on or before the last day. The gap is governed by the ended
  version and asks for nothing. Delete stays for a plan created by mistake.
- **Web.** Past plans section; "Planı sonlandır" with a date and an explanation; "Yeniden
  başlat" with the earliest allowed day; "Planı sil" behind a disclosure and a
  confirmation. The plan form's start date now defaults to **today** on edit (it defaulted
  to the plan's original start, which re-governed every day in between), and "today" is
  the viewer's calendar date rather than the UTC date (`lib/dates.ts`, tested).

### Slice 5 — Coverage and the two compute buttons — PR #53 (migration 16)

- **Coverage** (TR *Karşılama*): `InsuranceCovered`, `SelfPaid`, `Unspecified` (behaves as
  covered). On the definition as the default; a box may override it (`packages.coverage`,
  nullable), set when stock is added or in the Edit box dialog. The owner's naming
  request is honoured: nothing reads as "informal" or "illegal".
- **Two dates in the Temin dialog, both optional, both with "Stoktan hesapla".** The
  official refill date is suggested from the **insurance-covered stock only**; the new
  **expected end date** (`refill_policies.expected_depletion_on`) from all stock. Both use
  `RefillForecast.SupplyRunsOutOn`, which counts an as-needed plan as one dose a day — the
  owner's "resmî olarak her gün kullanacağı varsayılır" — while the forecast proper keeps
  excluding as-needed plans. Suggestions arrive on `GET …/forecast`
  (`canSuggest`, `suggestedNextEligibleRefillOn`, `suggestedDepletionOn`,
  `coveredBalance`); nothing is stored until the household saves. Without a plan the
  buttons are disabled and the dialog says why.
- Tests: `SupplySuggestionTests` (the owner's 20-tablets-at-2-a-day example to the day,
  as-needed counted daily, empty supply runs out today) and `CoverageApiTests` (inherit and
  override, covered-only versus all-stock suggestions, policy round trip, unknown coverage
  refused). Migration-count assertions now say 16.

### Slice 6 — monthly recurrence in both forms — PR #54 (migration 17)

- `RecurrencePattern.DayOfMonth` (1–31) and `RecurrencePattern.EveryNMonths` (1–120,
  anchored on the start date's day). **Calendar months, never thirty-day spans**; a day
  the month does not have falls on its last day, so a plan written for the 31st is not
  silently skipped in February. The rule lives in `RecurrenceRule.IsDue`; the forecast,
  the adherence replay and the Today list walk days through it and needed no change.
- `plan_versions.day_of_month`, `plan_versions.interval_months`, and the
  `ck_plan_versions_recurrence` check constraint rewritten so each pattern still owns
  exactly its own fields. Validation mirrors it in `RecurrenceRule.IsValid`.
- Web: two new schedule choices with hints that say what happens in short months;
  `describeSchedule` names them on the card.
- Tests: `MonthlyRecurrenceTests` (clamping, anchoring, quarterly and yearly, due-day
  enumeration, field ownership, and an API round trip that is due only on its day).
  Migration-count assertions now say 17.

### Slice 7 — do-not-take-with tags and the Today warning — PR #55 (migration 18, ADR 0016)

- The one place the product cross-references two medicines, and the whole of what it
  does is **string equality on words the household typed**: a tag on one medicine against
  the name, brand and active ingredients of another medicine the **same person** has on
  the **same day**. Normalised (case, diacritics, spacing, dotted and dotless i), never
  guessed: `cvitamine` does not match `C Vitamini`. No database, no synonyms.
- Both rows warn, with the owner's wording: on Allerset "Parol ile birlikte almayınız ·
  Nedeni: Parol, paracetamol"; on Parol the same with "Allerset" as the reason. **Red,
  never a block**, always attributed to the household's own tag, and every warning and
  the tag field's hint say that silence is not a safety claim. The footer disclaimer now
  says the app only reminds the household of its own tags.
- The prose caution notes stay unevaluated; the tag list lives beside them. The boundary
  paragraphs in `docs/domain-model.md`, `docs/v1-acceptance.md` and `PROJECT.md` gained
  one sentence each; `CautionNotes` and `CautionNotesTests` keep their structural refusal.
- `DoNotTakeWithMatcher` runs inside the Today projection — a handful of comparisons per
  request — so the "analyse" button the owner offered as a fallback is not needed.
- Tests: `ConflictWarningTests` (normalisation, the owner's example to the word, no
  guessing, no self-match, brand as a name, mutual tags give one warning per row; and the
  API: same person and day only, never blocks, round trip, not due today → no warning).
  Migration-count assertions now say 18.

### Slice 8 — the general review the owner asked for — PR #56 → `main` `c4c9240`, deployed (run `37316264856`)

Run against a local stack at migration 18 with a synthetic household seeded through the
API (two people, four medicines, mixed coverage, a labelled box, a tagged medicine, a
monthly plan, an ended plan) and driven through every screen the sprint touched at 1280
and 375 pixels, in Turkish and English.

- **Seen working on the screen, not inferred from code:** the red do-not-take-with
  warning on both rows of the same person and on neither row of the other person; the
  Temin dialog's two dates and the buttons filling them (twenty covered tablets at one a
  day → 25 October; twenty-eight on hand → 2 November, matching the forecast line); box
  name and location on the row, the "Kendi ödemesi" badge, the demoted actions behind
  "Diğer işlemler", the Edit box dialog; the plans screen with "Planı sonlandır", the
  past-plans section with "Yeniden başlat"; the monthly plan described on its card. No
  console or page errors beyond the expected pre-login 401; the local API log holds no
  exception.
- **Accessibility audit re-run** (`pnpm check:a11y`): no WCAG A/AA violations across
  all screens in both languages, and the layout holds at twice the root font size.
- **Fixed:** the generic "Bir şeyler ters gitti" banner. A validation refusal now names
  the field it rejected ("Geçersiz alan: effectiveFrom") and ten more refusal codes have
  their own sentence; `describeError` replaces the uniform expression in every component
  and is unit-tested. The Edit box dialog's date row no longer wraps its labels.
- **Found, recorded, not fixed (candidates, in Notion as Backlog):**
  1. Giving tablets back from one box to another — the owner's "two of Gülten's Arlec go
     back to İsmail" — is a transfer between boxes, which does not exist. Lending a box
     and returning it does (ADR 0011) and covers the "use İsmail's box for two days" half.
  2. Submitting the same count twice at the same instant would hit the unique index on
     `previous_batch_id` after the `stale_revision` check and surface as 500 rather than
     409. Only under a genuine race; worth a translation of that violation to 409.
  3. The "Diğer işlemler" disclosure on every plan card is visually heavy; a per-card
     menu would be lighter. Cosmetic.

### Slice 9 — mobile preparation, no mobile code — PR #56, same merge

ADR 0017 sets the versioning: one version line for the API and the web (`vX.Y.Z`), a
mobile number that names the server version it is at parity with (`mobile-vX.Y.Z`),
the lag visible rather than hidden, and the two honest ways to resolve the retired
`v1.0.0` tag for the owner to pick at acceptance. `docs/mobile-resume-plan.md` lists what
exists, what the server gained in Sprint 7 that the phone does not know (the
governing-rule change first, then the monthly patterns, `conflicts`, ended and restarted
plans), and the order to close the gap — starting with the release channel, which only
the owner can create.

## Sprint 8 — mobile resumes, from 2026-10-05

The owner's answers to the Sprint 7 report, in one message: "Mobile başla, kaldığın
yerden devam et"; the phone is plugged into their laptop and USB debugging is preferred
over EAS Build; an older build named *MedicineTracker* from a previous tool may be on the
phone and may be removed; **"kabul"** for the V1 acceptance items; the old tags stay
`v1.0.0` and what comes next is `v1.0.1`; and the working rule, in their words: they
decide, test, authorise — the agent does the work, including row 37.

### Slice 1 — schema v3 and the server's due-day rule on the phone — PR #58

`src/notifications/schedule.ts` is a pure module (no Expo, no React Native) mirroring
`RecurrenceRule` and the governing rule, tested under Vitest with cases taken from
`PlanEndTests` and `MonthlyRecurrenceTests` (20 tests; `pnpm test:mobile` is a CI step).
Reminders use a repeating OS trigger only for a started, open-ended daily or weekly
version; an end date, a future start, every-N-days and the monthly patterns are bounded
runs of exact instants topped up per plan. **Defect this closes:** a plan ended on the
web kept reminding on the phone, because a daily plan was a repeating trigger whatever
its end date. Schema v3 adds `day_of_month`, `interval_months` and `conflicts`; the
migration check upgrades frozen v1 and v2 fixtures. Today renders the conflicts in red
with attribution. `MobileContractTests` pins the three fields (5 tests). Full API suite
green locally (254).

Known gap, recorded in the plan: the workspace sends the latest version only, so a
future-dated edit leaves the in-between days without local reminders.

### Slice 2 — least privilege in production — PR #59

The mechanism for row 37, in the purge's shape. `compose.production.yml` reads
`APP_DATABASE_USER` / `APP_DATABASE_PASSWORD` for the API's credential and falls back to
the bootstrap account until they exist (CI pins both renderings). The *Apply least
privilege* workflow — confirmed by a dispatch input or by `deploy/least-privilege.request`
on `main`, serialised with the deploy under the `production-deploy` concurrency group —
ships `deploy/apply-least-privilege.sh`, the SQL and the compose file from the reviewed
commit and runs the script on the host: password generated there, role created or
refreshed as the superuser, proven confined over a password-checked TCP connection,
the two variables written to `.env.production` (previous file kept under
`.deploy/env-history`), only the api container recreated, readiness waited for with a
rollback to the previous file on failure, then `pg_stat_activity` read to prove the API's
connections are the confined role and none remain as the superuser. The SQL gained
`ALTER DEFAULT PRIVILEGES` per schema because the deployment keeps applying migrations as
the superuser inside the container. Rehearsed on a local production-shaped database:
every proof above, plus idempotency, rotation and a migration re-run after the ownership
change.

**Applied in production on 2026-10-05.** PR #59 merged as `8775cf1`; the *Apply least
privilege* workflow ran first on that push (run `37337177964`) and printed: the role
reads `households.households`; `rolsuper = f`, `COPY ... TO PROGRAM` refused, `pg_shadow`
refused; only `medication-tracker-api-1` recreated; `pg_stat_activity` shows
`medication_tracker_app 1` and no superuser connection; previous environment file kept
under `.deploy/env-history`; 23 containers of other projects untouched; `GET / -> 200`,
`GET /api/auth/session -> 401`. The deploy of the same push (run `37337177804`) then ran
behind it and the site stayed up on the confined credential. The request file was
removed in the follow-up PR, which ran the workflow once more to find nothing to do.

### Slice 3 — the `v1.0.1` cut — PR #61

ADR 0017 made real: the root `VERSION` file is the one source; the API project reads it
into the assembly version and `GET /api/version` reports it with the commit the image was
built from (`APP_GIT_SHA`, a build argument CI passes; null locally, never invented);
`next.config.ts` bakes it into the static export and the footer shows `v1.0.1 · <commit>`;
the phone's `app.json` says `1.0.1` and the header shows it. Both Dockerfiles copy
`VERSION` into the build. `VersionEndpointTests` asserts the endpoint reports the file's
content and needs no session; a web unit test pins the label. Release notes:
`docs/releases/v1.0.1.md`. The tag goes on the merge commit once its CI is green, and
row 38 closes with that run.

### What the cloud session could not do

Install on the phone. The owner asked "yapamaz mısın oradan?" — no: this session runs in
a cloud container and cannot reach a USB port on their laptop. `docs/mobile-device-install.md`
is the runbook for a session on their computer (Claude Desktop app, or
`claude remote-control` in the repository folder), including removing the old app by its
package name and nothing else.

## Where this stands

Sprints 2 through 7 are closed, each with its report on its Notion sprint page; Sprint 8
(mobile) is open.

The acceptance table stands at **36 DONE, 2 PARTIAL, 2 DEFERRED, 0 OPEN** of 40 rows.
`DEFERRED` is an owner decision recorded on a date, not a criterion met: **V1 is a web
release with mobile infrastructure in place and does not deliver the offline mobile
client** (ADR 0015). The owner accepted rows 30, 39 and 40 on 2026-10-05.

**What remains is the agent's:** the `v1.0.1` cut; row 38 closes itself on that commit.
Row 37 landed in production on 2026-10-05.

**Exact next action.** Cut `v1.0.1`, as the *Next exact action* section at the top
describes. Mobile continues on the owner's computer in parallel.

Still open for the owner, unchanged: which production household is theirs, so the
synthetic ones left by smoke tests can be cleaned; and the `VPS_SSH_KEY` rotation.
