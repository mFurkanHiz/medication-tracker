# ADR 0004: Static web preview hosting

- Status: Accepted
- Date: 2026-09-09

## Context

The first release needs a public, TLS-protected web preview before production API
authentication and VPS operations are ready. The preview must not expose database
credentials, real health data, or an unauthenticated API.

## Decision

- Export the Next.js web application as static files.
- Host the public preview independently from the API and PostgreSQL. The static
  artifact may run on Sites or in the repository's loopback-bound VPS container;
  the selected public hostname must point to exactly one of those origins.
- Keep only synthetic presentation data in the static bundle.
- Store the non-secret hosting project identifier in `.openai/hosting.json`.
- Attach `medicationtracker.rapidconfigs.com` through explicit DNS validation.
- Keep the API and PostgreSQL deployment as a separate security boundary; the
  database will never be published directly to the internet.

## Consequences

The project has an immediately viewable public surface without expanding access to
health data. Interactive account and household workflows remain on the offline-first
mobile client until production authentication, API deployment, consent, retention,
backup, and incident-response controls are complete.
