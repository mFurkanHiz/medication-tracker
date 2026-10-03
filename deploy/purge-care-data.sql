-- Removes every care record the household ever entered, and keeps the people who
-- sign in.
--
-- WHY THIS EXISTS
--
-- The pre-rebuild product modelled medication in a way the owner rejected: one
-- ambiguous Medication entity, a package that was really a capacity/balance pair, and
-- no record of which physical box paid for a dose. The rows migrated forward intact,
-- which was the right call at the time — losing a household's history to a refactor
-- would have been worse. But they describe a product that no longer exists, and
-- reading them alongside package-first data is actively confusing.
--
-- This is a deliberate, one-off reset of the care data, not a feature. It is destructive
-- and it is not reversible from inside the application. Run it through
-- deploy/purge-care-data.sh, which takes a verified backup first.
--
-- WHAT SURVIVES
--
--   identity.accounts              sign-in identity — the owner asked for this to stay
--   identity.sessions              so nobody is signed out by a data cleanup
--   households.households          an account with no household cannot use the app
--   households.household_memberships   the link between the two
--   subscriptions.subscriptions    entitlement, deliberately separate from care data
--   subscriptions.entitlements
--
-- WHAT GOES
--
-- Everything describing care: people, the medication catalog, physical packages, the
-- whole inventory ledger, treatment plans and their versions, recorded doses and the
-- allocations and corrections behind them, counts, refill settings, and the sync
-- receipts that would otherwise replay a deleted command.
--
-- WHY ONE TRUNCATE, AND WHY NOT CASCADE
--
-- Every foreign key between these tables is RESTRICT, and two of them point at their
-- own table (an allocation names the allocation it supersedes; a count batch names the
-- batch it revises). Deleting row by row would have to unpick that order by hand.
-- TRUNCATE over the whole set does it in one step, and because the set is complete,
-- no CASCADE is needed.
--
-- CASCADE is omitted on purpose. If a future table references one of these and is not
-- listed here, this statement fails and names it, instead of quietly erasing a table
-- nobody decided to erase.

BEGIN;

-- A purge that ran against the wrong database would be a disaster, and the password is
-- the same across this project's environments.
DO $$
BEGIN
  IF current_database() <> 'medication_tracker' THEN
    RAISE EXCEPTION
      'Refusing to purge: expected the medication_tracker database, got %.',
      current_database();
  END IF;
END
$$;

CREATE TEMP TABLE purge_before AS
SELECT 'accounts' AS name, count(*) AS rows FROM identity.accounts
UNION ALL SELECT 'households', count(*) FROM households.households
UNION ALL SELECT 'memberships', count(*) FROM households.household_memberships
UNION ALL SELECT 'people', count(*) FROM care.people
UNION ALL SELECT 'medications', count(*) FROM catalog.medication_definitions
UNION ALL SELECT 'packages', count(*) FROM inventory.packages
UNION ALL SELECT 'ledger entries', count(*) FROM inventory.ledger_entries
UNION ALL SELECT 'plans', count(*) FROM treatments.plans
UNION ALL SELECT 'administrations', count(*) FROM administrations.administration_events;

TRUNCATE TABLE
  -- Recorded doses and the stock that paid for them.
  administrations.allocation_corrections,
  administrations.allocations,
  administrations.administration_events,

  -- Physical inventory, its ledger and its counts.
  inventory.count_sessions,
  inventory.count_batches,
  inventory.package_loans,
  inventory.package_assignment_events,
  inventory.ledger_entries,
  inventory.packages,
  inventory.inventory_items,

  -- Treatment plans and their effective-dated versions.
  treatments.plan_change_events,
  treatments.plan_versions,
  treatments.plans,

  -- The medication catalog and its audit trail.
  catalog.medication_definition_change_events,
  catalog.medication_definitions,

  -- Refill settings hang off a medication that is about to stop existing.
  refill.medication_refill_policies,

  -- The people the medication was for.
  care.people,

  -- Idempotency receipts. Left behind, a phone replaying an offline command would be
  -- told its dose was already recorded, against a dose that no longer exists.
  sync.processed_administration_commands,
  sync.command_receipts;

-- Report what changed, so the operator sees the result rather than trusting it.
SELECT
  b.name AS "table",
  b.rows AS "before",
  a.rows AS "after",
  CASE WHEN a.rows = b.rows THEN 'kept' ELSE 'purged' END AS outcome
FROM purge_before b
JOIN (
  SELECT 'accounts' AS name, count(*) AS rows FROM identity.accounts
  UNION ALL SELECT 'households', count(*) FROM households.households
  UNION ALL SELECT 'memberships', count(*) FROM households.household_memberships
  UNION ALL SELECT 'people', count(*) FROM care.people
  UNION ALL SELECT 'medications', count(*) FROM catalog.medication_definitions
  UNION ALL SELECT 'packages', count(*) FROM inventory.packages
  UNION ALL SELECT 'ledger entries', count(*) FROM inventory.ledger_entries
  UNION ALL SELECT 'plans', count(*) FROM treatments.plans
  UNION ALL SELECT 'administrations', count(*) FROM administrations.administration_events
) a ON a.name = b.name
ORDER BY b.name;

-- Sign-in must still work afterwards. An account whose household went missing would
-- reach the app and find nothing it can use, which is a worse outcome than the stale
-- data this is cleaning up.
DO $$
DECLARE
  stranded integer;
BEGIN
  SELECT count(*) INTO stranded
  FROM identity.accounts a
  WHERE NOT EXISTS (
    SELECT 1 FROM households.household_memberships m WHERE m.account_id = a.id
  );

  IF stranded > 0 THEN
    RAISE EXCEPTION 'Refusing to commit: % account(s) would be left with no household.', stranded;
  END IF;
END
$$;

COMMIT;
