-- Synthetic fixture shaped like the live production database on 2026-10-02.
--
-- Production has **ten** migrations applied, not eleven: the selected-weekday/interval
-- schedule migration was never deployed. So the real upgrade path is 10 → 11 → 12, and
-- this fixture is written against the ten-migration schema — it must not reference
-- recurrence_kind, weekday_mask or interval_days, which do not exist yet.
--
-- Every value below mirrors a shape actually present in production, read from it during
-- the deployment preflight:
--   * medications are all form 'tablet'; some inactive, some soft-deleted
--   * ledger reasons are acquisition, package_acquisition, administration,
--     count_reconciliation and refill
--   * six ledger rows carry quantity_numerator = 0 with reason 'acquisition', from
--     medications created with no stock
--   * administration ledger entries exist both with and without a package
--   * schedule types are 'scheduled' and 'as_needed'; day periods morning/evening/night
--   * change-event kinds are 'deleted'
--
-- Entirely invented data. No real person, prescription or medication record.

INSERT INTO identity.accounts (id, normalized_email, created_at)
VALUES ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'PRODSHAPE@EXAMPLE.INVALID', '2026-01-01T00:00:00Z');

INSERT INTO households.households (id, name, created_at)
VALUES ('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'Synthetic household', '2026-01-01T00:00:00Z');

-- Every production household has an owner membership, which is what the rebuild's
-- actor backfills resolve against.
INSERT INTO households.household_memberships (id, household_id, account_id, role, valid_from)
VALUES ('a0a0a0a0-a0a0-4a0a-8a0a-a0a0a0a0a0a0', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'owner', '2026-01-01T00:00:00Z');

INSERT INTO care.people (id, household_id, name, created_at)
VALUES ('cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'Synthetic person', '2026-01-01T00:00:00Z');

INSERT INTO care.medications (
    id, household_id, person_id, name, form, strength, active_ingredient, notes,
    category, tags, is_active, created_at, deleted_at)
VALUES
  -- Active, fully populated.
  ('dddddddd-dddd-4ddd-8ddd-dddddddddddd', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'Synthetic tablet', 'tablet', '500 mg',
   'synthetic-ingredient', 'synthetic note', 'synthetic-category',
   ARRAY['alpha', 'beta'], true, '2026-01-01T00:00:00Z', NULL),
  -- Soft-deleted: archived_at must take the deletion timestamp.
  ('d0d0d0d0-d0d0-4d0d-8d0d-d0d0d0d0d0d0', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   NULL, 'Deleted synthetic tablet', 'tablet', NULL, NULL, NULL, NULL,
   '{}'::text[], false, '2026-01-01T00:00:00Z', '2026-02-01T00:00:00Z'),
  -- Inactive without a deletion timestamp: archived, but the when is unknown.
  ('d1d1d1d1-d1d1-4d1d-8d1d-d1d1d1d1d1d1', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   NULL, 'Inactive synthetic tablet', 'tablet', NULL, NULL, NULL, NULL,
   '{}'::text[], false, '2026-01-01T00:00:00Z', NULL);

INSERT INTO inventory.inventory_items (id, household_id, medication_id, created_at)
VALUES
  ('eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', '2026-01-01T00:00:00Z'),
  ('e0e0e0e0-e0e0-4e0e-8e0e-e0e0e0e0e0e0', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'd0d0d0d0-d0d0-4d0d-8d0d-d0d0d0d0d0d0', '2026-01-01T00:00:00Z'),
  ('e1e1e1e1-e1e1-4e1e-8e1e-e1e1e1e1e1e1', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'd1d1d1d1-d1d1-4d1d-8d1d-d1d1d1d1d1d1', '2026-01-01T00:00:00Z');

-- An owned package that gets drawn from, and an unowned one that never does.
INSERT INTO inventory.packages (
    id, household_id, inventory_item_id, owner_person_id, person_id,
    capacity_numerator, capacity_denominator, created_at)
VALUES
  ('22222222-2222-4222-8222-222222222222', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
   'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 20, 1, '2026-01-01T00:00:00Z'),
  ('33333333-3333-4333-8333-333333333333', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, NULL, 20, 1, '2026-01-02T00:00:00Z');

INSERT INTO treatments.regimens (id, household_id, person_id, medication_id, created_at, deleted_at)
VALUES ('44444444-4444-4444-8444-444444444444', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
        '2026-01-01T00:00:00Z', NULL);

-- Ten-migration schema: no recurrence columns. Migration 11 adds them with the 'daily'
-- default, which is exactly what will happen to production's fifteen rows.
INSERT INTO treatments.regimen_versions (
    id, regimen_id, valid_from, valid_to, dose_numerator, dose_denominator, local_time,
    time_zone_id, created_at, schedule_type, day_period, meal_relation,
    minimum_interval_minutes)
VALUES
  ('55555555-5555-4555-8555-555555555555', '44444444-4444-4444-8444-444444444444',
   '2026-01-01', NULL, 1, 2, '08:00:00', 'Europe/Istanbul', '2026-01-01T00:00:00Z',
   'scheduled', 'morning', 'after_food', 240),
  ('56565656-5656-4565-8565-565656565656', '44444444-4444-4444-8444-444444444444',
   '2026-03-01', NULL, 1, 1, NULL, 'Europe/Istanbul', '2026-02-01T00:00:00Z',
   'as_needed', NULL, 'with_food', NULL);

INSERT INTO administrations.administration_events (
    id, household_id, person_id, medication_id, regimen_version_id, outcome,
    scheduled_for, occurred_at, recorded_at)
VALUES
  ('66666666-6666-4666-8666-666666666666', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
   '55555555-5555-4555-8555-555555555555', 'taken',
   '2026-03-02T05:00:00Z', '2026-03-02T05:05:00Z', '2026-03-02T05:05:00Z'),
  -- A dose whose consumption was recorded without a package, which production also has.
  ('67676767-6767-4676-8676-767676767676', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
   '56565656-5656-4565-8565-565656565656', 'taken',
   '2026-03-03T05:00:00Z', '2026-03-03T05:00:00Z', '2026-03-03T05:00:00Z');

INSERT INTO inventory.ledger_entries (
    id, household_id, inventory_item_id, administration_event_id, package_id,
    quantity_numerator, quantity_denominator, reason, occurred_at, recorded_at)
VALUES
  ('f1f1f1f1-f1f1-4f1f-8f1f-f1f1f1f1f1f1', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, '22222222-2222-4222-8222-222222222222',
   20, 1, 'package_acquisition', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
  ('f2f2f2f2-f2f2-4f2f-8f2f-f2f2f2f2f2f2', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, '33333333-3333-4333-8333-333333333333',
   20, 1, 'package_acquisition', '2026-01-02T00:00:00Z', '2026-01-02T00:00:00Z'),
  -- Consumption charged to a package.
  ('f3f3f3f3-f3f3-4f3f-8f3f-f3f3f3f3f3f3', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', '66666666-6666-4666-8666-666666666666',
   '22222222-2222-4222-8222-222222222222',
   -1, 2, 'administration', '2026-03-02T05:05:00Z', '2026-03-02T05:05:00Z'),
  -- Consumption with no package at all, which the rebuild must promote to a loose
  -- allocation rather than drop.
  ('f5f5f5f5-f5f5-4f5f-8f5f-f5f5f5f5f5f5', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', '67676767-6767-4676-8676-767676767676', NULL,
   -1, 1, 'administration', '2026-03-03T05:00:00Z', '2026-03-03T05:00:00Z'),
  ('ffffffff-ffff-4fff-8fff-ffffffffffff', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, NULL,
   3, 2, 'acquisition', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
  ('f6f6f6f6-f6f6-4f6f-8f6f-f6f6f6f6f6f6', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, NULL,
   1, 1, 'refill', '2026-01-04T00:00:00Z', '2026-01-04T00:00:00Z'),
  ('f7f7f7f7-f7f7-4f7f-8f7f-f7f7f7f7f7f7', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, NULL,
   1, 2, 'count_reconciliation', '2026-01-05T00:00:00Z', '2026-01-05T00:00:00Z'),
  -- THE ROW THAT MATTERS: a medication created with no stock wrote a zero-quantity
  -- 'acquisition'. Production holds six of these. A non-zero check constraint that did
  -- not exempt acquisitions would abort the production migration here.
  ('f4f4f4f4-f4f4-4f4f-8f4f-f4f4f4f4f4f4', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'e0e0e0e0-e0e0-4e0e-8e0e-e0e0e0e0e0e0', NULL, NULL,
   0, 1, 'acquisition', '2026-01-03T00:00:00Z', '2026-01-03T00:00:00Z'),
  ('f8f8f8f8-f8f8-4f8f-8f8f-f8f8f8f8f8f8', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   'e1e1e1e1-e1e1-4e1e-8e1e-e1e1e1e1e1e1', NULL, NULL,
   0, 1, 'acquisition', '2026-01-03T00:00:00Z', '2026-01-03T00:00:00Z');

INSERT INTO inventory.count_batches (id, household_id, account_id, previous_batch_id, revision_number, accepted_at)
VALUES ('77777777-7777-4777-8777-777777777777', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', NULL, 1, '2026-01-05T00:00:00Z');

INSERT INTO inventory.count_sessions (
    "Id", "HouseholdId", "BatchId", "InventoryItemId", "AccountId",
    "BeforeNumerator", "BeforeDenominator", "ObservedNumerator", "ObservedDenominator",
    "LedgerEntryId", "AcceptedAt")
VALUES
  ('11111111-1111-4111-8111-111111111111', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
   '77777777-7777-4777-8777-777777777777',
   'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
   0, 1, 1, 2, 'f7f7f7f7-f7f7-4f7f-8f7f-f7f7f7f7f7f7', '2026-01-05T00:00:00Z');

-- Production's only audit kind is 'deleted'.
INSERT INTO care.medication_change_events (
    id, household_id, medication_id, account_id, kind, previous_value, new_value, recorded_at)
VALUES ('88888888-8888-4888-8888-888888888888', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        'd0d0d0d0-d0d0-4d0d-8d0d-d0d0d0d0d0d0', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        'deleted', '{"name":"before"}', NULL, '2026-02-01T00:00:00Z');

INSERT INTO treatments.regimen_change_events (
    id, household_id, regimen_id, account_id, kind, previous_value, new_value, recorded_at)
VALUES ('99999999-9999-4999-8999-999999999999', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        '44444444-4444-4444-8444-444444444444', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        'deleted', NULL, NULL, '2026-02-01T00:00:00Z');
