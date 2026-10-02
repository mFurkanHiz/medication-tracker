# Security and privacy threat model

## Protected assets

- Medication, diagnosis, symptom, treatment, and adherence data
- Household relationships and caregiver access
- Attachments and health documents
- Authentication credentials, sessions, and device tokens
- Audit, synchronization, and inventory history
- Backups and exports

## Trust boundaries

- Mobile local database and operating-system notification storage
- Browser and web application
- Public network and reverse proxy
- API and background processing
- PostgreSQL and attachment storage
- Push-notification providers
- GitHub Actions and deployment credentials
- Household invitations and shared access

## Initial threats and controls

| Threat | Initial control |
| --- | --- |
| Access to another household | Server-side household scope on every protected operation; deny by default |
| Lost or shared phone | OS secure storage for tokens, short sessions, remote revocation, optional biometric lock |
| Offline data disclosure | SQLCipher evaluation, minimized notification text, no secrets in ordinary storage |
| Duplicate offline command | Client idempotency key with server uniqueness constraint |
| Silent sync conflict | Optimistic version check and explicit conflict resolution |
| Inventory/history tampering | Append-only domain ledger, reversal records, actor/device/time audit metadata |
| Malicious attachment | Private object storage, content/type limits, scanning, short-lived URLs |
| Leaked deployment secret | GitHub/environment secrets, least privilege, rotation, no secret values in logs |
| Sensitive push content | Generic notification payload; fetch details only after authenticated app open |
| Public demo exposes real data | Synthetic seed data and automated secret/PII review before release |
| Subscription bypass | Server-calculated entitlements separate from household roles |

## Review findings — 2026-10-02 takeover audit

### Resolved

**Identity no longer travels as a mutated request header.** The superseded design had
middleware strip a client-supplied `X-Account-Id`, then re-inject the account id from
the validated session so endpoints could bind it with `[FromHeader]`. Audited and
confirmed **not** an authentication bypass — the strip ran unconditionally, before any
route, and `AuthenticationBoundaryTests` covered it. But it made request-header
rewriting load-bearing for authorisation: one route registered outside the guarded path
prefix, or one missed strip, would have become an impersonation hole. Endpoints now read
`HttpContext.User`, the header is stripped and never repopulated, and a PostgreSQL test
asserts that supplying it grants nothing. See ADR 0013, finding 9.

**Every rebuilt endpoint re-covered for cross-household access.** A PostgreSQL test
drives a second household's signed-in client against another household's inventory,
workspace, activity, today, forecast, stock-add, package-pin and catalog-edit endpoints
and asserts 403 on each, then asserts the target household's stock is unchanged.

### Controls added by the rebuild

| Control | Where |
| --- | --- |
| Correction cannot create negative stock | A correction onto a source lacking the stock is refused rather than allowed to overdraw |
| Retiring a package cannot silently delete stock | Lost or disposed stock leaves through a typed ledger entry with an actor and reason |
| Concurrent consumption cannot overdraw | Per-household advisory lock inside the transaction, proven by a concurrent-request test |
| Replayed command cannot double-consume | Unique index on `(household_id, idempotency_key)`, proven by a replay test |
| Allocation history cannot be rewritten | Append-only ledger; superseded allocations retained and marked inactive |
| Count history cannot be rewritten | Corrections append a linked revision; only the newest revision may be revised |
| At most one pinned package per medication | Filtered unique index, not application convention |

### Outstanding

- **Export authorisation is unimplemented**, because export itself is unimplemented. It
  must be covered by a cross-household test when it lands.
- **Log redaction is unverified.** No audit has confirmed that medication names, person
  names or quantities stay out of application logs.
- **Mobile secure storage and notification privacy** remain unreviewed on a physical
  device.
- **Rate limiting covers only the auth endpoints.** Whether dose recording and sync need
  their own limits is undecided.
- **Attachments do not exist yet**, so their controls are untested.

## Required reviews before production

- KVKK legal basis, disclosure, explicit consent where applicable, retention, deletion, and transfer review
- Data-flow diagram and processor inventory
- Backup encryption and restore drill
- Authorization integration tests for every module
- Mobile storage and notification privacy review
- Dependency, container, and secret scanning
- Incident response and breach-notification procedure

This document is an engineering baseline and is not legal advice.
