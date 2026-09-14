# Web VPS deployment

Status: V1 web/API release deployed and accepted on 2026-09-14.

Current application image source: `11b9fd5c32511af8fea9df68189768e5e6dc86d0`.
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

The release image is built and validated in GitHub Actions. Download the image
artifact, verify its SHA-256 checksum, transfer it to the VPS, and load it before
running Compose. Do not build on the VPS while its swap is under pressure.

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
