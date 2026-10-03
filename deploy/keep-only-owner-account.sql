-- Reduces production to the owner's single account, and removes every other one.
--
-- WHY THIS EXISTS
--
-- The care-data purge of 2026-10-03 kept every account, because keeping the user is
-- what was asked for. Twelve accounts survived. Removing the smoke test's leftovers
-- took out exactly one, which showed that the other eleven are not smoke accounts at
-- all — they are earlier manual registrations. The owner wants to test from nothing,
-- with their own account and no other.
--
-- This is a one-off, not a feature. It is destructive and it is not reversible from
-- inside the application. Run it through deploy/cleanup-smoke-accounts.sh's sibling
-- path, which takes a verified backup first.
--
-- HOW THE OWNER IS IDENTIFIED, AND WHY NOT BY NAME
--
-- By the SHA-256 digest of their normalised (upper-cased) email, not by the address.
-- This repository is public: a digest keeps a personal address out of it, out of every
-- clone, and out of the world-readable job log. The owner can verify the digest
-- matches their own address at any time:
--
--   printf 'THEIR.ADDRESS@EXAMPLE.COM' | sha256sum
--
-- A wrong digest cannot cause damage. It would match no account, and the first guard
-- below stops the transaction before a single row is deleted.
--
-- WHAT PROTECTS THE REST
--
-- Every foreign key from care data to a household is RESTRICT, so a household that
-- still owns people, packages, plans or doses fails the DELETE, names the table, and
-- rolls the whole transaction back rather than orphaning anything.

BEGIN;

DO $$
BEGIN
  IF current_database() <> 'medication_tracker' THEN
    RAISE EXCEPTION
      'Refusing to clean up: expected the medication_tracker database, got %.',
      current_database();
  END IF;
END
$$;

CREATE TEMP TABLE kept_accounts AS
SELECT id
FROM identity.accounts
WHERE encode(sha256(upper(normalized_email)::bytea), 'hex')
      = 'e7f93617c4d7f102c749b83ede3e87b6b6a5c2fee74fc16fe089198c65917201';

-- Fail here rather than later. If the digest matches nothing, every account would be
-- doomed, and saying so plainly is more useful than letting the survive-check fire.
DO $$
DECLARE kept bigint;
BEGIN
  SELECT count(*) INTO kept FROM kept_accounts;
  IF kept = 0 THEN
    RAISE EXCEPTION
      'Refusing: the owner digest matched no account. Nothing was removed.';
  END IF;
  RAISE NOTICE 'Keeping % account(s).', kept;
END
$$;

CREATE TEMP TABLE doomed_accounts AS
SELECT id FROM identity.accounts WHERE id NOT IN (SELECT id FROM kept_accounts);

-- A household goes only when every one of its members goes with it, so one the owner
-- belongs to survives even if other members are being removed. A household with no
-- members at all is left alone: that is a broken row, and a cleanup is the wrong place
-- to guess about it.
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
          AND m.account_id IN (SELECT id FROM kept_accounts)
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

DELETE FROM subscriptions.entitlements e
USING subscriptions.subscriptions s
WHERE e.subscription_id = s.id
  AND (s.account_id IN (SELECT id FROM doomed_accounts)
       OR s.household_id IN (SELECT id FROM doomed_households));

DELETE FROM subscriptions.subscriptions
WHERE account_id IN (SELECT id FROM doomed_accounts)
   OR household_id IN (SELECT id FROM doomed_households);

-- The column really is "AccountId", quoted: the session entity is configured inline
-- without column names, so EF took the property name verbatim.
DELETE FROM identity.sessions WHERE "AccountId" IN (SELECT id FROM doomed_accounts);

DELETE FROM households.household_memberships
WHERE account_id IN (SELECT id FROM doomed_accounts);

DELETE FROM households.households WHERE id IN (SELECT id FROM doomed_households);

DELETE FROM identity.accounts WHERE id IN (SELECT id FROM doomed_accounts);

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
