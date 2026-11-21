# Testing

Install the .NET 10 SDK. All existing projects are xUnit projects in `scada.slnx`, inheriting nullable and warnings-as-errors from `Directory.Build.props`. Run from repository root:

```bash
dotnet restore scada.slnx
dotnet build scada.slnx -c Release --disable-build-servers
```

## Existing test projects

| Project | Command | Coverage / requirements |
| --- | --- | --- |
| ArchitectureTests | `dotnet test Tests/ArchitectureTests -c Release --no-build` | Dependency boundaries, one type/file, naming/namespaces and no-void rules including Razor; no Docker |
| UnitTests | `dotnet test Tests/UnitTests -c Release --no-build` | Hydraulics, safety/alarms, controllers/network, commands/replay, all detection/scenario behavior, incident workflow, security, memory stores, queue/retention, realtime and protocol parsing/node values; fake TimeProvider where needed |
| IntegrationTests | `dotnet test Tests/IntegrationTests -c Release --no-build` | HTTP endpoints, authentication/cookies/OIDC/proxy/CSRF/rate limits, paging/redaction, scenario-to-incident flow, DI, observability/correlation, SignalR, real TCP Modbus and OPC UA discovery/trust/subscriptions/methods; no PostgreSQL/Docker fixture |
| FrontendTests | `dotnet test Tests/FrontendTests -c Release --no-build` | bUnit HMI rendering, role-gated controls, confirmation/cancel flows, redaction, charts/filters/paging, incident conflicts, account secrets/rule editing and realtime updates; no browser/Node required |
| DatabaseTests | `dotnet test Tests/DatabaseTests -c Release --no-build` | Testcontainers PostgreSQL migrations/model drift, legacy import, stores/search, optimistic concurrency, retention, health and API restart/recovery; requires Docker |

`docker info` must succeed before DatabaseTests. The fixture starts `postgres:17.7-alpine`, uses isolated test databases and fails loudly if Docker/image startup fails; it does not silently skip. Allow image downloads, ephemeral TCP ports and enough disk. Protocol integration tests bind local ports and create temporary PKI stores; OPC UA also needs usable platform cryptography/key storage. On macOS a launch context without a usable keychain can fail certificate creation independently of application assertions.

Run all existing suites with:

```bash
dotnet test scada.slnx -c Release --no-build
```

`bash scripts/verify.sh` prints the SDK version, restores, builds Release and runs the entire solution. Because this includes DatabaseTests, it requires Docker. There are no current Node dashboard tests; the old dashboard was replaced by Blazor/bUnit.

For a focused rerun:

```bash
dotnet test Tests/IntegrationTests -c Release --no-build --filter FullyQualifiedName~Realtime
```

Use `--logger 'trx;LogFileName=results.trx'` when collecting machine-readable results. Do not store credential files, PKI private keys or secret-bearing logs in test artifacts.

## CI and parallel Step 7 additions

The current `.github/workflows/ci.yml` contains one `verify` job: restore, Release build and solution tests on Ubuntu with .NET 10.x. It runs on relevant pushes to main or workflow_dispatch, cancels superseded runs and has a 15-minute timeout.

The following are planned **(added in Step 7)** by parallel branches and are absent here. Their final assertions, dependencies, thresholds and invocation details must be reconciled after merge; these are intended commands, not evidence of a completed run:

| Planned project / asset | Intended use |
| --- | --- |
| `Tests/ProtocolTests` | Dedicated protocol coverage; build solution after merge, then `dotnet test Tests/ProtocolTests -c Release --no-build` |
| `Tests/PerformanceTests` | Performance coverage; `dotnet test Tests/PerformanceTests -c Release --no-build`; confirm thresholds/environment in merged source |
| `Tests/EndToEndTests` | Playwright browser workflows; `dotnet test Tests/EndToEndTests -c Release --no-build`; confirm browser installation/host setup in merged source |
| `SCADA_E2E_REQUIRED` | Planned Playwright environment gate; exact accepted values and missing-browser/host behavior are not yet verified |
| `scripts/smoke.sh` | Deployment smoke check; inspect merged script for arguments, Docker requirements and cleanup behavior |
| CI jobs | `build-test`, `integration`, `database`, `e2e`, `format`, `docker`, `dependencies`; replace the current single-job description after merge |

Do not assume Playwright browsers are installed just because NuGet restore succeeded. The lead should document the merged project's browser installation command and explicit required-mode value, then run it in CI. [Deployment additions](deployment.md#parallel-step-7-additions)
