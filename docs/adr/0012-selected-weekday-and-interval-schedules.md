# ADR 0012: Scheduled recurrence uses local dates and immutable version rules

## Status

Accepted — 2026-09-16

## Context

The V1 daily plan already supports daily and as-needed use, exact local time, and
named periods. Owner-defined V1 also requires selected weekdays and arbitrary
day intervals without confusing elapsed UTC hours with local calendar days.

## Decision

Each regimen version stores a recurrence kind: `daily`, `weekdays`, or `interval`.
Weekdays use a seven-bit Monday-first mask. Intervals use an inclusive effective
start date as their anchor and a positive integer number of local calendar days.
As-needed versions have no recurrence restriction. Existing versions migrate to
`daily` without changing their history.

The same pure due-date rule filters the today projection and validates scheduled
administrations. A newer version valid for a day supersedes an older version for
both reads and writes. Editing defaults to the current local date and rejects a
past effective date, so an edit cannot retroactively alter the historical plan.
The existing local-time-to-instant policy handles daylight-saving gaps; recurrence
itself uses `DateOnly` arithmetic rather than 24-hour increments. Forecasting
iterates actual scheduled local dates across a bounded ten-year horizon instead
of treating a selected-weekday or interval plan as daily consumption.

## Consequences

- Daily plans remain backward-compatible; no old regimen rows are rewritten.
- Interval plans require a start date and specify exactly which local dates are due.
- Today, administration acceptance, and depletion projection agree on due dates.
- A named day period remains descriptive; its midnight occurrence key is not a
  reminder time or a medical dosing recommendation.
