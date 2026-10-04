-- Fails loudly if upgrading the production-shaped baseline lost or corrupted data.
--
-- The package-first rebuild renames five tables, reshapes columns in place and
-- transforms stored values. EF scaffolded that as drop-and-create, which would have
-- silently emptied five production tables, and it guessed two column renames by
-- position so that plan-version identifiers would have landed in a medication column.
-- These assertions exist so neither class of mistake can reach production unnoticed.
DO $verify$
DECLARE
    owner_account CONSTANT uuid := 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    household CONSTANT uuid := 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    person CONSTANT uuid := 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
    active_medication CONSTANT uuid := 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
    archived_medication CONSTANT uuid := 'd0d0d0d0-d0d0-4d0d-8d0d-d0d0d0d0d0d0';
    drawn_package CONSTANT uuid := '22222222-2222-4222-8222-222222222222';
    sealed_package CONSTANT uuid := '33333333-3333-4333-8333-333333333333';
    plan CONSTANT uuid := '44444444-4444-4444-8444-444444444444';
    first_version CONSTANT uuid := '55555555-5555-4555-8555-555555555555';
    second_version CONSTANT uuid := '56565656-5656-4565-8565-565656565656';
    taken_dose CONSTANT uuid := '66666666-6666-4666-8666-666666666666';
    skipped_dose CONSTANT uuid := '67676767-6767-4676-8676-767676767676';
    consume_entry CONSTANT uuid := 'f3f3f3f3-f3f3-4f3f-8f3f-f3f3f3f3f3f3';
BEGIN
    IF (SELECT count(*) FROM infrastructure.__ef_migrations_history) <> 14 THEN
        RAISE EXCEPTION 'Unexpected migration history count: %',
            (SELECT count(*) FROM infrastructure.__ef_migrations_history);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM infrastructure.__ef_migrations_history
        WHERE "MigrationId" = '20261002162013_PackageFirstDomainRebuild'
    ) THEN
        RAISE EXCEPTION 'Package-first rebuild migration was not applied';
    END IF;

    -- ---------------------------------------------------------------- catalog ----
    -- Renamed from care.medications, so its rows must still be here.
    IF (SELECT count(*) FROM catalog.medication_definitions) <> 2 THEN
        RAISE EXCEPTION 'Medication definitions were not carried across the rename: %',
            (SELECT count(*) FROM catalog.medication_definitions);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM catalog.medication_definitions
        WHERE id = active_medication
          AND household_id = household
          AND name = 'Synthetic tablet'
          AND strength = '500 mg'
          AND form = 'Tablet'
          AND unit = 'Tablet'
          AND active_ingredients = ARRAY['synthetic-ingredient']
          AND tags = ARRAY['alpha', 'beta']
          AND category = 'synthetic-category'
          AND notes = 'synthetic note'
          AND archived_at IS NULL
          -- The former person link is preserved rather than destroyed.
          AND legacy_person_id = person
    ) THEN
        RAISE EXCEPTION 'Active medication definition lost detail across the rebuild';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM catalog.medication_definitions
        WHERE id = archived_medication
          AND archived_at = '2026-02-01T00:00:00Z'
          AND active_ingredients = '{}'::text[]
    ) THEN
        RAISE EXCEPTION 'Archived medication state was not preserved';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM catalog.medication_definition_change_events
        WHERE id = '88888888-8888-4888-8888-888888888888'
          AND medication_definition_id = active_medication
          AND kind = 'Updated'
          AND previous_value = '{"name":"before"}'
          AND new_value = '{"name":"after"}'
    ) THEN
        RAISE EXCEPTION 'Medication audit history was not preserved';
    END IF;

    -- Migration 14 widened the audit snapshot columns from varchar(4000) to text in the
    -- same migration that added the caution notes. That pairing is the point: the
    -- snapshot is hand-serialised JSON of every field, and five prose notes push it well
    -- past 4000 characters, so columns without the widening fail the audit write exactly
    -- when the household finally records something worth auditing. Asserted here because
    -- a regenerated migration could drop the widening and nothing else would notice.
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'catalog'
          AND table_name = 'medication_definition_change_events'
          AND column_name IN ('previous_value', 'new_value')
          AND data_type <> 'text'
    ) THEN
        RAISE EXCEPTION 'Catalog audit snapshot columns are not text: %',
            (SELECT string_agg(column_name || ' ' || data_type, ', ')
             FROM information_schema.columns
             WHERE table_schema = 'catalog'
               AND table_name = 'medication_definition_change_events'
               AND column_name IN ('previous_value', 'new_value'));
    END IF;

    IF (
        SELECT count(*) FROM information_schema.columns
        WHERE table_schema = 'catalog'
          AND table_name = 'medication_definitions'
          AND column_name LIKE 'caution_%'
          AND data_type = 'text'
    ) <> 5 THEN
        RAISE EXCEPTION 'Expected five text caution columns on the catalog, found %',
            (SELECT count(*) FROM information_schema.columns
             WHERE table_schema = 'catalog'
               AND table_name = 'medication_definitions'
               AND column_name LIKE 'caution_%');
    END IF;

    -- Additive and nullable, so an upgrade must not invent a warning for a medicine
    -- whose household never wrote one. An empty string here would read on screen as a
    -- note somebody left blank on purpose.
    IF EXISTS (
        SELECT 1 FROM catalog.medication_definitions
        WHERE coalesce(caution_do_not_take_with, caution_foods_to_avoid, caution_things_to_do,
                       caution_things_to_avoid, caution_warning) IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'The upgrade invented caution notes on a pre-existing medication';
    END IF;

    -- -------------------------------------------------------------- treatments ----
    IF NOT EXISTS (
        SELECT 1 FROM treatments.plans
        WHERE id = plan AND person_id = person AND medication_definition_id = active_medication
    ) THEN
        RAISE EXCEPTION 'Treatment plan was not carried across the rename';
    END IF;

    -- Versions must be numbered by creation order, not arbitrarily.
    IF NOT EXISTS (
        SELECT 1 FROM treatments.plan_versions
        WHERE id = first_version
          AND plan_id = plan
          AND version_number = 1
          AND dose_numerator = 1 AND dose_denominator = 2
          AND kind = 'Scheduled'
          AND pattern = 'Daily'
          AND effective_from = '2026-01-01'
          AND local_time = '08:00:00'
          AND time_zone_id = 'Europe/Istanbul'
          AND day_period = 'Morning'
          AND meal_relation = 'AfterFood'
          AND minimum_interval_minutes = 240
          AND created_by_account_id = owner_account
    ) THEN
        RAISE EXCEPTION 'First plan version was not transformed correctly';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM treatments.plan_versions
        WHERE id = second_version
          AND version_number = 2
          AND pattern = 'SelectedWeekdays'
          AND weekday_mask = 5
          AND meal_relation = 'WithFood'
          AND day_period IS NULL
    ) THEN
        RAISE EXCEPTION 'Second plan version was not transformed correctly';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM treatments.plan_change_events
        WHERE id = '99999999-9999-4999-8999-999999999999'
          AND plan_id = plan
          AND kind = 'Updated'
    ) THEN
        RAISE EXCEPTION 'Plan audit history was not preserved';
    END IF;

    -- ---------------------------------------------------------------- packages ----
    IF (SELECT count(*) FROM inventory.packages) <> 2 THEN
        RAISE EXCEPTION 'Package rows were not preserved';
    END IF;

    -- Ownership and custody are now separate columns; both must carry the old value.
    IF NOT EXISTS (
        SELECT 1 FROM inventory.packages
        WHERE id = drawn_package
          AND owner_person_id = person
          AND holder_person_id = person
          AND nominal_capacity_numerator = 3
          AND nominal_capacity_denominator = 2
          AND medication_definition_id = active_medication
          AND unit = 'Tablet'
          AND created_by_account_id = owner_account
          -- Drawn from, so it must be open and carry an opened timestamp.
          AND state = 'Opened'
          AND opened_at IS NOT NULL
          AND ordinal = 1
          AND is_pinned = false
    ) THEN
        RAISE EXCEPTION 'Drawn-from package was not reshaped correctly';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM inventory.packages
        WHERE id = sealed_package
          AND owner_person_id IS NULL
          AND holder_person_id IS NULL
          -- Never drawn from, so it must still be sealed with no opened timestamp.
          AND state = 'Sealed'
          AND opened_at IS NULL
          AND ordinal = 2
    ) THEN
        RAISE EXCEPTION 'Untouched package was not left sealed';
    END IF;

    IF (SELECT count(DISTINCT ordinal) FROM inventory.packages
         WHERE medication_definition_id = active_medication) <> 2 THEN
        RAISE EXCEPTION 'Package ordinals are not unique per medication';
    END IF;

    -- ------------------------------------------------------------------ ledger ----
    IF (SELECT count(*) FROM inventory.ledger_entries) <> 5 THEN
        RAISE EXCEPTION 'Ledger entries were not preserved: %',
            (SELECT count(*) FROM inventory.ledger_entries);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM inventory.ledger_entries
        WHERE id = 'ffffffff-ffff-4fff-8fff-ffffffffffff'
          AND quantity_numerator = 3 AND quantity_denominator = 2
          AND entry_type = 'CountAdjustment'
          AND medication_definition_id = active_medication
          AND package_id IS NULL
          AND correlation_id = id
    ) THEN
        RAISE EXCEPTION 'Loose ledger entry was not preserved or typed';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM inventory.ledger_entries
        WHERE id = consume_entry AND entry_type = 'Consume' AND quantity_numerator = -1
    ) THEN
        RAISE EXCEPTION 'Consumption entry was not typed as a consumption';
    END IF;

    -- The zero-delta "counted and matched" row must survive the non-zero constraint.
    IF NOT EXISTS (
        SELECT 1 FROM inventory.ledger_entries
        WHERE id = 'f4f4f4f4-f4f4-4f4f-8f4f-f4f4f4f4f4f4'
          AND quantity_numerator = 0
          AND entry_type = 'CountAdjustment'
    ) THEN
        RAISE EXCEPTION 'Zero-delta count entry was rejected or retyped';
    END IF;

    IF EXISTS (SELECT 1 FROM inventory.ledger_entries WHERE entry_type = '') THEN
        RAISE EXCEPTION 'Some ledger entries were left without a typed entry kind';
    END IF;

    -- Total stock must be unchanged by a migration: 3/2 + 1 + 3/2 - 1 = 3.
    IF (SELECT sum(quantity_numerator::numeric / quantity_denominator)
          FROM inventory.ledger_entries
         WHERE medication_definition_id = active_medication) <> 3 THEN
        RAISE EXCEPTION 'Migrating changed the medication total: %',
            (SELECT sum(quantity_numerator::numeric / quantity_denominator)
               FROM inventory.ledger_entries
              WHERE medication_definition_id = active_medication);
    END IF;

    -- ----------------------------------------------------------- administrations --
    -- The columns EF would have scrambled. A taken dose must still point at its own
    -- medication and its own plan version.
    IF NOT EXISTS (
        SELECT 1 FROM administrations.administration_events
        WHERE id = taken_dose
          AND medication_definition_id = active_medication
          AND plan_version_id = second_version
          AND person_id = person
          AND outcome = 'Taken'
          AND stock_source = 'TrackedInventory'
          AND actual_quantity_numerator = 1 AND actual_quantity_denominator = 1
          AND planned_quantity_numerator = 1 AND planned_quantity_denominator = 1
          AND actor_account_id = owner_account
    ) THEN
        RAISE EXCEPTION 'Taken dose was not reshaped correctly';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM administrations.administration_events
        WHERE id = skipped_dose
          AND medication_definition_id = active_medication
          AND plan_version_id = first_version
          AND outcome = 'Skipped'
          AND stock_source = 'NotApplicable'
          AND actual_quantity_numerator IS NULL
          AND actual_quantity_denominator IS NULL
    ) THEN
        RAISE EXCEPTION 'Skipped dose was not reshaped correctly';
    END IF;

    -- ---------------------------------------------------------------- allocations --
    -- Historical consumption becomes a first-class allocation so existing history can
    -- answer "which package paid for this dose" and can be corrected.
    IF (SELECT count(*) FROM administrations.allocations) <> 1 THEN
        RAISE EXCEPTION 'Historical consumption was not promoted to an allocation: %',
            (SELECT count(*) FROM administrations.allocations);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM administrations.allocations
        WHERE administration_event_id = taken_dose
          AND package_id = drawn_package
          AND ledger_entry_id = consume_entry
          AND is_active
          -- A ledger entry is a signed delta; an allocation is a positive draw.
          AND quantity_numerator = 1 AND quantity_denominator = 1
    ) THEN
        RAISE EXCEPTION 'Promoted allocation does not match its ledger entry';
    END IF;

    -- The invariant the allocation model exists to guarantee.
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
        RAISE EXCEPTION 'A tracked dose''s allocations do not sum to the amount administered';
    END IF;

    -- --------------------------------------------------------------------- counts --
    IF NOT EXISTS (
        SELECT 1 FROM inventory.count_sessions
        WHERE id = '11111111-1111-4111-8111-111111111111'
          AND ledger_entry_id = 'ffffffff-ffff-4fff-8fff-ffffffffffff'
          AND observed_numerator = 3 AND observed_denominator = 2
          AND batch_id IS NULL
          AND package_id IS NULL
    ) THEN
        RAISE EXCEPTION 'Count session was not preserved across the column renames';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM inventory.count_sessions
        WHERE id = '12121212-1212-4121-8121-121212121212'
          AND batch_id = '77777777-7777-4777-8777-777777777777'
    ) THEN
        RAISE EXCEPTION 'Revisioned count session lost its batch';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM inventory.count_batches
        WHERE id = '77777777-7777-4777-8777-777777777777' AND revision_number = 1
    ) THEN
        RAISE EXCEPTION 'Count batch was not preserved';
    END IF;

    -- ---------------------------------------------------------------- new tables --
    -- Present and empty: the rebuild adds these surfaces without inventing history.
    IF (SELECT count(*) FROM administrations.allocation_corrections) <> 0 THEN
        RAISE EXCEPTION 'Allocation corrections were invented by the migration';
    END IF;

    IF (SELECT count(*) FROM refill.medication_refill_policies) <> 0 THEN
        RAISE EXCEPTION 'Refill policies were invented by the migration';
    END IF;

    -- The superseded tables must be gone, not left behind as a second source of truth.
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE (table_schema, table_name) IN (
            ('care', 'medications'),
            ('care', 'medication_change_events'),
            ('treatments', 'regimens'),
            ('treatments', 'regimen_versions'),
            ('treatments', 'regimen_change_events'))
    ) THEN
        RAISE EXCEPTION 'A superseded table was left in place alongside its replacement';
    END IF;
END $verify$;
