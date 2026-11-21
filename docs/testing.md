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
| UnitTests | `dotnet test Tests/UnitTests -c Release --no-build` | Hydraulics, safety/alarms, controllers/network, commands/replay, all detection/scenario behavior, incident workflow, security, memory stores, queue/retention, realtime and protocol parsing/node values; fake TimeProvider where needed. Grid: engine physics/control/training/telemetry, redactor totals from reported measurements, IEC 104 point evaluation, and `GridRealEngineSecurityTests` driving the real `GridCoordinator` + detection + scenario engines deterministically (full simulated day = zero findings, all 8 catalog scenarios Detected with a sane network afterwards) |
| IntegrationTests | `dotnet test Tests/IntegrationTests -c Release --no-build` | HTTP endpoints, authentication/cookies/OIDC/proxy/CSRF/rate limits, paging/redaction, scenario-to-incident flow, DI, observability/correlation, SignalR, real TCP Modbus and OPC UA discovery/trust/subscriptions/methods; no PostgreSQL/Docker fixture. Grid: `/api/grid` REST incl. real-engine day/scenario runs in `GridSecurityApiTests` |
| FrontendTests | `dotnet test Tests/FrontendTests -c Release --no-build` | bUnit HMI rendering, role-gated controls, confirmation/cancel flows, redaction, charts/filters/paging, incident conflicts, account secrets/rule editing and realtime updates; no browser/Node required. Grid: all `/grid/*` pages incl. `/grid/scenarios` and `/grid/rules` (role gating, confirmations, calls), operator truth isolation, grid incidents on the shared incidents page |
| DatabaseTests | `dotnet test Tests/DatabaseTests -c Release --no-build` | Testcontainers PostgreSQL migrations/model drift, legacy import, stores/search, optimistic concurrency, retention, health and API restart/recovery; requires Docker. Grid: `GridEngineDefinitionStoreTests` |

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

## Current counts (grid integration branch, Release)

| Project | Tests | Result |
| --- | --- | --- |
| ArchitectureTests | 13 | pass |
| UnitTests | 453 | pass (incl. 11 real-engine grid security tests, 3 redactor-total tests, 2 IEC 104 evaluator tests) |
| IntegrationTests | 190 | pass (incl. real-engine full-day + 8 scenario runs through the grid REST API) |
| FrontendTests | 89 | pass (incl. 6 grid scenarios/rules page tests, 2 new grid pages) |
| ProtocolTests | 69 | pass (incl. 2 new IEC 104 operator-view wire tests) |
| PerformanceTests | 6 | pass |
| DatabaseTests | 32 | pass (Docker/Testcontainers) |
| EndToEndTests | 9 | pass with `PLAYWRIGHT_BROWSERS_PATH=$HOME/.cache/ms-playwright` and `SCADA_E2E_REQUIRED=1` (incl. the grid-mode browser test) |

## Step 7 suites and CI

| Project / asset | Command | Notes |
| --- | --- | --- |
| `Tests/ProtocolTests` | `dotnet test Tests/ProtocolTests -c Release --no-build` | Wire-level Modbus TCP (raw sockets), OPC UA client, SignalR hub and IEC 60870-5-104 slave tests (GI, spontaneous data on the operator view, SBO commands, k/w/t timers) |
| `Tests/PerformanceTests` | `dotnet test Tests/PerformanceTests -c Release --no-build` | Deterministic, CI-safe bounds (`Category=Performance`); measured numbers go to the test output |
| `Tests/EndToEndTests` | `dotnet test Tests/EndToEndTests -c Release --no-build` | Playwright (headless Chromium) against an in-process Kestrel host, water and grid mode (grid: sign-in mode choice, overview, diagram, confirmed SBO breaker operation with alarm, editor role gating); screenshots and traces go to `TestResults/EndToEndTests` on failure |
| `scripts/smoke.sh` | `scripts/smoke.sh` | Builds the image, starts compose with random secrets, waits for `/health/ready`, runs a scenario to Detected, removes everything. `SCADA_SMOKE_PROVIDER=InMemory`, `SCADA_OPCUA_ENABLED=true`, `SCADA_MODBUS_ENABLED=true` select variants |

Install the Playwright browser once after building:

```bash
PLAYWRIGHT_BROWSERS_PATH=$HOME/.cache/ms-playwright node Tests/EndToEndTests/bin/Release/net10.0/.playwright/package/cli.js install chromium
```

Without a browser the E2E tests are skipped with a message; `SCADA_E2E_REQUIRED=1` turns the skip into a failure (CI sets it).

`.github/workflows/ci.yml` runs on pushes to `main`, pull requests and manual dispatch, with the jobs `build-test`
(build, architecture, unit, frontend), `integration` (integration, protocol, performance), `database` (Testcontainers
PostgreSQL), `e2e` (Playwright Chromium), `format` (`dotnet format --verify-no-changes`), `docker` (image build plus
`scripts/smoke.sh`) and `dependencies` (`dotnet list package --vulnerable --include-transitive`). Test results (trx) are
uploaded when a job fails.

