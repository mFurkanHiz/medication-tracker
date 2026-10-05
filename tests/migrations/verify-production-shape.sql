-- Asserts the live production upgrade path, 10 → 11 → 12 → 13 → 14 → 15 → 16 → 17 → 18, preserved everything.
--
-- Production runs ten migrations, so applying the packaged SQL there runs BOTH the
-- schedule migration and the package-first rebuild in one pass. This file checks the
-- end state of that exact path against data shaped like production's.
DO $verify$
DECLARE
    owner_account CONSTANT uuid := 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    person CONSTANT uuid := 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
    active_medication CONSTANT uuid := 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
    deleted_medication CONSTANT uuid := 'd0d0d0d0-d0d0-4d0d-8d0d-d0d0d0d0d0d0';
    inactive_medication CONSTANT uuid := 'd1d1d1d1-d1d1-4d1d-8d1d-d1d1d1d1d1d1';
    drawn_package CONSTANT uuid := '22222222-2222-4222-8222-222222222222';
    untouched_package CONSTANT uuid := '33333333-3333-4333-8333-333333333333';
    scheduled_version CONSTANT uuid := '55555555-5555-4555-8555-555555555555';
    as_needed_version CONSTANT uuid := '56565656-5656-4565-8565-565656565656';
    package_dose CONSTANT uuid := '66666666-6666-4666-8666-666666666666';
    loose_dose CONSTANT uuid := '67676767-6767-4676-8676-767676767676';
    zero_acquisition CONSTANT uuid := 'f4f4f4f4-f4f4-4f4f-8f4f-f4f4f4f4f4f4';
BEGIN
    -- Both the schedule migration and the rebuild must have been applied by one pass.
    IF (SELECT count(*) FROM infrastructure.__ef_migrations_history) <> 18 THEN
        RAISE EXCEPTION 'Expected eighteen migrations after the production-path upgrade, found %',
            (SELECT count(*) FROM infrastructure.__ef_migrations_history);
    END IF;

    -- --------------------------------------------------- the zero-acquisition rows ---
    -- The reason this fixture exists. Production holds six ledger rows with
    -- quantity_numerator = 0 and reason 'acquisition', from medications created with no
    -- stock. A non-zero constraint that only exempted count adjustments would have
    -- aborted the migration on them.
    IF (SELECT count(*) FROM inventory.ledger_entries WHERE quantity_numerator = 0) <> 2 THEN
        RAISE EXCEPTION 'Zero-quantity acquisitions were lost: %',
            (SELECT count(*) FROM inventory.ledger_entries WHERE quantity_numerator = 0);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM inventory.ledger_entries
        WHERE id = zero_acquisition
          AND quantity_numerator = 0
          AND entry_type = 'Acquire'
    ) THEN
        RAISE EXCEPTION 'The zero-quantity acquisition was dropped or retyped';
    END IF;

    -- ----------------------------------------------------------------- the catalog ---
    IF (SELECT count(*) FROM catalog.medication_definitions) <> 3 THEN
        RAISE EXCEPTION 'Medication definitions were not carried across the rename';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM catalog.medication_definitions
        WHERE id = active_medication AND form = 'Tablet' AND unit = 'Tablet'
          AND archived_at IS NULL AND legacy_person_id = person
          AND active_ingredients = ARRAY['synthetic-ingredient']
    ) THEN
        RAISE EXCEPTION 'The active medication lost detail across the rebuild';
    END IF;

    -- A soft-deleted medication keeps its own deletion time.
    IF NOT EXISTS (
        SELECT 1 FROM catalog.medication_definitions
        WHERE id = deleted_medication AND archived_at = '2026-02-01T00:00:00Z'
    ) THEN
        RAISE EXCEPTION 'A soft-deleted medication lost its deletion timestamp';
    END IF;

    -- An inactive medication with no deletion time is still archived, dated from the
    -- migration because the real time is unknown.
    IF NOT EXISTS (
        SELECT 1 FROM catalog.medication_definitions
        WHERE id = inactive_medication AND archived_at IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'An inactive medication was silently reactivated';
    END IF;

    -- -------------------------------------------------------------- plan versions ---
    -- Migration 11 gives every pre-existing version the daily default; migration 12
    -- then maps that onto the enum name.
    IF NOT EXISTS (
        SELECT 1 FROM treatments.plan_versions
        WHERE id = scheduled_version
          AND version_number = 1
          AND kind = 'Scheduled'
          AND pattern = 'Daily'
          AND weekday_mask IS NULL
          AND interval_days IS NULL
          AND day_period = 'Morning'
          AND meal_relation = 'AfterFood'
          AND minimum_interval_minutes = 240
          AND created_by_account_id = owner_account
    ) THEN
        RAISE EXCEPTION 'The scheduled plan version was not transformed correctly';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM treatments.plan_versions
        WHERE id = as_needed_version
          AND version_number = 2
          AND kind = 'AsNeeded'
          AND pattern = 'Daily'
          AND meal_relation = 'WithFood'
          AND local_time IS NULL
    ) THEN
        RAISE EXCEPTION 'The as-needed plan version was not transformed correctly';
    END IF;

    -- ------------------------------------------------------------------- packages ---
    IF NOT EXISTS (
        SELECT 1 FROM inventory.packages
        WHERE id = drawn_package
          AND owner_person_id = person AND holder_person_id = person
          AND nominal_capacity_numerator = 20
          AND medication_definition_id = active_medication
          AND state = 'Opened' AND opened_at IS NOT NULL
          AND created_by_account_id = owner_account
          AND ordinal = 1
    ) THEN
        RAISE EXCEPTION 'The drawn-from package was not reshaped correctly';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM inventory.packages
        WHERE id = untouched_package
          AND owner_person_id IS NULL AND holder_person_id IS NULL
          AND state = 'Sealed' AND opened_at IS NULL
          AND ordinal = 2
    ) THEN
        RAISE EXCEPTION 'The untouched package was not left sealed';
    END IF;

    -- --------------------------------------------------------------------- ledger ---
    IF (SELECT count(*) FROM inventory.ledger_entries) <> 9 THEN
        RAISE EXCEPTION 'Ledger entries were lost: %', (SELECT count(*) FROM inventory.ledger_entries);
    END IF;

    IF EXISTS (SELECT 1 FROM inventory.ledger_entries WHERE entry_type = '') THEN
        RAISE EXCEPTION 'Some ledger entries were left untyped';
    END IF;

    -- Every production reason must land on a real entry type, never the fallback.
    IF EXISTS (
        SELECT 1 FROM inventory.ledger_entries
        WHERE reason IN ('acquisition', 'package_acquisition', 'refill')
          AND entry_type <> 'Acquire'
    ) THEN
        RAISE EXCEPTION 'An acquisition reason was mapped to the wrong entry type';
    END IF;

    IF EXISTS (
        SELECT 1 FROM inventory.ledger_entries
        WHERE reason = 'administration' AND entry_type <> 'Consume'
    ) THEN
        RAISE EXCEPTION 'A consumption was mapped to the wrong entry type';
    END IF;

    IF EXISTS (
        SELECT 1 FROM inventory.ledger_entries
        WHERE reason = 'count_reconciliation' AND entry_type <> 'CountAdjustment'
    ) THEN
        RAISE EXCEPTION 'A count reconciliation was mapped to the wrong entry type';
    END IF;

    -- A migration must not change how much medication the household has.
    -- 20 + 20 - 1/2 - 1 + 3/2 + 1 + 1/2 + 0 + 0 = 41.5
    IF (SELECT sum(quantity_numerator::numeric / quantity_denominator)
          FROM inventory.ledger_entries) <> 41.5 THEN
        RAISE EXCEPTION 'Migrating changed the stock total: %',
            (SELECT sum(quantity_numerator::numeric / quantity_denominator)
               FROM inventory.ledger_entries);
    END IF;

    -- ------------------------------------------------------------ administrations ---
    IF NOT EXISTS (
        SELECT 1 FROM administrations.administration_events
        WHERE id = package_dose
          AND medication_definition_id = active_medication
          AND plan_version_id = scheduled_version
          AND outcome = 'Taken'
          AND stock_source = 'TrackedInventory'
          AND actual_quantity_numerator = 1 AND actual_quantity_denominator = 2
          AND actor_account_id = owner_account
    ) THEN
        RAISE EXCEPTION 'The package-sourced dose was not reshaped correctly';
    END IF;

    -- -------------------------------------------------------------- allocations ---
    -- Both historical consumptions become allocations: one against a package, one
    -- against loose stock.
    IF (SELECT count(*) FROM administrations.allocations) <> 2 THEN
        RAISE EXCEPTION 'Historical consumption was not promoted to allocations: %',
            (SELECT count(*) FROM administrations.allocations);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM administrations.allocations
        WHERE administration_event_id = package_dose
          AND package_id = drawn_package
          AND quantity_numerator = 1 AND quantity_denominator = 2
          AND is_active
    ) THEN
        RAISE EXCEPTION 'The package allocation does not match its ledger entry';
    END IF;

    -- A consumption with no package becomes a loose allocation rather than vanishing.
    IF NOT EXISTS (
        SELECT 1 FROM administrations.allocations
        WHERE administration_event_id = loose_dose
          AND package_id IS NULL
          AND quantity_numerator = 1 AND quantity_denominator = 1
          AND is_active
    ) THEN
        RAISE EXCEPTION 'The package-independent consumption was not promoted';
    END IF;

    -- The invariant the allocation model exists to guarantee, on migrated history.
    IF EXISTS (
        SELECT 1
          FROM administrations.administration_events e
          LEFT JOIN administrations.allocations a
            ON a.administration_event_id = e.id AND a.is_active
         WHERE e.stock_source = 'TrackedInventory'
         GROUP BY e.id, e.actual_quantity_numerator, e.actual_quantity_denominator
        HAVING coalesce(sum(a.quantity_numerator::numeric / a.quantity_denominator), 0)
               <> e.actual_quantity_numerator::numeric / e.actual_quantity_denominator
    ) THEN
        RAISE EXCEPTION 'A migrated dose''s allocations do not sum to the amount administered';
    END IF;

    -- -------------------------------------------------------------- audit history ---
    IF NOT EXISTS (
        SELECT 1 FROM catalog.medication_definition_change_events
        WHERE id = '88888888-8888-4888-8888-888888888888' AND kind = 'Deleted'
    ) THEN
        RAISE EXCEPTION 'Medication audit history was lost';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM treatments.plan_change_events
        WHERE id = '99999999-9999-4999-8999-999999999999' AND kind = 'Deleted'
    ) THEN
        RAISE EXCEPTION 'Plan audit history was lost';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM inventory.count_sessions
        WHERE id = '11111111-1111-4111-8111-111111111111'
          AND batch_id = '77777777-7777-4777-8777-777777777777'
          AND observed_numerator = 1 AND observed_denominator = 2
    ) THEN
        RAISE EXCEPTION 'The count session was lost across the column renames';
    END IF;
END $verify$;
