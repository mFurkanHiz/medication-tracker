# Authenticated web application on the existing VPS

The synthetic landing page did not satisfy web product acceptance. The web client
now uses the same-origin ASP.NET API through its Nginx container. PostgreSQL and
the API stay on the project's private Docker network, without published ports.

Accounts use ASP.NET PasswordHasher. Random seven-day sessions are stored only as
SHA-256 hashes in PostgreSQL. The browser receives a Secure, HttpOnly, host-only,
SameSite=Strict cookie. Mutations require a custom client header and no cross-origin
CORS policy is enabled. Client-supplied account UUIDs are discarded. The existing
care endpoint header adapter is populated only after server session validation.
Identity, household authorization and commercial entitlements remain separate.

Web operations include persons, tablet medications, exact fractional stock, daily
schedules, taken/skipped events, acquisitions and accepted inventory counts. Counts
retain the before/observed values and actor and append ledger reconciliation.
Household advisory locks serialize sync and inventory commands. Duplicate scheduled
administrations return the original outcome or a conflict, without double consumption.

This increment is not completion of the full roadmap: mobile authenticated sync,
count revisions, lending/returns, reliable notifications, privacy operations and
advanced schedule rules still require implementation and verification.

Deployment requires a successful PostgreSQL CI integration suite, reviewed explicit
migrations and isolated containers with memory limits. No changes to other VPS
applications are needed. Real health data must not be used for validation.
