-- Synthetic fixture for upgrading the seven-migration production baseline.
INSERT INTO identity.accounts (id, normalized_email, created_at)
VALUES ('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'MIGRATION@EXAMPLE.INVALID', '2026-01-01T00:00:00Z');

INSERT INTO households.households (id, name, created_at)
VALUES ('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'Synthetic household', '2026-01-01T00:00:00Z');

INSERT INTO care.people (id, household_id, name, created_at)
VALUES ('cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'Synthetic person', '2026-01-01T00:00:00Z');

INSERT INTO care.medications (id, household_id, person_id, name, form, created_at)
VALUES ('dddddddd-dddd-4ddd-8ddd-dddddddddddd', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 'Synthetic tablet', 'tablet', '2026-01-01T00:00:00Z');

INSERT INTO inventory.inventory_items (id, household_id, medication_id, created_at)
VALUES ('eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', '2026-01-01T00:00:00Z');

INSERT INTO inventory.ledger_entries (id, household_id, inventory_item_id, quantity_numerator, quantity_denominator, reason, occurred_at, recorded_at)
VALUES ('ffffffff-ffff-4fff-8fff-ffffffffffff', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', 3, 2, 'count', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');

INSERT INTO inventory.count_sessions ("Id", "HouseholdId", "InventoryItemId", "AccountId", "BeforeNumerator", "BeforeDenominator", "ObservedNumerator", "ObservedDenominator", "LedgerEntryId", "AcceptedAt")
VALUES ('11111111-1111-4111-8111-111111111111', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 0, 1, 3, 2, 'ffffffff-ffff-4fff-8fff-ffffffffffff', '2026-01-01T00:00:00Z');

INSERT INTO inventory.packages (id, household_id, inventory_item_id, person_id, capacity_numerator, capacity_denominator, created_at)
VALUES
  ('22222222-2222-4222-8222-222222222222', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', 3, 2, '2026-01-01T00:00:00Z'),
  ('33333333-3333-4333-8333-333333333333', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', NULL, 1, 1, '2026-01-01T00:00:00Z');
