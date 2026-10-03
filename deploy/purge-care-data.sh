#!/usr/bin/env bash
#
# Runs deploy/purge-care-data.sql against production, behind a verified backup.
#
# This erases every care record — people, medications, packages, the inventory ledger,
# plans, recorded doses, counts — and keeps the accounts, households and memberships
# that let somebody sign in. It is a deliberate one-off reset of data that describes
# the superseded product, not a feature, and it cannot be undone from inside the
# application. The only way back is the backup this script takes first.
#
#   bash deploy/purge-care-data.sh --yes-erase-care-data
#
set -euo pipefail

if [ "${1:-}" != "--yes-erase-care-data" ]; then
  cat >&2 <<'USAGE'
This erases every medication, person, package, plan, dose and count in production.
Accounts, households and memberships survive, so signing in still works.

A backup is taken and verified before anything is removed, and the whole purge runs in
one transaction. Re-run with the confirmation flag if that is what you want:

  bash deploy/purge-care-data.sh --yes-erase-care-data
USAGE
  exit 2
fi

cd /opt/medication-tracker
test "$(pwd -P)" = /opt/medication-tracker
test -f compose.production.yml
test -f deploy/purge-care-data.sql
umask 077

# Same promise the deploy makes: this host runs other people's projects behind one
# nginx, and nothing here may disturb them. Recorded before anything is touched.
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
backup=".deploy/backups/pre-purge-$(date -u +%Y%m%dT%H%M%SZ).dump"
database_container="$("${compose[@]}" ps -q database)"
test -n "$database_container"

# A backup nobody has read is not a rollback plan. The dump and its table of contents
# are both produced inside the container against a real file, because pg_restore cannot
# read a custom-format archive from a pipe — the same lesson the deploy learned.
echo "Backing up before erasing anything..."
"${compose[@]}" exec -T database sh -c '
  set -e
  pg_dump -U medication_tracker -d medication_tracker -Fc -f /tmp/pre-purge.dump
  pg_restore -l /tmp/pre-purge.dump > /tmp/pre-purge.toc
  test -s /tmp/pre-purge.toc
'
docker cp "$database_container:/tmp/pre-purge.dump" "$backup"
docker cp "$database_container:/tmp/pre-purge.toc" .deploy/pre-purge-contents.txt
"${compose[@]}" exec -T database rm -f /tmp/pre-purge.dump /tmp/pre-purge.toc
test -s "$backup"
test -s .deploy/pre-purge-contents.txt
echo "Backup written to $backup and its contents verified readable."

# TRUNCATE needs an exclusive lock on every table it touches. Stopping this project's
# api and web first means no in-flight request is holding a read lock against it, and
# no client sees the database change underneath a half-finished page. The database
# container stays up, and nothing belonging to another project is touched.
"${compose[@]}" stop api web

purge_status=0
"${compose[@]}" exec -T database psql -v ON_ERROR_STOP=1 -U medication_tracker -d medication_tracker \
  < deploy/purge-care-data.sql || purge_status=$?

"${compose[@]}" up -d api web

for attempt in $(seq 1 30); do
  if "${compose[@]}" exec -T web wget --quiet --spider http://api:8080/health/ready; then break; fi
  sleep 2
done
"${compose[@]}" exec -T web wget --quiet --spider http://api:8080/health/ready

if [ "$purge_status" -ne 0 ]; then
  echo "PURGE FAILED: the transaction rolled back and the data is unchanged." >&2
  echo "The site has been brought back up. The backup is at $backup." >&2
  exit "$purge_status"
fi

curl --fail --silent --show-error http://127.0.0.1:3022/ > /dev/null

outsiders_after="$(outsiders)"
missing="$(comm -23 <(printf '%s\n' "$outsiders_before") <(printf '%s\n' "$outsiders_after"))"

if [ -n "$missing" ]; then
  echo 'WARNING: containers belonging to other projects are no longer running.' >&2
  printf '%s\n' "$missing" >&2
  echo 'This purge touched only medication-tracker services, so investigate the host.' >&2
  exit 1
fi

echo "Care data purged. Accounts, households and memberships are intact, and the backup"
echo "at $backup is the only way back."
