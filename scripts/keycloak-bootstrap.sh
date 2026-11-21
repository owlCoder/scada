#!/usr/bin/env bash
# Creates one Keycloak user per SCADA role in the "scada" realm of deploy/compose.keycloak.yaml.
# Generated passwords are appended to the gitignored .scada/dev-credentials.txt and never printed.
# Usage: scripts/keycloak-bootstrap.sh   (after: docker compose -f deploy/compose.yaml -f deploy/compose.keycloak.yaml up -d)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
credentials="$root/.scada/dev-credentials.txt"
compose=(docker compose -f "$root/deploy/compose.yaml" -f "$root/deploy/compose.keycloak.yaml")

if [[ ! -f "$credentials" ]]; then
  echo "Missing $credentials (see docs/postgresql.md)." >&2
  exit 1
fi
set -a; . "$credentials"; set +a
: "${KEYCLOAK_ADMIN_PASSWORD:?Set KEYCLOAK_ADMIN_PASSWORD in .scada/dev-credentials.txt}"

kcadm() { "${compose[@]}" exec -T keycloak /opt/keycloak/bin/kcadm.sh "$@"; }

for _ in $(seq 1 60); do
  kcadm config credentials --server http://localhost:8080 --realm master --user admin --password "$KEYCLOAK_ADMIN_PASSWORD" >/dev/null 2>&1 && break
  sleep 2
done

umask 077
for role in viewer operator administrator; do
  user="sso-$role"
  var="KEYCLOAK_$(echo "$role" | tr '[:lower:]' '[:upper:]')_PASSWORD"
  if kcadm get users -r scada -q "username=$user" --fields id | grep -q '"id"'; then
    echo "User $user already exists; skipped."
    continue
  fi
  password="$(openssl rand -base64 24 | tr -d '/+=' | cut -c1-24)"
  kcadm create users -r scada -s "username=$user" -s enabled=true -s emailVerified=true >/dev/null
  kcadm set-password -r scada --username "$user" --new-password "$password" >/dev/null
  kcadm add-roles -r scada --uusername "$user" --rolename "scada-$role" >/dev/null
  printf '%s=%s\n' "$var" "$password" >> "$credentials"
  echo "Created $user with role scada-$role; password saved as $var in .scada/dev-credentials.txt."
done
