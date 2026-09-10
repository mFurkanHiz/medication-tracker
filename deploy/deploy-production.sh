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
"${compose[@]}" exec -T database pg_dump -U medication_tracker -d medication_tracker -Fc > ".deploy/backups/pre-migration-$(date -u +%Y%m%dT%H%M%SZ).dump"
container=$(docker create medication-tracker-api:local)
docker cp "$container:/app/migrations.sql" .deploy/migrations.sql
docker rm "$container"
"${compose[@]}" exec -T database psql -v ON_ERROR_STOP=1 -U medication_tracker -d medication_tracker < .deploy/migrations.sql
"${compose[@]}" up -d api web
"${compose[@]}" ps
curl --fail --silent --show-error http://127.0.0.1:3022/ > /dev/null
