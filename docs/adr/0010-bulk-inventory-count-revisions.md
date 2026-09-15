# ADR 0010: Bulk inventory counts use immutable revision batches

## Status

Accepted — 2026-09-15

## Context

Owner-defined V1 requires a household to count multiple medication inventories in
one operation and to correct an accepted count without rewriting audit history. The
existing `inventory.count_sessions` table stored one accepted medication count and
its reconciliation ledger entry, but it did not group a bulk operation or identify
later corrections.

## Decision

Add `inventory.count_batches` as the transaction and revision boundary. A batch owns
one or more existing `InventoryCount` rows through a nullable `BatchId`; the nullable
column preserves previously accepted rows without fabricating history.

A new bulk count:

- takes the household advisory transaction lock,
- validates a unique set of household medications and exact non-negative quantities,
- appends one reconciliation ledger entry and one count line per medication,
- stores a household-scoped idempotency receipt, and
- commits the batch, lines, ledger entries, and receipt atomically.

A correction must target the latest batch in its chain and contain the same inventory
scope. It appends a batch with `PreviousBatchId` and an incremented revision number,
then reconciles current projected stock to the replacement observations. Accepted
batches and their ledger entries are never updated or deleted.

## Consequences

- Count history and corrections are inspectable and replay-safe.
- Concurrent or stale attempts cannot fork a revision chain.
- Existing ungrouped count rows remain valid legacy audit records.
- The web client can offer one bulk form and revise only the latest visible batch.
- Package-level count allocation remains a separate concern; this decision counts the
  exact medication total and records the difference as unallocated reconciliation.
