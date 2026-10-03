-- Removes the accounts the smoke test registers, and nothing else.
--
-- WHY THIS EXISTS
--
-- deploy/smoke-test.ps1 registers one synthetic household on every run so it can
-- exercise the live site end to end. Those households accumulate. After the care-data
-- purge of 2026-10-03 the owner was left with twelve accounts, of which one is theirs
-- and the rest are leftovers from smoke runs.
--
-- The purge deliberately kept every account, because keeping the user is what was
-- asked for. Removing these is a narrower and different deletion, so it gets its own
-- script that can say exactly what it will touch.
--
-- WHAT IS MATCHED, AND WHY IT CANNOT MATCH A PERSON
--
-- The smoke test registers synthetic-smoke-<guid>@example.invalid. The `.invalid`
-- top-level domain is reserved by RFC 2606 precisely so that it can never be
-- registered or routed: no real person can hold an address there. So the pattern is
-- matched rather than an owner address being named — which also keeps the owner's
-- email out of this public repository.
--
-- Emails are stored normalised with ToUpperInvariant (see IdentityEndpoints), so the
-- comparison is made in upper case.
--
-- WHY NO EMAIL IS EVER PRINTED
--
-- This runs in GitHub Actions against a public repository, where job logs are world
-- readable. Counts are printed; addresses are not.
--
-- WHAT PROTECTS THE REST
--
-- Every foreign key from care data to a household is RESTRICT. If one of these
-- households still owns people, packages, plans or doses, the DELETE fails, names the
-- table, and the whole transaction rolls back. That is deliberate: it means this
-- script cannot quietly erase care data, and it stays safe to run in the future when
-- the care tables are no longer empty.

BEGIN;

-- The same credentials are used across this project's environments, so a mistyped
-- host must not be allowed to become a deletion somewhere else.
DO $$
BEGIN
  IF current_database() <> 'medication_tracker' THEN
    RAISE EXCEPTION
      'Refusing to clean up: expected the medication_tracker database, got %.',
      current_database();
  END IF;
END
$$;

CREATE TEMP TABLE doomed_accounts AS
SELECT id
FROM identity.accounts
WHERE upper(normalized_email) LIKE 'SYNTHETIC-SMOKE-%@EXAMPLE.INVALID';

-- A household goes only when every one of its members is going with it. A household
-- that has even one surviving member stays, and so does one with no members at all —
-- that would be a broken row, and a cleanup is the wrong place to guess about it.
CREATE TEMP TABLE doomed_households AS
SELECT h.id
FROM households.households h
WHERE EXISTS (
        SELECT 1 FROM households.household_memberships m WHERE m.household_id = h.id
      )
  AND NOT EXISTS (
        SELECT 1
        FROM households.household_memberships m
        WHERE m.household_id = h.id
          AND m.account_id NOT IN (SELECT id FROM doomed_accounts)
      );

CREATE TEMP TABLE cleanup_before AS
SELECT 'accounts' AS name, count(*) AS rows FROM identity.accounts
UNION ALL SELECT 'households', count(*) FROM households.households
UNION ALL SELECT 'memberships', count(*) FROM households.household_memberships
UNION ALL SELECT 'sessions', count(*) FROM identity.sessions;

\echo 'Matched for removal:'
SELECT
  (SELECT count(*) FROM doomed_accounts) AS accounts,
  (SELECT count(*) FROM doomed_households) AS households;

-- Entitlements before their subscriptions, subscriptions before what they belong to.
DELETE FROM subscriptions.entitlements e
USING subscriptions.subscriptions s
WHERE e.subscription_id = s.id
  AND (s.account_id IN (SELECT id FROM doomed_accounts)
       OR s.household_id IN (SELECT id FROM doomed_households));

DELETE FROM subscriptions.subscriptions
WHERE account_id IN (SELECT id FROM doomed_accounts)
   OR household_id IN (SELECT id FROM doomed_households);

-- Sessions would cascade with the account, but doing it here keeps the order readable
-- and the counts honest.
--
-- The column really is "AccountId", quoted. Every other table maps its columns to
-- snake_case, but the session entity is configured inline in MedicationTrackerDbContext
-- without column names, so EF took the property names verbatim and PostgreSQL folded
-- the unquoted identifier to lower case at creation time. Writing account_id here
-- fails with "column does not exist", which is how this was found.
DELETE FROM identity.sessions WHERE "AccountId" IN (SELECT id FROM doomed_accounts);

DELETE FROM households.household_memberships
WHERE account_id IN (SELECT id FROM doomed_accounts);

-- If a doomed household still owns care data, this is where the transaction fails and
-- names the table that still references it. That is the protection, not a bug.
DELETE FROM households.households WHERE id IN (SELECT id FROM doomed_households);

DELETE FROM identity.accounts WHERE id IN (SELECT id FROM doomed_accounts);

-- Nothing about this cleanup should be able to empty the system or to strand somebody
-- outside a household. Both are checked before the transaction is allowed to commit.
DO $$
DECLARE
  remaining bigint;
  stranded bigint;
BEGIN
  SELECT count(*) INTO remaining FROM identity.accounts;
  IF remaining = 0 THEN
    RAISE EXCEPTION 'Refusing to commit: no account would survive this cleanup.';
  END IF;

  SELECT count(*) INTO stranded
  FROM identity.accounts a
  WHERE NOT EXISTS (
    SELECT 1 FROM households.household_memberships m WHERE m.account_id = a.id
  );
  IF stranded > 0 THEN
    RAISE EXCEPTION
      'Refusing to commit: % account(s) would be left without a household.', stranded;
  END IF;
END
$$;

\echo ''
\echo 'Before and after:'
SELECT
  b.name,
  b.rows AS before,
  a.rows AS after,
  CASE WHEN a.rows = b.rows THEN 'unchanged' ELSE 'removed ' || (b.rows - a.rows) END AS result
FROM cleanup_before b
JOIN (
  SELECT 'accounts' AS name, count(*) AS rows FROM identity.accounts
  UNION ALL SELECT 'households', count(*) FROM households.households
  UNION ALL SELECT 'memberships', count(*) FROM households.household_memberships
  UNION ALL SELECT 'sessions', count(*) FROM identity.sessions
) a ON a.name = b.name
ORDER BY b.name;

COMMIT;
