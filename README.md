# SCADA — Industrial Control Simulation

A modular .NET 10 educational simulator for a water treatment plant, telemetry, process alarms, security events, incidents and local read-only Modbus TCP.

**Simulation only. Do not connect to physical industrial equipment.**

## Quick start

Install the .NET 10 SDK. Configure unique credentials:

```bash
export Security__ViewerApiKey="replace-with-random-viewer-key"
export Security__OperatorApiKey="replace-with-random-operator-key"
export Security__AdminApiKey="replace-with-random-admin-key"

dotnet restore scada.slnx
dotnet build scada.slnx --configuration Release
dotnet test scada.slnx --configuration Release
dotnet run --project src/Api --urls http://127.0.0.1:5080
```

Dashboard: http://127.0.0.1:5080/

**GitHub Actions CI is temporarily disabled** to conserve resources. Run `bash scripts/verify.sh` locally for restore, Release build, tests and JavaScript syntax validation. This script requires the .NET 10 SDK; it does not enable CI.

## Project layout

```text
src/
  Domain/
    Entities/
    Models/
  Application/
    Abstractions/
    Models/
    Services/
  Infrastructure/
    Persistence/
      InMemory/
      Postgres/
  Security/
    Models/
    Services/
  Protocols/
    Models/
    Services/
    Modbus/
  Simulation/
    HostedServices/
  Api/
    Contracts/
    Endpoints/
    wwwroot/
Tests/
  UnitTests/
  IntegrationTests/
  ArchitectureTests/
docs/
deploy/
```

One public class, interface, record or enum per source file. The existing short project names and top-level namespaces are retained; no `ScadaSecurity` prefix is used.

## API

| Method | Endpoint | Role |
| --- | --- | --- |
| GET | `/health` | Public |
| GET | `/api/me` | Viewer |
| GET | `/api/plant` | Viewer |
| GET | `/api/telemetry?count=100` | Viewer |
| GET | `/api/events` | Viewer |
| GET | `/api/incidents` | Viewer |
| GET | `/api/scenarios` | Viewer |
| GET | `/api/registers` | Viewer |
| GET | `/api/registers/{address}` | Viewer |
| POST | `/api/plant/tick` | Operator |
| POST | `/api/plant/pump/P-101/command` | Operator |
| POST | `/api/plant/valve/V-101/command` | Operator |
| POST | `/api/registers/{address}` | Operator |
| POST | `/api/incidents/{id}/status` | Administrator |
| POST | `/api/scenarios/{id}` | Administrator |

All protected endpoints require `X-Api-Key`. Named user credentials are supported; see [Security](docs/security.md).

## Documentation

- [Architecture](docs/architecture.md)
- [Security](docs/security.md)
- [PostgreSQL and Docker](docs/postgresql.md)
- [Incidents](docs/incidents.md)
- [Protocols](docs/protocols.md)
- [Security scenarios](docs/scenarios.md)

## Limitations

The simulator is not production SCADA software. There is no OPC UA implementation, OIDC authentication, TLS termination or production-ready telemetry queue. PostgreSQL persistence is optional; plant state remains in memory.
