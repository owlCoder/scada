#!/usr/bin/env bash
set -euo pipefail

# Requirements: Docker Compose v2+, openssl, curl, python3. Optional protocol publishing is opt-in.
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
for tool in docker openssl curl python3; do
    command -v "$tool" >/dev/null || { echo "Missing prerequisite: $tool" >&2; exit 1; }
done
docker info >/dev/null
docker compose version >/dev/null
umask 077
work=$(mktemp -d "${TMPDIR:-/tmp}/scada-smoke.XXXXXX")
project="scada-smoke-$(openssl rand -hex 6)"
image=${SCADA_IMAGE:-scada-api:smoke}
provider=${SCADA_SMOKE_PROVIDER:-Postgres}
opcua=${SCADA_OPCUA_ENABLED:-false}
modbus=${SCADA_MODBUS_ENABLED:-false}
case "$provider" in
    Postgres) base="$root/deploy/compose.yaml" ;;
    InMemory) base="$root/deploy/compose.inmemory.yaml" ;;
    *) echo 'SCADA_SMOKE_PROVIDER must be Postgres or InMemory' >&2; rm -rf -- "$work"; exit 1 ;;
esac
for enabled in "$opcua" "$modbus"; do
    case "$enabled" in
        true|false) ;;
        *) echo 'Protocol enable flags must be true or false' >&2; rm -rf -- "$work"; exit 1 ;;
    esac
done
compose=(docker compose --project-name "$project" --env-file "$work/stack.env" -f "$base")
if [[ "$opcua" == true ]]; then compose+=(-f "$root/deploy/compose.opcua.yaml"); fi
if [[ "$modbus" == true ]]; then compose+=(-f "$root/deploy/compose.modbus.yaml"); fi
cleanup() {
    status=$?
    trap - EXIT INT TERM
    if (( status != 0 )); then
        echo 'Smoke test failed; container diagnostics follow.' >&2
        "${compose[@]}" ps >&2 || true
        "${compose[@]}" logs --no-color --tail=80 >&2 || true
    fi
    if ! "${compose[@]}" down -v --remove-orphans; then status=1; fi
    rm -rf -- "$work"
    exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
admin_key=$(openssl rand -hex 32)
# Avoid inherited credentials overriding the freshly generated env file during Compose interpolation.
unset POSTGRES_PASSWORD SCADA_VIEWER_KEY SCADA_OPERATOR_KEY SCADA_ADMIN_KEY
{
    printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 32)"
    printf 'SCADA_VIEWER_KEY=%s\n' "$(openssl rand -hex 32)"
    printf 'SCADA_OPERATOR_KEY=%s\n' "$(openssl rand -hex 32)"
    printf 'SCADA_ADMIN_KEY=%s\n' "$admin_key"
    printf 'SCADA_IMAGE=%s\n' "$image"
    printf 'SCADA_HTTP_PORT=%s\n' "${SCADA_HTTP_PORT:-0}"
    printf 'POSTGRES_PORT=%s\n' "${POSTGRES_PORT:-0}"
    printf 'SCADA_OPCUA_ENABLED=%s\n' "$opcua"
    printf 'SCADA_OPCUA_PORT=%s\n' "${SCADA_OPCUA_PORT:-0}"
    printf 'SCADA_MODBUS_ENABLED=%s\n' "$modbus"
    printf 'SCADA_MODBUS_PORT=%s\n' "${SCADA_MODBUS_PORT:-0}"
    printf 'SCADA_OIDC_ENABLED=false\nSCADA_OTLP_ENDPOINT=\n'
} > "$work/stack.env"
# Keep secrets out of process arguments (including curl's command line).
printf 'header = "X-Api-Key: %s"\n' "$admin_key" > "$work/curl.conf"
unset admin_key
# Explicitly disable optional external services, even if the caller exported them.
export SCADA_OIDC_ENABLED=false SCADA_OTLP_ENDPOINT='' SCADA_IMAGE="$image"

docker build --tag "$image" "$root"
"${compose[@]}" up -d --no-build --wait --wait-timeout 180
http_binding=$("${compose[@]}" port api 8080)
url="http://127.0.0.1:${http_binding##*:}"
ready=false
for ((attempt = 0; attempt < 90; attempt++)); do
    if curl --fail --silent --max-time 3 "$url/health/ready" > "$work/ready.json"; then ready=true; break; fi
    sleep 1
done
[[ "$ready" == true ]] || { echo 'Readiness timed out' >&2; exit 1; }
curl --fail --silent --show-error --max-time 5 "$url/health/live" > /dev/null
curl --config "$work/curl.conf" --fail --silent --show-error --max-time 5 "$url/api/plant" > "$work/plant.json"
python3 -c 'import json,sys; assert isinstance(json.load(open(sys.argv[1])), dict)' "$work/plant.json"
curl --config "$work/curl.conf" --fail --silent --show-error --max-time 10 \
    --request POST "$url/api/scenarios/unauthorized-command/start" > "$work/start.json"
run_id=$(python3 -c 'import json,sys,uuid; d=json.load(open(sys.argv[1])); assert d["succeeded"]; print(uuid.UUID(d["run"]["runId"]))' "$work/start.json")
completed=false
for ((attempt = 0; attempt < 90; attempt++)); do
    curl --config "$work/curl.conf" --fail --silent --show-error --max-time 5 \
        "$url/api/scenarios/runs/$run_id" > "$work/run.json"
    state=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["state"])' "$work/run.json")
    case "$state" in
        Completed) completed=true; break ;;
        Failed|Stopped) echo "Scenario ended unexpectedly: $state" >&2; exit 1 ;;
    esac
    sleep 1
done
[[ "$completed" == true ]] || { echo 'Scenario timed out' >&2; exit 1; }
python3 - "$work/run.json" <<'PY'
import json
import sys
run = json.load(open(sys.argv[1]))
assert run["outcome"] == "Detected", f"Expected Detected, got {run['outcome']}"
assert set(run["expectedDetections"]) <= set(run["observedDetections"])
print(f"Scenario {run['scenarioId']}: Detected")
PY
if [[ "$modbus" == true ]]; then
    binding=$("${compose[@]}" port api 1502)
    python3 - "${binding##*:}" <<'PY'
import socket
import struct
import sys

def receive(connection, count):
    data = b""
    while len(data) < count:
        chunk = connection.recv(count - len(data))
        assert chunk, "Modbus connection closed early"
        data += chunk
    return data

with socket.create_connection(("127.0.0.1", int(sys.argv[1])), timeout=5) as connection:
    # Unit 1, FC03: read one holding register at address 0.
    connection.sendall(struct.pack(">HHHBBHH", 1, 0, 6, 1, 3, 0, 1))
    transaction, protocol, length, unit = struct.unpack(">HHHB", receive(connection, 7))
    assert (transaction, protocol, length, unit) == (1, 0, 5, 1)
    response = receive(connection, length - 1)
    assert response[:2] == bytes([3, 2]), f"Unexpected Modbus response: {response.hex()}"
print("Modbus TCP holding register read: passed")
PY
fi
# Confirm Docker's own liveness probe works under the read-only, non-root configuration.
container=$("${compose[@]}" ps -q api)
[[ $(docker inspect --format '{{.State.Health.Status}}' "$container") == healthy ]]
echo "Smoke test passed ($provider)."
