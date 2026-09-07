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

## Required reviews before production

- KVKK legal basis, disclosure, explicit consent where applicable, retention, deletion, and transfer review
- Data-flow diagram and processor inventory
- Backup encryption and restore drill
- Authorization integration tests for every module
- Mobile storage and notification privacy review
- Dependency, container, and secret scanning
- Incident response and breach-notification procedure

This document is an engineering baseline and is not legal advice.
