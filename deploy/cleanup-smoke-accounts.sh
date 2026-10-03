#!/usr/bin/env bash
#
# Runs deploy/cleanup-smoke-accounts.sql against production, behind a verified backup.
#
# This removes the accounts the smoke test registers — synthetic-smoke-<guid>@
# example.invalid — together with the households that have no other member. Nothing
# else is touched: the pattern cannot match a real address, because .invalid is
# reserved by RFC 2606 and can never be routed.
#
#   bash deploy/cleanup-smoke-accounts.sh --yes-remove-smoke-accounts
#
# Unlike the care-data purge, this takes no downtime. That one TRUNCATEs and needs an
# exclusive lock on every table; this one DELETEs a handful of rows and takes row locks,
# so api and web keep serving throughout.
set -euo pipefail

if [ "${1:-}" != "--yes-remove-smoke-accounts" ]; then
  cat >&2 <<'USAGE'
This removes the synthetic accounts left behind by deploy/smoke-test.ps1, and the
households whose only members are those accounts. A real person's account cannot match:
the pattern ends in .invalid, which RFC 2606 reserves so it can never belong to anyone.

A backup is taken and verified first, and the whole cleanup runs in one transaction
that refuses to commit if it would empty the system or strand an account outside a
household. Re-run with the confirmation flag if that is what you want:

  bash deploy/cleanup-smoke-accounts.sh --yes-remove-smoke-accounts
USAGE
  exit 2
fi

cd /opt/medication-tracker
test "$(pwd -P)" = /opt/medication-tracker
test -f compose.production.yml
test -f deploy/cleanup-smoke-accounts.sql
umask 077

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
backup=".deploy/backups/pre-cleanup-$(date -u +%Y%m%dT%H%M%SZ).dump"
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
docker cp "$database_container:/tmp/pre-cleanup.toc" .deploy/pre-cleanup-contents.txt
"${compose[@]}" exec -T database rm -f /tmp/pre-cleanup.dump /tmp/pre-cleanup.toc
test -s "$backup"
test -s .deploy/pre-cleanup-contents.txt
echo "Backup written to $backup and its contents verified readable."

cleanup_status=0
"${compose[@]}" exec -T database psql -v ON_ERROR_STOP=1 -U medication_tracker -d medication_tracker \
  < deploy/cleanup-smoke-accounts.sql || cleanup_status=$?

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

echo "Smoke-test accounts removed. The backup at $backup is the only way back."
