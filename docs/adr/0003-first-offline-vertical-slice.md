# ADR 0003: First offline vertical slice

- Status: Accepted
- Date: 2026-09-09

## Context

Sprint 1 must prove that a daily tablet administration can be recorded without connectivity, survive an app restart, consume exact fractional inventory once, and later be replayed safely to a household-scoped API.

## Decision

- The mobile app owns a versioned SQLite schema and enables write-ahead logging.
- User actions commit the local administration, its negative inventory ledger entry, and a durable outbox command in one exclusive transaction.
- Quantities are integer numerator/denominator pairs. They are normalized and formatted without binary floating-point arithmetic.
- The API derives the consumed dose from the effective regimen version; it does not trust a client-supplied quantity.
- Each sync command has a client-created idempotency key unique within a household. The administration, ledger movement, and processed-command receipt commit in one PostgreSQL transaction.
- Every household endpoint checks a currently effective household membership. Account identity, membership, and subscription/entitlement remain separate.
- Regimen edits create effective-dated versions. Administration and inventory history are append-only.

## Consequences

The first slice works locally before a remote account is configured and supports safe retry once networking is added. Concurrent delivery of the same command is also protected by database uniqueness, while a later sync worker will add conflict response handling and cursor-based downloads.
