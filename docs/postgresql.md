# PostgreSQL and Docker

Memory persistence is the default. To enable PostgreSQL, configure `Storage__Provider=Postgres` and `ConnectionStrings__Scada`.

For a local Docker environment:

```bash
export POSTGRES_PASSWORD="replace-with-strong-password"
export SCADA_VIEWER_KEY="replace-with-viewer-key"
export SCADA_OPERATOR_KEY="replace-with-operator-key"
export SCADA_ADMIN_KEY="replace-with-admin-key"
docker compose -f deploy/compose.yaml up --build -d
```

The dashboard is available at `http://127.0.0.1:5080/`. Host ports are bound to loopback.

The schema lives in `deploy/postgres/init.sql`. Docker runs it only when creating a fresh database volume. For existing databases, apply the SQL manually before starting the API. Startup verifies the `telemetry_samples`, `security_events` and `incidents` tables.

Telemetry, events and current incident status persist in PostgreSQL. The live plant state is still in memory. Database writes are synchronous; production use requires asynchronous buffering, migrations, retention and retry policies.
