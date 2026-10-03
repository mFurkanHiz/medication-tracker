#!/usr/bin/env bash
# Writes the known_hosts file a deploy connects with, from reviewed sources only,
# and refuses to carry on without one.
#
# Pinning the server's host key is what makes `StrictHostKeyChecking=yes` mean
# something: without it, the first connection would accept whatever answered, and
# a hijacked name or route would be handed the deploy key. So this never falls
# back to trusting the network. When there is no pinned key it reads what the
# server currently offers, prints it for review, and fails — the key gets pinned
# by a human (or by an agent committing it) and the next deploy proceeds.
#
# Usage: authorise-ssh.sh <output path>
#
# Reads:
#   VPS_HOST           the address being connected to (required)
#   VPS_KNOWN_HOSTS    the secret, if it is set — takes precedence
#   PINNED_KNOWN_HOSTS the file to fall back to (default deploy/known_hosts)
set -euo pipefail

out="${1:?usage: authorise-ssh.sh <output path>}"
pinned="${PINNED_KNOWN_HOSTS:-deploy/known_hosts}"

test -n "${VPS_HOST:-}" || {
  echo 'VPS_HOST is not set, as a variable or a secret.' >&2
  exit 1
}

# A known_hosts line is "<host> <type> <key>". Accept the key on its own too, and
# accept a leading "***": when the address is stored as a secret GitHub masks it
# everywhere in the logs, so whoever copied the key out of a log had no host field
# to copy. The host we are about to connect to belongs in front of the key either
# way, and writing it ourselves is what makes those spellings equivalent.
normalise() {
  local line rest
  while IFS= read -r line; do
    line="${line%$'\r'}"
    case "$line" in
      '' | '#'*) continue ;;
      ssh-* | ecdsa-* | sk-*) printf '%s %s\n' "$VPS_HOST" "$line" ;;
      '***'[[:space:]]*)
        rest="${line#'***'}"
        printf '%s %s\n' "$VPS_HOST" "${rest# }"
        ;;
      *) printf '%s\n' "$line" ;;
    esac
  done
}

# Fails on an empty file or on anything that is not a host key, so a bad value is
# reported here by name rather than as ssh's own refusal three steps later.
usable() {
  test -s "$1" && ssh-keygen -lf "$1" >/dev/null 2>&1
}

if [ -n "${VPS_KNOWN_HOSTS:-}" ]; then
  printf '%s\n' "$VPS_KNOWN_HOSTS" | normalise > "$out"
  if usable "$out"; then
    chmod 600 "$out"
    echo "Host key: from the VPS_KNOWN_HOSTS secret."
    ssh-keygen -lf "$out"
    exit 0
  fi
  echo "The VPS_KNOWN_HOSTS secret does not hold a readable host key; trying $pinned." >&2
fi

if [ -f "$pinned" ]; then
  normalise < "$pinned" > "$out"
  if usable "$out"; then
    chmod 600 "$out"
    echo "Host key: pinned in $pinned, from this commit."
    ssh-keygen -lf "$out"
    exit 0
  fi
  echo "$pinned does not hold a readable host key." >&2
fi

rm -f "$out"

echo >&2
echo 'No pinned host key, so this deploy cannot prove which machine it is talking' >&2
echo 'to. Nothing was deployed. Reading what the server offers now, for review:' >&2
echo >&2

keys="$(ssh-keyscan -t ed25519 "$VPS_HOST" 2>/dev/null | grep -v '^#' || true)"
test -n "$keys" || keys="$(ssh-keyscan "$VPS_HOST" 2>/dev/null | grep -v '^#' || true)"

if [ -z "$keys" ]; then
  echo 'The server offered no host key at all. It may be unreachable from here.' >&2
  exit 1
fi

echo '================ HOST KEY BELOW ================'
printf '%s\n' "$keys"
echo '================ HOST KEY ABOVE ================'
echo
echo 'Fingerprint:'
printf '%s\n' "$keys" | ssh-keygen -lf -
echo
echo "Pin it: commit it as $pinned, or paste it into the VPS_KNOWN_HOSTS secret."
echo 'Compare the fingerprint against what your own SSH client shows before you do —'
echo 'that comparison is the whole point of pinning, and this log cannot make it for'
echo 'you. The address may read as *** here if VPS_HOST is stored as a secret; the'
echo 'key itself is intact and the deploy writes the address back in front of it.'
exit 1
