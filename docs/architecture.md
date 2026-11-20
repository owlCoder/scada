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

The application retains existing top-level namespaces for the domain libraries. API endpoint namespaces follow their folders: `Api/Endpoints/Read`, `Api/Endpoints/Commands`, and `Api/Endpoints/System`. Dependency registration is split into `Api/Configuration/Registration` by responsibility, and the API composition root stays in `Program.cs`. Tests are grouped under `Tests/UnitTests` and `Tests/IntegrationTests` by functional area. Operations return meaningful results instead of explicit `void` methods; framework background operations continue to use `Task`.

`Tests/ArchitectureTests/Rules/SourceStructureTests.cs` enforces exactly one named type per C# file, type/file naming agreement, and no explicit `void` methods. All tests must pass in CI.

## Known limitations

Synchronous PostgreSQL writes may delay simulation. Plant state is not persisted. Static API keys are not a replacement for OIDC or TLS. OPC UA is not implemented. The optional Modbus TCP listener is loopback-only and read-only.

GitHub Actions CI runs restore, Release build, unit/integration/architecture tests, and Node.js frontend tests on relevant changes. The workflow cancels superseded runs and has a 15-minute job timeout. Run `bash scripts/verify.sh` locally for equivalent checks.
