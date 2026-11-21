# Observability

## Logs

Logging uses Serilog. Levels are set in the `Serilog` section of `appsettings.json`; framework noise is turned down to `Warning`.

- **Format:** human-readable text by default. Set `Observability__JsonLogs=true` for compact JSON, as `deploy/compose.yaml` does.
- **Requests:** each request is logged once, with method, path, status, duration and user. Health checks are logged only at `Verbose`.
- **What is never logged:** secrets (passwords, API keys, connection strings) and request headers.

## Correlation ids

Every request gets an `X-Correlation-Id`.

- **Where it comes from:** the caller's value if it matches `[A-Za-z0-9._-]{1,64}`, otherwise the trace id.
- **Where it shows up:**
  - echoed back in the response header
  - added to every log line for the request
  - set as a tag on the request trace
  - stored as `correlationId` on every event the request publishes, so `/api/events` links audit records to the HTTP call that caused them

## Traces and metrics

OpenTelemetry collects the following.

- **Traces:** ASP.NET Core requests (excluding health checks) and Npgsql database calls.
- **Metrics:** ASP.NET Core, the .NET runtime, Npgsql, and the SCADA meters below.

**`Scada` meter** (counted where the work happens, so Modbus and scenario commands are included):

| Instrument | Tags |
| --- | --- |
| `scada.simulation.steps` | none |
| `scada.commands` | `outcome`, `reason`, `source` |
| `scada.events` | `category` |
| `scada.detections` | `rule`, `severity` |
| `scada.incidents.created` | `category`, `severity` |
| `scada.scenario.runs` | `scenario`, `outcome` |

**`Scada.Runtime` meter** (read on demand):

| Instrument | Meaning |
| --- | --- |
| `scada.persistence.queue.pending` | Items waiting to be persisted |
| `scada.persistence.dropped` | Items dropped because the queue was full |
| `scada.persistence.failed` | Items that failed after all retries |
| `scada.simulation.time` | Simulated time, in seconds |
| `scada.simulation.paused` | 1 while the simulation is paused |

Export over OTLP/gRPC is enabled by setting `Observability__OtlpEndpoint`. To view the data locally in the Aspire dashboard:

```bash
SCADA_OTLP_ENDPOINT=http://aspire-dashboard:18889 docker compose -f deploy/compose.yaml --profile observability up -d
```

The dashboard is at `http://127.0.0.1:18888`. Its login token is printed in `docker compose logs aspire-dashboard`.

## Health

| Endpoint | Meaning |
| --- | --- |
| `/health/live` | The process is up |
| `/health/ready` | Storage is reachable and the write queue is not saturated; returns 503 otherwise and reports counters only |
| `/health` | Reports the storage provider |
