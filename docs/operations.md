# Operations runbook

Use the same Compose files and env file used to start the stack. Commands below assume the base PostgreSQL stack with `.scada/compose.env`; add the Keycloak file/profile if enabled.

## Check service health

```bash
curl --fail http://127.0.0.1:5080/health/live
curl --fail http://127.0.0.1:5080/health/ready
docker compose --env-file .scada/compose.env -f deploy/compose.yaml ps
docker compose --env-file .scada/compose.env -f deploy/compose.yaml logs --since=10m api postgres
```

`/health/live` reports process liveness, not simulation progress. `/health` JSON reports the selected provider, not database connectivity. `/health/ready` probes storage with a two-second deadline and returns 503 if unavailable or queue pending reaches 90% of capacity. It includes only counters, not error text. Send `Accept: application/json` from monitors: `Accept: text/html` selects the authenticated HMI, so an unauthenticated HTML request can return 401. In a browser `/health` is the authenticated HMI health page, with storage, simulation, queue and per-rule metrics. Pause is an expected state and does not itself fail readiness.

Watch simulated step count and time advance when running. Alert on growing queue pending, any increase in dropped/failed items, repeated observer/step errors or missing OTLP data. Queue drops and exhausted retries mean lost high-volume history, even after readiness recovers. [Instrument names](observability.md)

Request logs include method/path/status/duration/user and correlation ID. Use `X-Correlation-Id` to connect a request, traces and event `correlationId`; do not put secrets in correlation IDs, URLs, logs or support tickets. Inspect the persisted event history and recent buffer separately when diagnosing queue lag.

## Common failures

| Symptom | Check and fix |
| --- | --- |
| Startup says Postgres requires a connection string | Set Storage:Provider and ConnectionStrings:Scada together; check service hostname/internal port |
| Migration/startup DB failure | Confirm PostgreSQL health, password, schema permissions and available space. Back up, apply reviewed migrations and retry; do not bypass migrations against an incompatible schema |
| Readiness 503 / growing queue | Check DB reachability, disk/locks and persistence retry logs. Restore storage service; size queue/batch only after fixing throughput. Failed groups are discarded after retries |
| No process movement | Check simulation pause, RunLoop, controller state/links, Manual modes and interlocks; use manual step to distinguish paused clock from failed physics |
| 401 after rotation/disable/expiry | Sign in again or use the replacement key. Named-account state is read on every HTTP request; HMI password circuits revalidate every minute |
| 403 | Check role; training/network/scenario/account controls require Administrator. Register writes additionally require explicit write enable |
| 400 cookie API mutation | Fetch /account/antiforgery and send X-CSRF-TOKEN with the same cookies |
| 409 command/incident change | Switch actuator to Manual, address interlocks/replay, or reload incident version and choose an allowed transition; resolution requires notes |
| 429 | Wait for the rate window; check global/login/command budgets and authentication failures. Changing a client-supplied actor does not change identity |
| OPC UA cannot connect | Listener disabled, wrong port, loopback/container mismatch, certificate trust or local account credentials. Default anonymous and automatic trust are false. Trust both peers explicitly |
| OPC UA certificate startup failure | Check PKI path permissions and private-key access under the service user; on macOS inspect the user keychain in the launch context |
| Modbus write rejected | Both register WritesEnabled and Modbus AllowWrites are required; only HR100–199 and bounded values are allowed. Modbus has no per-user authentication |
| Browser/SignalR stale after disconnect | Reconnect/reload; external hub clients call GetSnapshot and REST history. Bounded feeds drop messages rather than replay them |
| Empty trends after restart | Expected: live historian is not rehydrated. Query /api/telemetry for durable history and wait for new samples |
| SSO fails / no role | Check authority vs metadata reachability, callback/client secret and mapped roles; identities without a mapped SCADA role cannot sign in |
| Port already in use | Change HTTP URL, protocol Port or Compose POSTGRES_PORT; host and internal database ports differ |
| New account config ignored | Seeding creates missing accounts only. Use account administration for existing records |

All referenced keys/defaults and environment forms are in [configuration](configuration.md). Changes to startup settings need a restart.

## Retention and capacity

Retention runs at startup and hourly by default, removing records older than 30 days from events, telemetry, alarm-history and detections in both storage modes. Per-target age overrides are supported. Incidents/timelines/comments, scenario runs, account records, rule settings and the latest plant checkpoint are not purged by this worker. Plan archival/cleanup separately. Recent buffers are bounded independently (5000 events, 20000 total live telemetry samples).

Memory mode loses stored state on process exit. PostgreSQL retains records, but queued high-volume work is not durable until written. A graceful stop saves final simulation state and attempts a five-second queue drain; abrupt termination can lose recent work. Watch shutdown warnings and verify [backups/restores](deployment.md#postgresql-backup-and-restore).

## Reset a training exercise

As Administrator, stop/reset the scenario from `/scenarios` (confirmation required) or POST `/api/scenarios/reset`. This aborts an active run, restores training defaults and clears current scenario state while keeping run/evidence history. A scenario pause freezes its schedule, not plant physics; use overview Pause to freeze the simulation too.

For a fresh world, reset the scenario first, then POST `/api/simulation/reset`. This recreates plant/controllers/network and clock from topology, preserving the paused flag, and queues a checkpoint. It does not erase incidents, events, runs or the live historian. Resume via overview or POST `/api/simulation/resume`. There is no reset button for the whole database; do not delete a shared volume to reset an exercise.

## Rotate credentials and certificates

Use Administrator Accounts & rules or POST the account password/API-key rotate routes. Copy the returned secret once to the authorized consumer, update that consumer and verify a request. Password rotation invalidates older password sessions; API-key rotation/revocation immediately replaces/removes that named key. Disable an account to prevent local access, or revoke its key when no replacement is needed. OIDC identities and sessions are managed at the provider; local account rotations do not rotate Keycloak credentials.

Legacy shared role keys are startup configuration, not local account entries. Generate a fresh unique key, update the corresponding deployment secret/configuration, recreate the API and update script clients. Database password changes require updating both PostgreSQL's role and the API connection secret. For Keycloak client-secret rotation update provider and API together.

OPC UA certificates live under the configured PKI directory. During a maintenance stop, back up the PKI, replace/generate the server certificate/private key using the OPC UA certificate workflow, distribute the new public certificate to clients and restart. Client trust is managed in trusted/certs and issuer/certs; do not solve trust failures by enabling global auto-accept. Restrict private-key directory access to the service user.
