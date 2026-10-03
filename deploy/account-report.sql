-- Describes the accounts in production without disclosing their addresses.
--
-- WHY THIS EXISTS
--
-- The owner-only cleanup refused: the digest of the address the owner gave matched no
-- account. That is the guard working, not a failure — but it leaves a question. Which
-- accounts are these, and which one is theirs?
--
-- Guessing would be the wrong answer to that question, so this reports instead. It is
-- read-only: it opens no transaction that changes anything and deletes nothing.
--
-- WHY THE ADDRESSES ARE MASKED
--
-- This runs in GitHub Actions against a public repository, where job logs are world
-- readable. A masked local part plus the domain is enough for the owner to recognise
-- their own account, and useless to anyone harvesting addresses.
--
-- The digest of each address is printed in full, because a digest discloses nothing by
-- itself. The owner can identify any account exactly by hashing a candidate:
--
--   printf 'CANDIDATE@EXAMPLE.COM' | tr '[:lower:]' '[:upper:]' | tr -d '\n' | sha256sum
--
-- Addresses are stored upper-cased (ToUpperInvariant), which is why the candidate is
-- upper-cased before hashing.

DO $$
BEGIN
  IF current_database() <> 'medication_tracker' THEN
    RAISE EXCEPTION
      'Refusing to report: expected the medication_tracker database, got %.',
      current_database();
  END IF;
END
$$;

\echo 'Accounts in production (addresses masked on purpose):'
\echo ''

SELECT
  -- First and last character of the local part, the rest starred, then the domain.
  -- Enough to recognise, not enough to write to.
  CASE
    WHEN length(split_part(a.normalized_email, '@', 1)) <= 2
      THEN left(split_part(a.normalized_email, '@', 1), 1) || '*'
    ELSE left(split_part(a.normalized_email, '@', 1), 1)
         || repeat('*', length(split_part(a.normalized_email, '@', 1)) - 2)
         || right(split_part(a.normalized_email, '@', 1), 1)
  END || '@' || split_part(a.normalized_email, '@', 2) AS masked_address,
  length(split_part(a.normalized_email, '@', 1)) AS local_len,
  a.created_at::date AS created,
  (SELECT count(*) FROM households.household_memberships m WHERE m.account_id = a.id) AS households,
  (SELECT count(*) FROM identity.sessions s WHERE s."AccountId" = a.id) AS sessions,
  encode(sha256(upper(a.normalized_email)::bytea), 'hex') AS digest
FROM identity.accounts a
ORDER BY a.created_at;

\echo ''
\echo 'Care data still attached to any household (all zero after the purge):'

SELECT
  (SELECT count(*) FROM care.people) AS people,
  (SELECT count(*) FROM catalog.medication_definitions) AS medications,
  (SELECT count(*) FROM inventory.packages) AS packages,
  (SELECT count(*) FROM treatments.plans) AS plans,
  (SELECT count(*) FROM administrations.administration_events) AS administrations;
