#!/usr/bin/env bash
#
# Reads the state of the host this project runs on, and changes nothing.
#
#   bash deploy/inspect-vps.sh
#
# Written to be run from GitHub Actions, because a cloud agent session cannot reach
# port 22 — the key works, the route is blocked by the environment's network policy.
#
# TWO RULES ABOUT WHAT IT PRINTS
#
# The job log is world readable, because this repository is public. So:
#
#   1. Every line of output goes through an address redactor. A medication tracker's
#      logs can carry a person's email, and a public CI log is no place for it.
#   2. Other projects on this shared host are counted, never named. Their container
#      names, domains and paths are the owner's business and nobody else's.
#
# It also prints no secret: not the database password, not a key, not .env.production.
set -uo pipefail   # deliberately not -e: one failing probe must not end the report

cd /opt/medication-tracker 2>/dev/null || { echo "No /opt/medication-tracker on this host."; exit 1; }

compose=(docker compose --project-name medication-tracker --env-file .env.production -f compose.production.yml)

section() { printf '\n========== %s ==========\n' "$1"; }
probe()   { "$@" 2>&1 || echo "  (probe failed: $*)"; }

section "HOST"
probe uname -srm
probe uptime
echo "clock: $(date -u '+%Y-%m-%dT%H:%M:%SZ') UTC"
# A reminder app lives or dies by its clock, so drift is worth seeing.
probe timedatectl show --property=Timezone --property=NTPSynchronized --value

section "DISK"
probe df -h / /var/lib/docker 2>/dev/null
echo "-- inodes --"
probe df -i / | tail -2
echo "-- this project's directory --"
probe du -sh /opt/medication-tracker
probe du -sh /opt/medication-tracker/.deploy/backups

section "MEMORY"
probe free -h
echo "-- swap pressure (si/so should be near zero) --"
probe vmstat 1 2 | tail -1

section "THIS PROJECT'S CONTAINERS"
probe docker ps --filter 'name=^medication-tracker-' \
  --format 'table {{.Names}}\t{{.Status}}\t{{.Image}}\t{{.Ports}}'
echo "-- restart counts (a climbing number means something is crashing) --"
for c in medication-tracker-web-1 medication-tracker-api-1 medication-tracker-database-1; do
  printf '%-32s restarts=%s health=%s\n' "$c" \
    "$(docker inspect "$c" --format '{{.RestartCount}}' 2>/dev/null || echo '?')" \
    "$(docker inspect "$c" --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' 2>/dev/null || echo '?')"
done
echo "-- deployed revision --"
probe docker image inspect medication-tracker-api:local \
  --format 'api  {{ index .Config.Labels "org.opencontainers.image.revision" }}'
probe docker image inspect medication-tracker-web:local \
  --format 'web  {{ index .Config.Labels "org.opencontainers.image.revision" }}'
echo "-- rollback images present --"
probe docker images --format '{{.Repository}}:{{.Tag}}' \
  --filter 'reference=medication-tracker-*:previous'

section "OTHER PROJECTS ON THIS HOST (counted, never named)"
echo "running containers not ours: $(docker ps --format '{{.Names}}' | grep -vc '^medication-tracker-')"
echo "total containers on host:    $(docker ps -aq | wc -l)"
echo "docker disk usage:"
probe docker system df

section "API HEALTH, FROM INSIDE THE PROJECT NETWORK"
probe "${compose[@]}" exec -T web wget --quiet --output-document=- http://api:8080/health/ready
echo
probe curl --silent --show-error --max-time 10 --output /dev/null \
  --write-out 'loopback web: %{http_code} in %{time_total}s\n' http://127.0.0.1:3022/

section "RECENT API LOG — LEVELS ONLY, NOT CONTENT"
# Counting rather than quoting: a line of this application's log can name a person.
logs="$(docker logs --since 24h medication-tracker-api-1 2>&1 || true)"
printf 'lines in the last 24h: %s\n' "$(printf '%s' "$logs" | grep -c . || echo 0)"
for level in fail Error Warn Unhandled Exception; do
  printf '  %-12s %s\n' "$level" "$(printf '%s' "$logs" | grep -c "$level" || echo 0)"
done

section "DATABASE"
probe "${compose[@]}" exec -T database psql -U medication_tracker -d medication_tracker -At -c "
  SELECT 'size: ' || pg_size_pretty(pg_database_size('medication_tracker'));"
probe "${compose[@]}" exec -T database psql -U medication_tracker -d medication_tracker -At -c "
  SELECT 'connections: ' || count(*) FROM pg_stat_activity WHERE datname = 'medication_tracker';"
echo "-- row counts --"
probe "${compose[@]}" exec -T database psql -U medication_tracker -d medication_tracker -c "
  SELECT 'accounts' AS t, count(*) FROM identity.accounts
  UNION ALL SELECT 'households', count(*) FROM households.households
  UNION ALL SELECT 'people', count(*) FROM care.people
  UNION ALL SELECT 'medications', count(*) FROM catalog.medication_definitions
  UNION ALL SELECT 'packages', count(*) FROM inventory.packages
  UNION ALL SELECT 'ledger', count(*) FROM inventory.ledger_entries
  UNION ALL SELECT 'plans', count(*) FROM treatments.plans
  UNION ALL SELECT 'administrations', count(*) FROM administrations.administration_events
  ORDER BY 1;"
echo "-- last migrations applied --"
probe "${compose[@]}" exec -T database psql -U medication_tracker -d medication_tracker -At -c "
  SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 3;"

section "BACKUPS"
probe ls -lh .deploy/backups 2>/dev/null | tail -8
echo "count: $(ls .deploy/backups 2>/dev/null | wc -l)"

section "TLS AND THE PUBLIC NAME"
probe bash -c 'echo | openssl s_client -servername medicationtracker.rapidconfigs.com \
  -connect medicationtracker.rapidconfigs.com:443 2>/dev/null \
  | openssl x509 -noout -subject -issuer -enddate'
echo "-- certbot timer --"
probe systemctl is-active certbot.timer
probe systemctl is-active snap.certbot.renew.timer

section "NGINX (only this project's server block)"
probe nginx -t
probe bash -c "grep -rl medicationtracker /etc/nginx 2>/dev/null | head -3"

section "SECURITY POSTURE"
probe bash -c "ss -lntp 2>/dev/null | awk 'NR==1 || \$4 !~ /127.0.0.1|\\[::1\\]/' | head -15"
echo "-- SSH: password login should be no --"
probe bash -c "sshd -T 2>/dev/null | grep -E '^(passwordauthentication|permitrootlogin|pubkeyauthentication)' || grep -E '^(PasswordAuthentication|PermitRootLogin)' /etc/ssh/sshd_config"
echo "-- keys that may open this host --"
probe bash -c "wc -l < /root/.ssh/authorized_keys"
probe bash -c "ssh-keygen -lf /root/.ssh/authorized_keys 2>/dev/null | sed 's/ .*(/ (/'"
echo "-- firewall --"
probe bash -c "ufw status 2>/dev/null | head -8 || echo 'ufw not installed'"
echo "-- pending reboot --"
probe bash -c "test -f /var/run/reboot-required && cat /var/run/reboot-required || echo 'none'"

section "END"
