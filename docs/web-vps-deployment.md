# Web VPS deployment

Status: authenticated web/API release deployed on 2026-09-11.

Current application image source: `598394bb26702655da1a57c15f2e2360e7629033`.
CI `34518807194` passed all 11 API tests, web lint/build and mobile TypeScript.
`deploy/smoke-test.ps1` passed against the public HTTPS URL, covering registration,
medication, schedule, administration replay, exact forecast, count and logout.

Use `compose.production.yml` and `deploy/deploy-production.sh` for the authenticated
release. Place the successful CI image artifact in `.deploy/medication-tracker-web.tar.gz`.
The script uses private runtime credentials, starts the project database, backs it
up, applies the image's explicit migrations and checks API readiness. Never print
`.env.production` or commit it. The pre-migration backup is not an offsite backup
or a substitute for the outstanding encrypted backup/restore production review.

- Public URL: `https://medicationtracker.rapidconfigs.com`
- VPS: `srv925801` (`31.97.53.159`)
- Application directory: `/opt/medication-tracker`
- Container: `medication-tracker-web-1`
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
