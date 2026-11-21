# PostgreSQL and Docker

By default all data is kept in memory. To use PostgreSQL, set `Storage__Provider=Postgres` and `ConnectionStrings__Scada`. Both modes implement the same store interfaces, so all behaviour is identical apart from durability.

## What is stored

Everything lives in the `scada` schema. EF Core manages it from `src/Infrastructure/Persistence/Postgres`.

| Table | Contents |
| --- | --- |
| `events` | Audit, process and security events |
| `telemetry` | Historian samples, including the hash chain |
| `alarm_history` | Alarm raise, clear and acknowledge transitions |
| `plant_state` | The newest simulation snapshot, as one jsonb row. It is restored on start. |
| `incidents`, `incident_timeline`, `incident_comments` | Incident workflow |
| `detections`, `detection_rule_settings` | Detection results and tuned rule settings |
| `scenario_runs` | Scenario runs, with their actions, measurements and outcome |
| `user_accounts` | Accounts, with role, hashed credentials and lockout state |

Notes on how the data is handled:

- **Incident numbers** (`INC-00042`) come from the `scada.incident_number_seq` sequence.
- **Concurrent edits:** each incident has a `version` column used as an optimistic concurrency token. A stale update is rejected even if several API instances share the database.
- **Write path:** high-volume writes (events, telemetry, alarm history, plant state) go through the bounded write-behind queue, so database latency never stalls the simulation loop.

## Migrations

Pending migrations are applied at startup, before accounts are seeded. To disable this, set `Storage__ApplyMigrations=false` and apply the migrations yourself.

The `dotnet-ef` tool is pinned in `.config/dotnet-tools.json`:

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/Infrastructure --output-dir Persistence/Postgres/Migrations --namespace Infrastructure.Persistence.Postgres.Migrations
```

```bash
dotnet ef migrations script --idempotent --project src/Infrastructure --startup-project src/Infrastructure
```

`Tests/DatabaseTests` fails if the model has changes that are not covered by a migration.

### Upgrading from the legacy schema

Older builds created `public.security_events`, `public.telemetry_samples` and `public.incidents` (see `deploy/postgres/init.sql`). The initial migration copies their rows into the new tables, and only for tables that exist:

- **Events** keep their category, message, actor and time.
- **Tank level** is imported as tag `LT-101`.
- **Pump state** is imported as tag `P-101.running`.
- **Incidents** keep their id, time, description and status (Open, Acknowledged or Resolved). Each also gets a `Created` timeline entry. Numbering continues after the imported incidents.

The legacy tables are never altered or dropped. They remain as a rollback path and can be removed by hand once the upgrade has been verified.

## Retention

A hosted `RetentionWorker` removes old events, telemetry, alarm history and detections. It covers both storage modes.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Retention__Enabled` | `true` | Turns the worker on or off |
| `Retention__Interval` | `01:00:00` | How often the worker runs |
| `Retention__DefaultMaxAge` | `30.00:00:00` | How long data is kept |
| `Retention__MaxAge__<target>` | none | Per-target override; targets are `events`, `telemetry`, `alarm-history` and `detections` |

## Health

`/health` reports the storage provider. `/health/ready` returns 503 when the database cannot be reached within 2 seconds or the write queue is saturated. It reports counters only, never error text.

## Docker

Put the secrets in a file that is not committed, for example the gitignored `.scada/dev-credentials.txt`:

```bash
POSTGRES_PASSWORD=...
SCADA_VIEWER_KEY=...
SCADA_OPERATOR_KEY=...
SCADA_ADMIN_KEY=...
POSTGRES_PORT=5432   # host port; change it if 5432 is taken
```

Then load the file and start the stack:

```bash
set -a; . .scada/dev-credentials.txt; set +a
```

```bash
docker compose -f deploy/compose.yaml up --build -d
```

The dashboard is at `http://127.0.0.1:5080/`. Host ports are bound to loopback only. The PostgreSQL image is pinned to `postgres:17.7-alpine`.

## Tests

`dotnet test Tests/DatabaseTests` starts PostgreSQL with Testcontainers, so it needs a working Docker engine. If Docker is unavailable the tests fail rather than skip.

The tests cover:

- migrations, and copying data from the legacy tables
- every PostgreSQL store, including concurrent incident updates and literal `%` and `_` in search
- retention
- a full API restart against the same database
