-- Synthetic fixture representing a production-shaped database immediately before the
-- package-first rebuild: every pre-rebuild migration applied, and at least one row in
-- each table the rebuild renames, reshapes or transforms.
--
-- Entirely invented data. No real person, prescription or medication record.

INSERT INTO identity.accounts (id, normalized_email, created_at)
VALUES ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'MIGRATION@EXAMPLE.INVALID', '2026-01-01T00:00:00Z');

INSERT INTO households.households (id, name, created_at)
VALUES ('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'Synthetic household', '2026-01-01T00:00:00Z');

-- The rebuild attributes rows that predate actor tracking to the household owner, so
-- the owner membership must exist for those backfills to resolve.
INSERT INTO households.household_memberships (id, household_id, account_id, role, valid_from)
VALUES ('a0a0a0a0-a0a0-4a0a-8a0a-a0a0a0a0a0a0', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'owner', '2026-01-01T00:00:00Z');

INSERT INTO care.people (id, household_id, name, created_at)
VALUES ('cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'Synthetic person', '2026-01-01T00:00:00Z');

-- An active medication carrying the person link, free-text ingredient, category and
-- tags that the rebuild has to reshape, plus an archived one to prove archive state
-- survives the move to catalog.medication_definitions.
INSERT INTO care.medications (
    id, household_id, person_id, name, form, strength, active_ingredient, notes,
    category, tags, is_active, created_at, deleted_at)
VALUES
  ('dddddddd-dddd-4ddd-8ddd-dddddddddddd', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'Synthetic tablet', 'tablet', '500 mg',
   'synthetic-ingredient', 'synthetic note', 'synthetic-category',
   ARRAY['alpha', 'beta'], true, '2026-01-01T00:00:00Z', NULL),
  ('d0d0d0d0-d0d0-4d0d-8d0d-d0d0d0d0d0d0', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   NULL, 'Retired synthetic tablet', 'tablet', NULL, NULL, NULL, NULL,
   '{}'::text[], false, '2026-01-01T00:00:00Z', '2026-02-01T00:00:00Z');

INSERT INTO inventory.inventory_items (id, household_id, medication_id, created_at)
VALUES
  ('eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', '2026-01-01T00:00:00Z'),
  ('e0e0e0e0-e0e0-4e0e-8e0e-e0e0e0e0e0e0', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'd0d0d0d0-d0d0-4d0d-8d0d-d0d0d0d0d0d0', '2026-01-01T00:00:00Z');

-- Package 2222 is owned and will be drawn from, so the rebuild must mark it opened.
-- Package 3333 is unowned and untouched, so it must stay sealed.
INSERT INTO inventory.packages (
    id, household_id, inventory_item_id, owner_person_id, person_id,
    capacity_numerator, capacity_denominator, created_at)
VALUES
  ('22222222-2222-4222-8222-222222222222', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
   'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 3, 2, '2026-01-01T00:00:00Z'),
  ('33333333-3333-4333-8333-333333333333', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, NULL, 1, 1, '2026-01-02T00:00:00Z');

INSERT INTO treatments.regimens (id, household_id, person_id, medication_id, created_at, deleted_at)
VALUES ('44444444-4444-4444-8444-444444444444', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
        '2026-01-01T00:00:00Z', NULL);

-- Two versions of one plan, so the rebuild has to number them by creation order, plus
-- the named-period, meal-relation and weekday-recurrence values it must map to enum
-- names.
INSERT INTO treatments.regimen_versions (
    id, regimen_id, valid_from, valid_to, dose_numerator, dose_denominator, local_time,
    time_zone_id, created_at, schedule_type, day_period, meal_relation,
    minimum_interval_minutes, recurrence_kind, weekday_mask, interval_days)
VALUES
  ('55555555-5555-4555-8555-555555555555', '44444444-4444-4444-8444-444444444444',
   '2026-01-01', NULL, 1, 2, '08:00:00', 'Europe/Istanbul', '2026-01-01T00:00:00Z',
   'scheduled', 'morning', 'after_food', 240, 'daily', NULL, NULL),
  ('56565656-5656-4565-8565-565656565656', '44444444-4444-4444-8444-444444444444',
   '2026-03-01', NULL, 1, 1, '09:00:00', 'Europe/Istanbul', '2026-02-01T00:00:00Z',
   'scheduled', NULL, 'with_food', NULL, 'weekdays', 5, NULL);

-- A taken dose against the newer version and a skipped dose against the older one.
-- The rebuild must give the taken row an administered amount copied from its plan
-- version, and leave the skipped row's amount null.
INSERT INTO administrations.administration_events (
    id, household_id, person_id, medication_id, regimen_version_id, outcome,
    scheduled_for, occurred_at, recorded_at)
VALUES
  ('66666666-6666-4666-8666-666666666666', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
   '56565656-5656-4565-8565-565656565656', 'taken',
   '2026-03-02T06:00:00Z', '2026-03-02T06:05:00Z', '2026-03-02T06:05:00Z'),
  ('67676767-6767-4676-8676-767676767676', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
   '55555555-5555-4555-8555-555555555555', 'skipped',
   '2026-01-05T05:00:00Z', '2026-01-05T05:00:00Z', '2026-01-05T05:00:00Z');

INSERT INTO inventory.ledger_entries (
    id, household_id, inventory_item_id, administration_event_id, package_id,
    quantity_numerator, quantity_denominator, reason, occurred_at, recorded_at)
VALUES
  -- Acquisitions that give each package its balance.
  ('f1f1f1f1-f1f1-4f1f-8f1f-f1f1f1f1f1f1', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, '22222222-2222-4222-8222-222222222222',
   3, 2, 'package_acquisition', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
  ('f2f2f2f2-f2f2-4f2f-8f2f-f2f2f2f2f2f2', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, '33333333-3333-4333-8333-333333333333',
   1, 1, 'package_acquisition', '2026-01-02T00:00:00Z', '2026-01-02T00:00:00Z'),
  -- The consumption behind the taken dose. The rebuild must promote this to an
  -- allocation, flipping its sign.
  ('f3f3f3f3-f3f3-4f3f-8f3f-f3f3f3f3f3f3', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', '66666666-6666-4666-8666-666666666666',
   '22222222-2222-4222-8222-222222222222',
   -1, 1, 'administration', '2026-03-02T06:05:00Z', '2026-03-02T06:05:00Z'),
  -- Loose stock with no package.
  ('ffffffff-ffff-4fff-8fff-ffffffffffff', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, NULL,
   3, 2, 'count', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
  -- A count that matched. The superseded flow wrote a zero delta for this, and the
  -- rebuilt non-zero check constraint must keep tolerating it for count adjustments.
  ('f4f4f4f4-f4f4-4f4f-8f4f-f4f4f4f4f4f4', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'e0e0e0e0-e0e0-4e0e-8e0e-e0e0e0e0e0e0', NULL, NULL,
   0, 1, 'count_reconciliation', '2026-01-03T00:00:00Z', '2026-01-03T00:00:00Z');

INSERT INTO inventory.count_batches (id, household_id, account_id, previous_batch_id, revision_number, accepted_at)
VALUES ('77777777-7777-4777-8777-777777777777', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', NULL, 1, '2026-01-03T00:00:00Z');

INSERT INTO inventory.count_sessions (
    "Id", "HouseholdId", "BatchId", "InventoryItemId", "AccountId",
    "BeforeNumerator", "BeforeDenominator", "ObservedNumerator", "ObservedDenominator",
    "LedgerEntryId", "AcceptedAt")
VALUES
  ('11111111-1111-4111-8111-111111111111', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', NULL,
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
   0, 1, 3, 2, 'ffffffff-ffff-4fff-8fff-ffffffffffff', '2026-01-01T00:00:00Z'),
  ('12121212-1212-4121-8121-121212121212', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   '77777777-7777-4777-8777-777777777777',
   'e0e0e0e0-e0e0-4e0e-8e0e-e0e0e0e0e0e0', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
   0, 1, 0, 1, 'f4f4f4f4-f4f4-4f4f-8f4f-f4f4f4f4f4f4', '2026-01-03T00:00:00Z');

-- Audit rows whose free-text kind the rebuild maps to enum names.
INSERT INTO care.medication_change_events (
    id, household_id, medication_id, account_id, kind, previous_value, new_value, recorded_at)
VALUES ('88888888-8888-4888-8888-888888888888', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        'dddddddd-dddd-4ddd-8ddd-dddddddddddd', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        'updated', '{"name":"before"}', '{"name":"after"}', '2026-01-04T00:00:00Z');

INSERT INTO treatments.regimen_change_events (
    id, household_id, regimen_id, account_id, kind, previous_value, new_value, recorded_at)
VALUES ('99999999-9999-4999-8999-999999999999', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        '44444444-4444-4444-8444-444444444444', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        'updated', NULL, '{"dose":"1/1"}', '2026-02-01T00:00:00Z');
