#!/usr/bin/env bash
#
# Runs an account-cleanup SQL file against production, behind a verified backup.
#
#   bash deploy/cleanup-accounts.sh --yes-remove-accounts deploy/<file>.sql
#
# Two files use it today:
#
#   cleanup-smoke-accounts.sql   removes the synthetic accounts smoke-test.ps1 leaves
#                                behind, matched by a pattern that cannot belong to a
#                                person. Reusable for as long as the smoke test exists.
#   keep-only-owner-account.sql  a one-off: reduces production to the owner's single
#                                account, identified by the digest of their address.
#
# Both are one transaction with the same guards, so the wrapper is the same for both:
# back up, verify the backup is readable, run the file, prove the site still serves,
# prove no other project's containers went down.
#
# Unlike the care-data purge, this takes no downtime. That one TRUNCATEs and needs an
# exclusive lock on every table; these DELETE rows and take row locks, so api and web
# keep serving throughout.
set -euo pipefail

sql="${2:-}"

if [ "${1:-}" != "--yes-remove-accounts" ] || [ -z "$sql" ]; then
  cat >&2 <<'USAGE'
This removes accounts from production. Which ones depends on the SQL file you name;
each file says at the top exactly what it matches and what it protects.

A backup is taken and verified first, and the whole cleanup runs in one transaction
that refuses to commit if it would empty the system or strand an account outside a
household. Re-run with the confirmation flag and a file:

  bash deploy/cleanup-accounts.sh --yes-remove-accounts deploy/cleanup-smoke-accounts.sql
  bash deploy/cleanup-accounts.sh --yes-remove-accounts deploy/keep-only-owner-account.sql
USAGE
  exit 2
fi

cd /opt/medication-tracker
test "$(pwd -P)" = /opt/medication-tracker
test -f compose.production.yml
test -f "$sql" || { echo "No such SQL file: $sql" >&2; exit 2; }
umask 077

label="$(basename "$sql" .sql)"

# Same promise the deploy and the purge make: this host runs other people's projects
# behind one nginx, and nothing here may disturb them.
outsiders() {
  docker ps --format '{{.Names}}' 2>/dev/null | grep -v '^medication-tracker-' | sort || true
}

outsiders_before="$(outsiders)"

compose=(docker compose --project-name medication-tracker --env-file .env.production -f compose.production.yml)

"${compose[@]}" up -d database
for attempt in $(seq 1 30); do
  if "${compose[@]}" exec -T database pg_isready -U medication_tracker -d medication_tracker; then break; fi
  sleep 2
done
"${compose[@]}" exec -T database pg_isready -U medication_tracker -d medication_tracker

mkdir -p .deploy/backups
backup=".deploy/backups/pre-$label-$(date -u +%Y%m%dT%H%M%SZ).dump"
database_container="$("${compose[@]}" ps -q database)"
test -n "$database_container"

# A backup nobody has read is not a rollback plan. Both the dump and its table of
# contents are produced inside the container against a real file, because pg_restore
# cannot read a custom-format archive from a pipe.
echo "Backing up before removing anything..."
"${compose[@]}" exec -T database sh -c '
  set -e
  pg_dump -U medication_tracker -d medication_tracker -Fc -f /tmp/pre-cleanup.dump
  pg_restore -l /tmp/pre-cleanup.dump > /tmp/pre-cleanup.toc
  test -s /tmp/pre-cleanup.toc
'
docker cp "$database_container:/tmp/pre-cleanup.dump" "$backup"
docker cp "$database_container:/tmp/pre-cleanup.toc" ".deploy/pre-$label-contents.txt"
"${compose[@]}" exec -T database rm -f /tmp/pre-cleanup.dump /tmp/pre-cleanup.toc
test -s "$backup"
test -s ".deploy/pre-$label-contents.txt"
echo "Backup written to $backup and its contents verified readable."

cleanup_status=0
"${compose[@]}" exec -T database psql -v ON_ERROR_STOP=1 -U medication_tracker -d medication_tracker \
  < "$sql" || cleanup_status=$?

if [ "$cleanup_status" -ne 0 ]; then
  echo "CLEANUP FAILED: the transaction rolled back and the accounts are unchanged." >&2
  echo "Nothing was stopped, so the site never went down. The backup is at $backup." >&2
  exit "$cleanup_status"
fi

# The site was never stopped, but prove it is still serving rather than assume it.
curl --fail --silent --show-error http://127.0.0.1:3022/ > /dev/null

outsiders_after="$(outsiders)"
missing="$(comm -23 <(printf '%s\n' "$outsiders_before") <(printf '%s\n' "$outsiders_after"))"

if [ -n "$missing" ]; then
  echo 'WARNING: containers belonging to other projects are no longer running.' >&2
  printf '%s\n' "$missing" >&2
  echo 'This cleanup touched only the database, so investigate the host.' >&2
  exit 1
fi

echo "$label complete. The backup at $backup is the only way back."
