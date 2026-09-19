-- Raise an error if a repeatable upgrade lost synthetic baseline data.
DO $verify$
BEGIN
    IF (SELECT count(*) FROM infrastructure.__ef_migrations_history) <> 10 THEN
        RAISE EXCEPTION 'Unexpected migration history count';
    END IF;
    IF (SELECT count(*) FROM inventory.packages) <> 2 THEN
        RAISE EXCEPTION 'Package rows were not preserved';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM inventory.packages
        WHERE id = '22222222-2222-4222-8222-222222222222'
          AND person_id = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'
          AND owner_person_id = person_id
          AND capacity_numerator = 3 AND capacity_denominator = 2
    ) THEN
        RAISE EXCEPTION 'Assigned package ownership was not backfilled';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM inventory.packages
        WHERE id = '33333333-3333-4333-8333-333333333333'
          AND person_id IS NULL AND owner_person_id IS NULL
    ) THEN
        RAISE EXCEPTION 'Unassigned package changed';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM inventory.count_sessions
        WHERE "Id" = '11111111-1111-4111-8111-111111111111'
          AND "LedgerEntryId" = 'ffffffff-ffff-4fff-8fff-ffffffffffff'
          AND "ObservedNumerator" = 3 AND "ObservedDenominator" = 2
          AND "BatchId" IS NULL
    ) THEN
        RAISE EXCEPTION 'Count session was not preserved';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM inventory.ledger_entries
        WHERE id = 'ffffffff-ffff-4fff-8fff-ffffffffffff'
          AND quantity_numerator = 3 AND quantity_denominator = 2
    ) THEN
        RAISE EXCEPTION 'Inventory ledger entry was not preserved';
    END IF;
END $verify$;
