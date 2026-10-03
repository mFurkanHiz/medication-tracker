# Web VPS deployment

Status: V1 web/API release deployed and accepted on 2026-09-14. Reports, export and
counting deployed 2026-10-03 from `8ebe4c60a9660fb4cdb57fb31f6b51aa351c2d9f`, CI run
`37125695168` — the first release GitHub Actions shipped without a hand on it. The
script reported `left 23 container(s) from other projects untouched`, and the public
checks returned 200 and 401. See `docs/v1-progress.md` for the full evidence.

Earlier application image source: `11b9fd5c32511af8fea9df68189768e5e6dc86d0`.
CI `34757450538` passed all 15 API tests against PostgreSQL, web lint/build,
mobile TypeScript and both production Docker builds. The downloaded image archive
was verified as SHA-256
`c8c7fe8e44a844e49e3297ef3c6962e794bbfe42d56b70d0959ee1c7372d0d52`
before deployment. `deploy/smoke-test.ps1` passed against the public HTTPS URL,
covering independent medication entry, 20+8 package stock, package assignment,
as-needed use, exact package consumption, named-period scheduling and logout.

Use `compose.production.yml` and `deploy/deploy-production.sh` for the authenticated
release. Place the successful CI image artifact in `.deploy/medication-tracker-web.tar.gz`.
The script uses private runtime credentials, starts the project database, backs it
up, applies the image's explicit migrations and checks API readiness. Never print
`.env.production` or commit it. The pre-migration backup is not an offsite backup
or a substitute for the outstanding encrypted backup/restore production review.
The 2026-09-14 deployment created
`.deploy/backups/pre-migration-20260914T095719Z.dump` before applying the V1
migration. The previous release archive remains recoverable at
`.deploy/medication-tracker-web.pre-v1.tar.gz`.

- Public URL: `https://medicationtracker.rapidconfigs.com`
- VPS: `srv925801` (`31.97.53.159`)
- Application directory: `/opt/medication-tracker`
- Containers: `medication-tracker-web-1`, `medication-tracker-api-1`, and
  `medication-tracker-database-1`
- Host binding: `127.0.0.1:3022`
- Initial deployed source: `053fefd8e99bde2a5f9bdb6bba7ac87d01775818`
- Origin TLS: Let's Encrypt with automatic Certbot renewal

The web container binds only to loopback port `3022`. The API and PostgreSQL are
private services in the same project network and do not publish host ports.
The original `compose.web.yml` commands below describe only the earlier static
preview; use the production compose and deployment script for current releases.

## DNS

Use one proxied Cloudflare `A` record:

- Name: `medicationtracker`
- IPv4 address: `31.97.53.159`
- Proxy status: Proxied
- TTL: Auto

Do not create a CNAME for the same hostname. Remove the two Sites verification
TXT records if they were added; they are not needed for VPS hosting.

## Deploy

A merge to `main` deploys by itself once the owner has armed it, unless every file it
changed is documentation (`**/*.md` or `docs/**`): a commit that changes nothing the
server runs should not restart it. Pull requests have no such filter, so documentation
is still built and tested before it lands; only the shipping is declined, and
*Run workflow* still deploys such a commit on demand.

CI's `deploy` job
runs after both test jobs pass, downloads the image artifact it just built, verifies
its SHA-256 on both sides of the transfer, ships the deploy script and
`compose.production.yml` from the same commit, and runs the script over SSH. It is
skipped entirely unless `DEPLOY_ENABLED` is `true`, so the automation stays inert
until it is configured. `docs/secrets.md` says what each setting is and
`docs/preflight.md` section 6a is the one-off setup, both in Turkish.

Only `VPS_SSH_KEY` and `VPS_KNOWN_HOSTS` have to be secrets. `DEPLOY_ENABLED`,
`VPS_HOST`, `VPS_USER` and `PUBLIC_URL` are read from either context, variable
first, because an address and an on/off switch are not credentials and storing
them as secrets only masks them in the logs. The arming switch is read in a
separate `gate` job rather than the deploy job's `if:`, because a job-level
condition cannot see the `secrets` context at all: a switch kept as a secret
would read as empty there and deployment would silently never run. A step can see
both, so `gate` reads either and publishes a plain `yes`/`no`.

The host key is handled by `deploy/authorise-ssh.sh`, which both workflows call.
It takes the key from the `VPS_KNOWN_HOSTS` secret, else from `deploy/known_hosts`
in the same commit, which is why that secret is optional: a host key is public by
design. Either source is normalised first, so a full `known_hosts` line, the key
on its own, and a line whose host field was masked to `***` all produce the same
file — whoever read the key off a log had no host field to copy when the address
is a secret.

With no key from either source it does **not** fall back to trusting the network.
`StrictHostKeyChecking=yes` only means something while the key is pinned, so the
script reads what the server currently offers, prints it with its fingerprint for
review, and fails before anything is transferred. Pinning that key is a decision
someone makes, not one a deploy makes for itself.

The same job can be run on demand — Actions → CI → *Run workflow* on `main` — so
deploying the commit that is already on `main` does not need an empty commit to push.
A manual run rebuilds and re-verifies from scratch, so it ships exactly what a merge
would have shipped. Its one input, `run_smoke_test`, additionally runs
`deploy/smoke-test.ps1` against the live site; it is off by default because that
script registers a synthetic household on every run.

### Proving the deploy worked

The script checks the site on the host's own loopback port. The job then checks it
from outside, writing nothing: `GET /` must return 200, and `GET /api/auth/session`
must return **401**. The second is the useful one — 401 proves the request reached the
API through nginx, where 200 or 502 would mean it did not.

The job declares `environment: production`, so adding a required reviewer there makes
every deploy wait for an explicit approval — which is how the acceptance contract's
preflight gate survives automation.

### What protects the rest of the host

This VPS runs other projects: more than twenty containers and several sites behind
one nginx. Every Compose command in `deploy/deploy-production.sh` is scoped with
`--project-name medication-tracker`, the only stop is `stop api web`, and the script
prunes nothing and never touches nginx. Since 2026-10-02 it also records the
containers that are **not** ours before it starts and compares them at the end: if
one of them is no longer running the deploy fails and names it. Another project
starting a new container is not treated as damage.

## Purging the superseded care data

`deploy/purge-care-data.sh --yes-erase-care-data` erases every care record the
household entered — people, the medication catalog, packages, the whole inventory
ledger, plans and their versions, recorded doses with their allocations and
corrections, counts, refill settings and sync receipts — and keeps what lets somebody
sign in: accounts, sessions, households, memberships and subscriptions.

It exists because the rows that migrated forward describe the pre-rebuild product the
owner rejected, and reading them beside package-first data is misleading. It is a
deliberate one-off, not a feature, and the only way back is the backup it takes first.

How it protects the data it is not erasing:

- The SQL refuses to run unless `current_database()` is `medication_tracker`.
- A backup is dumped **and its table of contents read back** before anything is
  removed, inside the container against a real file.
- The whole purge is one transaction, and it refuses to commit if any account would be
  left without a household.
- `TRUNCATE` names every table explicitly and deliberately omits `CASCADE`: a future
  table referencing one of these fails the statement by name rather than being
  silently erased.
- api and web are stopped for the few seconds the exclusive locks are held, then
  brought back. A failed purge still brings the site back up and says the data is
  unchanged.
- Only this project's containers are touched, and the outsider check from the deploy
  runs here too.

Rehearsed with Docker mocked across three scenarios — success, a failing transaction,
and an unrelated container disappearing — and the SQL itself was run against a real
PostgreSQL database seeded through the API: every care table emptied, accounts and
households intact, and the application then signed in and created new data normally.

### Manual fallback

Download the image artifact, verify its SHA-256 checksum, transfer it to the VPS, and
load it before running Compose. Do not build on the VPS while its swap is under
pressure.

For a source-build fallback, clone or update only this repository in its dedicated
application directory, then run:

```bash
docker compose -f compose.web.yml build --pull web
docker compose -f compose.web.yml up -d web
docker compose -f compose.web.yml ps
curl --fail http://127.0.0.1:3022/
```

Install `deploy/nginx/medicationtracker.rapidconfigs.com.conf` as a dedicated host
configuration, validate the complete host configuration with `nginx -t`, reload
Nginx, and issue a TLS certificate for the hostname. Keep Cloudflare SSL/TLS mode
at **Full (strict)** after the origin certificate is valid.

## Verification

- `https://medicationtracker.rapidconfigs.com` returns HTTP 200.
- Response headers include the static-container security headers.
- The container health status is healthy.
- No host port exposes PostgreSQL or the future API.
