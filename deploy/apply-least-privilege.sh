#!/usr/bin/env bash
#
# Moves the live API off the cluster superuser — acceptance row 37, least privilege.
#
# compose.production.yml creates the database with POSTGRES_USER=medication_tracker, which
# the postgres image makes the cluster's bootstrap SUPERUSER, and until this ran the API
# connected as that same account. This script creates (or refreshes) the confined role
# that deploy/least-privilege-database-role.sql describes, gives it a password generated
# on this host, proves the role is confined, points the API at it through
# APP_DATABASE_USER / APP_DATABASE_PASSWORD in .env.production, recreates only the api
# container, and proves — before declaring success — that the API now connects as that
# role. If the API does not come back, the previous environment file is restored and the
# API is brought back on the old credential.
#
# The password is generated here and never printed. It exists in two places, both on this
# host: the database cluster and .env.production. The superuser's own password stays in
# .env.production for the database container and for the deployment's migration step,
# and is used by nothing that faces the network.
#
#   bash deploy/apply-least-privilege.sh --yes-apply-least-privilege
#
# Running it again rotates the credential: the role gets a new password and the API is
# recreated with it. That is the intended way to rotate, and why there is no "already
# applied" early exit.
set -euo pipefail

if [ "${1:-}" != "--yes-apply-least-privilege" ]; then
  cat >&2 <<'USAGE'
This changes the credential the live API authenticates to PostgreSQL with: it creates
the confined role medication_tracker_app with a password generated on this host, points
the API at it, recreates only the api container and verifies the result, restoring the
previous environment file if the API does not come back. Re-run with the confirmation
flag if that is what you want:

  bash deploy/apply-least-privilege.sh --yes-apply-least-privilege
USAGE
  exit 2
fi

root="${MEDICATION_TRACKER_ROOT:-/opt/medication-tracker}"
cd "$root"
test -f compose.production.yml
test -f deploy/least-privilege-database-role.sql
test -f .env.production
umask 077

# Same promise the deploy and the purge make: this host runs other people's projects
# behind one nginx, and nothing here may disturb them. Recorded before anything moves.
outsiders() {
  docker ps --format '{{.Names}}' 2>/dev/null | grep -v '^medication-tracker-' | sort || true
}
outsiders_before="$(outsiders)"

compose=(docker compose --project-name medication-tracker --env-file .env.production -f compose.production.yml)

# The compose file on this host must be one that reads APP_DATABASE_*; the workflow
# ships it from the same commit as this script, but an older file would make the
# recreate below a silent no-op and the proof at the end would fail for a confusing
# reason. So it is checked for, by name, first.
grep -q 'APP_DATABASE_USER' compose.production.yml || {
  echo 'compose.production.yml on this host does not read APP_DATABASE_USER; ship the current one first.' >&2
  exit 1
}

"${compose[@]}" up -d database
for attempt in $(seq 1 30); do
  if "${compose[@]}" exec -T database pg_isready -U medication_tracker -d medication_tracker; then break; fi
  sleep 2
done
"${compose[@]}" exec -T database pg_isready -U medication_tracker -d medication_tracker

# ------------------------------------------------------------------ the password ----
# Letters and digits only, so it sits inside a connection string unquoted; 48 of them
# from this host's own randomness. It is not echoed anywhere below. (A finite read of
# /dev/urandom and `cut` rather than `head` on the end: under `set -o pipefail` a
# producer that is still writing when `head` closes the pipe dies of SIGPIPE and the
# assignment fails.)
password="$(head -c 192 /dev/urandom | base64 | LC_ALL=C tr -dc 'A-Za-z0-9' | cut -c1-48)"
test "${#password}" -eq 48

# ---------------------------------------------------------------------- the role ----
# As the bootstrap superuser, inside the container, over its local socket. The SQL is
# idempotent: it creates the role only if missing and re-applies every grant.
echo 'Creating or refreshing the confined role...'
"${compose[@]}" exec -T database psql -v ON_ERROR_STOP=1 -q \
  -U medication_tracker -d medication_tracker \
  -v app_password="'$password'" < deploy/least-privilege-database-role.sql

# ------------------------------------------------ the role, before the API uses it ----
# Over TCP to the container's own address, so pg_hba's password rule applies: the
# image's loopback rule is "trust", and a trust connection proves nothing about the
# password the API is about to use.
as_app() {
  "${compose[@]}" exec -T -e PGPASSWORD="$password" database \
    sh -c 'psql -v ON_ERROR_STOP=1 -qtA -h "$(hostname -i | cut -d" " -f1)" -U medication_tracker_app -d medication_tracker "$@"' sh "$@"
}

echo 'Proving the role can read the household tables with its password...'
households="$(as_app -c 'select count(*) from households.households')"
test -n "$households"
echo "    households.households holds $households row(s), readable by the role."

echo 'Proving the role is not a superuser and cannot run programs on the host...'
test "$(as_app -c "select rolsuper from pg_roles where rolname = 'medication_tracker_app'")" = 'f'
if as_app -c "copy (select 1) to program 'true'" >/dev/null 2>&1; then
  echo 'FAILED: the role can still COPY ... TO PROGRAM, which is the power this exists to remove.' >&2
  exit 1
fi
if as_app -c 'select count(*) from pg_shadow' >/dev/null 2>&1; then
  echo 'FAILED: the role can read pg_shadow, which only a superuser should.' >&2
  exit 1
fi
echo '    rolsuper = f; COPY ... TO PROGRAM refused; pg_shadow refused.'

# ----------------------------------------------------------- the environment file ----
# The previous file is kept, readable only by root, so a failed restart below can put it
# back, and so a reader can see what changed. It holds the same superuser password the
# new file still holds; nothing is gained by shredding one copy of a value the other
# keeps.
mkdir -p .deploy/env-history
previous=".deploy/env-history/env.production.$(date -u +%Y%m%dT%H%M%SZ)"
cp .env.production "$previous"
chmod 600 "$previous"

next="$(mktemp .env.production.XXXXXX)"
grep -v -e '^APP_DATABASE_USER=' -e '^APP_DATABASE_PASSWORD=' .env.production > "$next" || true
{
  echo 'APP_DATABASE_USER=medication_tracker_app'
  echo "APP_DATABASE_PASSWORD=$password"
} >> "$next"
chmod 600 "$next"
mv "$next" .env.production

# -------------------------------------------------------------- only the api moves ----
# The database container is not touched (its credential did not change) and web is
# only ensured up, because the readiness probe runs from inside it, as in the deploy.
echo 'Recreating only this project'"'"'s api container with the confined credential...'
"${compose[@]}" up -d --no-deps --force-recreate api
"${compose[@]}" up -d --no-deps web

ready=no
for attempt in $(seq 1 30); do
  if "${compose[@]}" exec -T web wget --quiet --spider http://api:8080/health/ready; then ready=yes; break; fi
  sleep 2
done

if [ "$ready" != yes ]; then
  echo 'The API did not become ready with the confined credential. Restoring the previous environment file...' >&2
  install -m 600 "$previous" .env.production
  "${compose[@]}" up -d --no-deps --force-recreate api
  for attempt in $(seq 1 30); do
    if "${compose[@]}" exec -T web wget --quiet --spider http://api:8080/health/ready; then break; fi
    sleep 2
  done
  "${compose[@]}" exec -T web wget --quiet --spider http://api:8080/health/ready
  echo 'ROLLED BACK: the API is serving again with the previous credential. The role exists but nothing uses it.' >&2
  exit 1
fi

# ------------------------------------------------ what the API actually connects as ----
# /health/ready opened a connection through the API's own connection string a moment
# ago, and Npgsql keeps it pooled, so pg_stat_activity still shows it. psql names its
# own sessions and is excluded; anything else connected as the superuser is the API.
echo 'Reading who is connected to the database...'
connections="$("${compose[@]}" exec -T database psql -qtA -U medication_tracker -d medication_tracker -c \
  "select coalesce(usename::text, '?') || ' ' || count(*)::text from pg_stat_activity
    where datname = 'medication_tracker' and backend_type = 'client backend'
      and application_name <> 'psql'
    group by usename order by 1")"
printf '%s\n' "$connections" | sed 's/^/    /'

if printf '%s\n' "$connections" | grep -q '^medication_tracker '; then
  echo 'FAILED: something is still connected as the superuser after the api container was recreated with APP_DATABASE_*. Check compose.production.yml and .env.production on this host.' >&2
  exit 1
fi
printf '%s\n' "$connections" | grep -q '^medication_tracker_app ' || {
  echo 'FAILED: no connection as medication_tracker_app, although the readiness probe passed.' >&2
  exit 1
}

curl --fail --silent --show-error http://127.0.0.1:3022/ > /dev/null

outsiders_after="$(outsiders)"
missing="$(comm -23 <(printf '%s\n' "$outsiders_before") <(printf '%s\n' "$outsiders_after"))"
if [ -n "$missing" ]; then
  echo 'WARNING: containers belonging to other projects are no longer running.' >&2
  printf '%s\n' "$missing" >&2
  echo 'This change touched only the medication-tracker api container, so investigate the host.' >&2
  exit 1
fi

echo 'Least privilege applied: the API connects as medication_tracker_app, a role confined to'
echo "this database and not a superuser. The previous environment file is kept at $previous."
echo "${0##*/} left $(printf '%s\n' "$outsiders_after" | grep -c . || true) container(s) from other projects untouched."
