# Architecture

The solution is a modular .NET 10 monolith. Each class, interface, record and enum has its own file. Modules are organized by responsibility, without adding generic repository or mediator layers.

| Module | Responsibility |
| --- | --- |
| Domain | Plant entities and immutable state models |
| Application | Services, abstractions, event and telemetry models |
| Infrastructure | In-memory and PostgreSQL persistence |
| Security | Authorization, identities, command monitoring and training scenarios |
| Protocols | Virtual registers and optional read-only Modbus TCP |
| Simulation | Background simulation and incident monitoring |
| Api | Composition root, endpoint groups, request contracts and static dashboard |
| Tests | Unit, integration and architecture tests |

The application uses the existing top-level namespaces to preserve compatibility. Folder names express architectural boundaries. The API composition root stays in `Program.cs`; HTTP routes are grouped in `Api/Endpoints`.

## Known limitations

Synchronous PostgreSQL writes may delay simulation. Plant state is not persisted. Static API keys are not a replacement for OIDC or TLS. OPC UA is not implemented. The optional Modbus TCP listener is loopback-only and read-only.

CI is intentionally disabled. Run `dotnet restore scada.slnx`, `dotnet build scada.slnx -c Release`, and `dotnet test scada.slnx -c Release` locally.
