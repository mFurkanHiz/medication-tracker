#!/usr/bin/env bash
set -euo pipefail
cd /opt/medication-tracker
test "$(pwd -P)" = /opt/medication-tracker
test -f compose.production.yml
test -f .deploy/medication-tracker-web.tar.gz
umask 077
if [ ! -f .env.production ]; then
  printf 'DATABASE_PASSWORD=%s\n' "$(openssl rand -hex 32)" > .env.production
fi
chmod 600 .env.production
# Pin whatever is running right now under a stable tag before the incoming archive
# takes over the :ci names. Without this the outgoing images become dangling and a
# rollback has to hunt for a sha256 in `docker images -a`.
compose_project=medication-tracker
for service in web api; do
  running="$(docker inspect "${compose_project}-${service}-1" --format '{{.Image}}' 2>/dev/null || true)"
  if [ -n "$running" ]; then
    docker tag "$running" "medication-tracker-${service}:previous"
  fi
done

docker load --input .deploy/medication-tracker-web.tar.gz
docker tag medication-tracker-web:ci medication-tracker-web:local
docker tag medication-tracker-api:ci medication-tracker-api:local
compose=(docker compose --project-name medication-tracker --env-file .env.production -f compose.production.yml)
"${compose[@]}" up -d database
for attempt in $(seq 1 30); do
  if "${compose[@]}" exec -T database pg_isready -U medication_tracker -d medication_tracker; then break; fi
  sleep 2
done
"${compose[@]}" exec -T database pg_isready -U medication_tracker -d medication_tracker
mkdir -p .deploy/backups
backup=".deploy/backups/pre-migration-$(date -u +%Y%m%dT%H%M%SZ).dump"
"${compose[@]}" exec -T database pg_dump -U medication_tracker -d medication_tracker -Fc > "$backup"
# A backup nobody has read is not a rollback plan. Listing its table of contents
# proves the archive is well-formed and non-empty before anything is migrated.
"${compose[@]}" exec -T database pg_restore -l /dev/stdin < "$backup" > .deploy/backup-contents.txt
test -s .deploy/backup-contents.txt
container=$(docker create medication-tracker-api:local)
docker cp "$container:/app/migrations.sql" .deploy/migrations.sql
docker rm "$container"
# Stop only this project's application containers while the schema changes. A
# migration that renames a table cannot run safely underneath the previous release
# still serving requests against the old names. The database container stays up, and
# no container belonging to another compose project is touched.
"${compose[@]}" stop api web
"${compose[@]}" exec -T database psql -v ON_ERROR_STOP=1 -U medication_tracker -d medication_tracker < .deploy/migrations.sql
"${compose[@]}" up -d api web
for attempt in $(seq 1 30); do
  if "${compose[@]}" exec -T web wget --quiet --spider http://api:8080/health/ready; then break; fi
  sleep 2
done
"${compose[@]}" exec -T web wget --quiet --spider http://api:8080/health/ready
"${compose[@]}" ps
curl --fail --silent --show-error http://127.0.0.1:3022/ > /dev/null
