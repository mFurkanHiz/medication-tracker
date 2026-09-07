# ADR 0001: Modular monolith with offline-first mobile

- Status: Accepted
- Date: 2026-09-07

## Decision

Use a .NET 10 modular monolith with PostgreSQL, a Next.js web client, and an Expo React Native mobile client using SQLite and an explicit synchronization protocol.

## Context

The product needs transactional inventory calculations, reliable audit history, a public portfolio-quality architecture, web administration, mobile reminders, and useful operation without a network connection. The target VPS has constrained memory and already hosts many containers.

## Consequences

- PostgreSQL is preferred over another MSSQL container on the current VPS.
- Mobile remains responsive and usable during API outages.
- Synchronization, authorization, conflicts, and notification reliability require first-class tests.
- Services remain deployable as a small number of containers while modules retain clear boundaries.
