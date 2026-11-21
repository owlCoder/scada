# Deployment

Run one active API/simulation instance per environment. The default assets are intended for an isolated training workstation. Run commands from the repository root with Docker Compose v2. [Configuration](configuration.md) lists application settings; this page describes the checked-in deployment assets.

## Image

`Dockerfile` restores/publishes `src/Api/Api.csproj` in the .NET 10 SDK image and runs `Api.dll` from `/app` in `mcr.microsoft.com/dotnet/aspnet:10.0`. It runs as the image's non-root `$APP_UID`, sets `ASPNETCORE_URLS=http://+:8080` and exposes 8080. The checked-in SDK/runtime tags are floating `10.0`; they are not digest pins. Build explicitly when updating the runtime:

```bash
docker build --pull -t scada-training:local .
```

Persist certificates outside the disposable container if enabling OPC UA. Its default `/app/.scada/pki` may not be writable by the image user; mount a writable directory and set `Protocols__OpcUa__PkiDirectory` to it. The default image/Compose assets do not configure that mount or expose protocol ports.

## PostgreSQL Compose stack

`deploy/compose.yaml` builds the API, starts `postgres:17.7-alpine`, waits for its health check and enables startup migrations. HTTP is at `http://127.0.0.1:5080/`. Generate local secrets as in [README](../README.md), then:

```bash
docker compose --env-file .scada/compose.env -f deploy/compose.yaml up --build -d
curl --fail http://127.0.0.1:5080/health/ready
docker compose --env-file .scada/compose.env -f deploy/compose.yaml logs --tail=100 api
```

| Service / resource | Binding / persistence |
| --- | --- |
| api | `127.0.0.1:5080` → container 8080, restart unless-stopped |
| postgres | `127.0.0.1:${POSTGRES_PORT:-5432}` → 5432, database/user `scada` |
| scada_pgdata | Named volume mounted at `/var/lib/postgresql/data`; Compose adds a project-name prefix |
| aspire-dashboard | Optional `observability` profile; loopback 18888; API exports to internal OTLP/gRPC 18889 |
| keycloak | Separate Compose file; loopback 8081 → 8080; read-only realm JSON import |

`down` retains the PostgreSQL volume; `down -v` destroys it. The legacy `deploy/postgres/init.sql` is a migration fixture/reference and is no longer mounted on startup. No API storage volume is needed for PostgreSQL data. Keycloak's current development service has no persistent database/volume: recreation can discard users and bootstrap changes.

| Compose variable | Default / requirement | Effect |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | Required | Database password and API connection string |
| `POSTGRES_PORT` | 5432 | PostgreSQL host port |
| `SCADA_VIEWER_KEY` | Required | Shared Viewer API key |
| `SCADA_OPERATOR_KEY` | Required | Shared Operator API key |
| `SCADA_ADMIN_KEY` | Required | Shared Administrator API key |
| `SCADA_OTLP_ENDPOINT` | Empty | Optional observability export |
| `SCADA_OIDC_ENABLED` | false | Enable API OIDC |
| `SCADA_OIDC_CLIENT_SECRET` | Empty in base file; required in Keycloak file | OIDC client secret |
| `KEYCLOAK_ADMIN_PASSWORD` | Required in Keycloak file | Bootstrap `admin` password |

Compose sets JSON logging, `Storage__Provider=Postgres`, migrations on and a connection to service `postgres:5432`. The host port does not change that internal port. Base Compose needs no Keycloak secrets when OIDC is disabled. Treat the env file as a secret and restrict permissions; Compose environment inspection can reveal credentials. No secret values are checked in.

Shared keys do not create browser accounts. For local browser sign-in, seed a named user via `Security:Users` in an override or use Keycloak. A temporary override can supply a named Administrator from locally defined environment variables (password at least 12 characters):

```yaml
services:
  api:
    environment:
      Security__Users__0__Username: ${SCADA_LOCAL_USERNAME:?Set SCADA_LOCAL_USERNAME}
      Security__Users__0__Role: Administrator
      Security__Users__0__Password: ${SCADA_LOCAL_PASSWORD:?Set SCADA_LOCAL_PASSWORD}
```

These two `SCADA_LOCAL_*` values are example override substitutions, not variables consumed by the checked-in Compose file. Save the override locally, append its `-f` after `deploy/compose.yaml`, and use the same files for subsequent commands. Existing accounts are not reseeded; rotate credentials through Accounts & rules/API.

## Optional Keycloak

Use `deploy/compose.keycloak.yaml` alongside the base file. It runs `quay.io/keycloak/keycloak:26.0.7` with `start-dev --import-realm`; it is a local development identity provider. The imported `scada` realm has no users and defines client `scada-api` and three mapped roles.

The bootstrap script specifically reads `.scada/dev-credentials.txt`; place the Compose variables and Keycloak secrets there (owner-only, gitignored), then:

```bash
set -a
. .scada/dev-credentials.txt
set +a
docker compose -f deploy/compose.yaml -f deploy/compose.keycloak.yaml up --build -d
bash scripts/keycloak-bootstrap.sh
```

Set `SCADA_OIDC_ENABLED=true`, `KEYCLOAK_ADMIN_PASSWORD` and `SCADA_OIDC_CLIENT_SECRET` before starting. The script creates `sso-viewer`, `sso-operator` and `sso-administrator`, assigns roles and appends random passwords to that credential file. Existing users are skipped. API discovery uses the internal `keycloak:8080` URL while the browser authority is `http://127.0.0.1:8081/realms/scada`; callback is `http://127.0.0.1:5080/signin-oidc`. Update provider/client URLs together if moving hosts. Local metadata HTTPS validation is explicitly disabled by Compose and produces a warning.

## Optional observability

```bash
SCADA_OTLP_ENDPOINT=http://aspire-dashboard:18889 \
  docker compose --env-file .scada/compose.env -f deploy/compose.yaml --profile observability up -d
```

Open `http://127.0.0.1:18888`; retrieve the dashboard login token from its service logs locally. It is diagnostic storage, not a backup. [Metrics and logs](observability.md)

## PostgreSQL backup and restore

Back up before upgrades. For a consistent simulator recovery point, stop API writes first; `pg_dump` itself gives a transactionally consistent database snapshot. Keep the same env/file options used at startup. The following uses the base stack and `.scada/compose.env`:

```bash
mkdir -p .scada/backups
docker compose --env-file .scada/compose.env -f deploy/compose.yaml stop api
(umask 077; docker compose --env-file .scada/compose.env -f deploy/compose.yaml exec -T postgres \
  pg_dump -U scada -d scada -Fc > .scada/backups/scada.dump)
docker compose --env-file .scada/compose.env -f deploy/compose.yaml start api
```

Restore deliberately replaces existing objects in the target database. Stop the API, restore the trusted dump and start again:

```bash
docker compose --env-file .scada/compose.env -f deploy/compose.yaml stop api
docker compose --env-file .scada/compose.env -f deploy/compose.yaml exec -T postgres \
  pg_restore -U scada -d scada --clean --if-exists --no-owner --exit-on-error < .scada/backups/scada.dump
docker compose --env-file .scada/compose.env -f deploy/compose.yaml start api
curl --fail http://127.0.0.1:5080/health/ready
```

Store dumps securely: they contain account credential hashes and training evidence. Also back up deployment secrets, custom topology and OPC UA PKI separately. Test restores in a separate environment. Rotating `POSTGRES_PASSWORD` in an env file does not change an existing volume's database role password; change the PostgreSQL role password and application connection configuration together.

## Upgrades and migrations

Stop the API, take a backup, build/pull the new image and recreate the service; retain the existing volume. Pending migrations run before accounts are seeded. Check readiness, logs, recovered plant state and historical runs/incidents. If startup migration is disabled with `Storage:ApplyMigrations=false`, apply a reviewed EF idempotent script before starting the new API. [Migration commands and legacy import](postgresql.md#migrations)

Avoid running two simulator versions against one database during an upgrade. Restore a matching backup if rolling back a schema-incompatible version; the legacy public tables do not capture new activity after migration.

## Compose variants and smoke test

- `deploy/compose.yaml`: API plus PostgreSQL (default). Copy `deploy/.env.example` to `deploy/.env` and set distinct secrets.
- `deploy/compose.inmemory.yaml`: API only with `Storage__Provider=InMemory`:
  `docker compose -f deploy/compose.inmemory.yaml --env-file deploy/.env up --build -d`.
- `deploy/compose.opcua.yaml` and `deploy/compose.modbus.yaml`: overrides that publish OPC UA (4840) or Modbus TCP on
  `127.0.0.1` only. Combine them with a base file and also set `SCADA_OPCUA_ENABLED=true` / `SCADA_MODBUS_ENABLED=true`.
  Inside the container the listeners bind `0.0.0.0` through `Protocols:OpcUa:BindAddress` and
  `Protocols:Modbus:BindAddress` (default `127.0.0.1`, IPv4 only), while the host port stays loopback.
- The API container runs as non-root with `read_only: true`, a `/tmp` tmpfs, `no-new-privileges`, `cap_drop: [ALL]`
  and a health check (`dotnet Api.dll --healthcheck`, which probes `/health/live`).
- `scripts/smoke.sh` builds the image, starts the stack with generated secrets, checks health, runs a scenario to
  Detected and tears everything down (`docker compose down -v`) on exit.
