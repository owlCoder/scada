# Session Handoff: Full SCADA Platform Build-out

Read this first in a new session. It records exactly where the previous session stopped.
The original task is the "MASTER IMPLEMENTATION PROMPT — COMPLETE SCADA PROJECT" (paste it again in the new session together with this file).

## 1. Environment facts (verified)

| Item | Value |
|---|---|
| Repository | `~/Desktop/scada` (cloned from `https://github.com/owlCoder/scada`) |
| Branch | `feature/full-scada-platform` (created from `main` @ `e95fb31`), first commit made after Step 2, **nothing pushed** |
| Git identity (repo-local) | `user.name=owlCoder`, `user.email=danijel.xda@gmail.com`. Verified: GitHub resolves commits with this email to login `owlCoder`. |
| Commit rule | **Never** add `Co-Authored-By` or any AI trailer (user's global CLAUDE.md overrides the default attribution). |
| .NET | SDK 10.0.100 installed. Use NuGet **10.0.x** Microsoft packages (11.x targets net11). |
| Package versions found | EF Core 10.0.15, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.4, Npgsql 10.0.4, Serilog.AspNetCore 10.0.0, OpenTelemetry.* 1.21.0, SignalR.Client 10.0.15, Authentication.OpenIdConnect 10.0.15, Mvc.Testing 10.0.15, dotnet-ef 10.0.15, Testcontainers.PostgreSql 4.17.0, bunit 2.15.2, Microsoft.Playwright 1.65.1, OPCFoundation.NetStandard.Opc.Ua.Server/Client 2.0.2 (major API change vs 1.5.x; consider latest 1.5.x if 2.0 API is a problem) |
| Docker | 29.9.3 available |
| Node | v24.19.0 |
| gh | logged in as `owlCoder` (scopes: repo, workflow, gist, read:org) |
| Mistral Vibe | `vibe` 3.1.3 at `~/.local/bin/vibe`. Strongest model: **Mistral Large 4** (`mistral-large-4`, reasoning low/medium/high). Headless call works: `vibe -p "<prompt>" --model mistral-large-4 --thinking high --max-turns 4 --output text` (run from the repo dir; use `--workdir`). |
| Agreed split | Claude implements; Mistral Large 4 (high) runs headless **code reviews** after each phase. |

Baseline before changes: Release build OK, **110 .NET tests** (4 architecture, 63 unit, 43 integration) + 3 Node frontend tests = 113 passing.

## 2. Current state of the working tree

**Builds without errors (Release):** `src/Domain`, `src/Application`, `src/Security`.

**Does NOT build yet (expected, still on the old API):** `src/Infrastructure`, `src/Protocols`, `src/Simulation`, `src/Api`, and all `Tests/*`. These still reference removed types: old `Domain.Plant`/`PlantState`, `Application.IPlantService`, the old `SecurityEvent(Timestamp, Category, Message, Actor)`, `Application.Incident`/`IncidentStatus`, `Security.AccessRole`, `Security.AuthorizationService`, `Security.ScenarioService`, `Security.CommandMonitor(IEventStore)`, and `Protocols.VirtualRegisterService`.

Code conventions used everywhere in the new code (keep them):
- One type per file, file name = type name, **namespaces match folders** (e.g. `Domain.Process.Devices`).
- No `void` methods: return `bool`, `int`, a snapshot or a result record.
- `System.Threading.Lock` for locks, `TimeProvider` injected for time, nullable enabled, warnings as errors.
- Units: volume m³, flow m³/min, pressure bar, level % of capacity. Simulation step is 0.5 simulated seconds; time scale is 5 simulated seconds per wall second.

### 2.1 Domain (done)
- `Process/`
  - `Plant` aggregate: `Step`, `CommandPump`/`CommandValve`/`SetMode`, `InjectFault`/`ClearFault`, `SpoofSensor`, `SetTankLevel`, `SetInterlockBypass`, `ResetInterlock`, `ApplyFailSafe`, `AcknowledgeAlarm`, `CaptureSnapshot`, `static Restore`.
  - Devices: `Devices/` (Tank, Pump, Valve, Sensor plus enums), `Configuration/` (definitions, validator, `DefaultPlantDefinition`), `Hydraulics/HydraulicModel`, `Safety/SafetyInterlockEvaluator` (overfill, dry-run, deadhead, pressure trip; pump interlocks latch until reset), `Alarms/` (AlarmBook, AlarmConditionEvaluator: level/pressure alarms come from reported sensor values, overflow from the true value), `Snapshots/`, `Commands/` (ChangeCause, CommandOrigin, CommandResult, DeviceChange, FaultInjection).
  - Default plant: tanks T-101/T-102/T-103, pumps P-101 (duty) / P-103 (standby) T-101→T-102 and P-102 T-102→T-103, valves V-101 inlet, V-102/V-103/V-201 discharge, V-301 outlet, V-302 drain. Sensors: LT-101..103, FT-101, FT-102, FT-103, FT-104, FT-301, FT-302, PT-101, PT-102, PT-103.
  - Mode rule: operator commands need Manual mode, controller commands need Automatic mode. `Unattributed` (scenario tampering) bypasses mode and interlocks.
- `Simulation/SimulationClock`.
- `Control/`: `VirtualController` (scan accumulation, field read over network, latched SafeState after `TimeoutThreshold` failures, register bank with forces, status registers), `VirtualPlc` (hysteresis pump loops with duty/standby, valve loops; setpoints live in holding registers 100+), `VirtualRtu`, `ControllerRegisterMap` (outputs HR0+, setpoints HR100+, status HR200+, loop demand HR210+), `DefaultControllerDefinitions` (PLC-1, PLC-2, RTU-1).
- `Network/`: `VirtualNetwork`, `CommunicationLink` (latency, loss, timeout, metrics), zones and conduits (Purdue model), `SeededRandomSource`, `DefaultNetworkDefinition` (links L-SCADA-PLC1/PLC2/RTU1, L-PLC1-IO, L-PLC2-IO, L-RTU1-IO; segments ENT, DMZ, SUP, CTL, WAN, FLD-1..3).
- `Incidents/`: `Incident` (number, severity, priority, category, assignment, evidence, version), `IncidentStateMachine` (Open→Acknowledged→InProgress→Resolved→Closed; Acknowledged→Resolved; Resolved→Open), `IncidentWorkflow` (status/assign/priority/evidence; resolution notes required), comments, timeline.

### 2.2 Application (done)
- `Identity/`: AccessRole (moved here from Security), Actor, UserAccount, IUserAccountStore.
- `Events/`: new `SecurityEvent(Id, Timestamp, Category, Severity, Source, Message, Actor, DeviceId, ScenarioRunId, CorrelationId)`, `EventCategories`, `RecentEventBuffer`, `EventPublisher` (writes to the buffer, the persistence queue and realtime), `IEventStore` (async, paged `EventQuery`).
- `Common/`: PagedResult, Paging.
- `Realtime/`: IRealtimePublisher, RealtimeTopics, RealtimeMessage.
- `Persistence/`: `IPersistenceQueue` (write-behind, non-blocking, drops and counts when full), work items (SecurityEvent, TelemetryBatch, AlarmHistory, SimulationState), `IRetentionTarget`.
- `Telemetry/`: `TelemetryHistorian` (hash-chained, `Verify`, `Tamper` training hook), `ITelemetryStore`, TelemetrySample/Query/Point, IntegrityReport.
- `Alarms/`: AlarmService (acknowledge plus history), IAlarmHistoryStore, AlarmHistoryRecord.
- `Simulation/`:
  - `SimulationEngine` (lock; `Step(force)`; `Pause`/`Resume`/`Reset`; `CaptureState`/`Restore`; `Read`/`Mutate`; `CheckCommandPath` over supervisory and field links), `SimulationWorld`, `PlantFieldBus`.
  - `SimulationCoordinator` (`StepAsync`/`StepManyAsync` fan out to events, historian, persistence, observers, realtime), `StepEventTranslator`, `ISimulationObserver`.
  - SimulationOptions, SimulationTopology (validates), reports, state snapshots, `IPlantStateStore`.
- `Commands/`: `ControlCommandService` (role check → rate limit → replay guard → comm path → execute → audit), OperatorCommand, OperatorCommandType, CommandSource, ICommandRateLimiter, CommandReplayGuard.
- `Incidents/`: IncidentService (`RaiseAsync` with correlation-key merge, status/assign/priority/comments, optimistic versioning), IIncidentStore, IncidentQuery, IncidentQueryEvaluator (in-memory filter/sort/page), IncidentDraft, IncidentDetails.
- `Detection/`: DetectionRecord, DetectionSeverity, DetectionEvidence, DetectionQuery, IDetectionStore, DetectionRuleSettings, IDetectionRuleSettingsStore.
- `Scenarios/`: ScenarioRunRecord, ScenarioRunState, ScenarioOutcome, ScenarioActionLog, IScenarioRunStore.
- `Training/`: ITrainingEnvironment and TrainingEnvironment (spoof, faults, force actuator, bypass, tank level, link state and impairment, register forces, controller start/stop/reset, blocked connection attempts that emit a Network event, historian tamper, `RestoreDefaults`).
- `Registers/`: `RegisterAccessService` (unit id = 1-based controller index; reads of input/holding registers; setpoint writes only to HR100–199, only when `RegisterWriteOptions.WritesEnabled`, Operator role required; invalid or denied operations are audited as Protocol/AuthorizationDenied events).

### 2.3 Security (partially done, builds)
- `Authentication/`: ApiKeyValidator, AccessPolicy, UserAccessPolicy (legacy, moved), `ApiKeyAuthenticator` (renamed from AuthorizationService), AuthenticatedUser, UserCredential, `PasswordHasher` (PBKDF2-SHA256, 210k iterations, plus password and API key generation).
- `Commands/`: CommandMonitor (implements ICommandRateLimiter, uses TimeProvider and CommandRateOptions).
- `Accounts/`: UserAdministrationService (change role, enable, rotate password or API key, revoke key; denied attempts audited with the text "requires Administrator").
- `Detection/`: DetectionEngine (ISimulationObserver: rules → dedup → store → Detection event → `IncidentService.RaiseAsync`; metrics; `UpdateSettings`), DetectionContext, DetectionFinding, DetectionDeduplicator, DetectionCorrelator (`scenario:{runId}` or `{category}:{device or rule}`), IActiveScenarioProvider, RuleExecutionMetrics, RuleDescriptor.
- `Detection/Rules/` (17 rules, `DefaultDetectionRules.Create(topology, historian)`):
  - abnormal-tank-level, mass-balance-inconsistency, impossible-measurement-change
  - unexpected-pump-activity, unexpected-valve-change
  - repeated-rejected-commands, excessive-control-requests, invalid-register-operations
  - communication-failure, controller-state-mismatch, conflicting-operator-commands
  - unauthorized-access, command-replay, network-segmentation-violation
  - historian-integrity, process-safety-violation, operator-misuse

## 3. Remaining work (in order)

### Step 1: Scenario engine (Security/Scenarios), DONE (builds in Release, not yet covered by tests)
What was built (2026-11-20):
- `Security/Scenarios/`: `IScenarioAction`, `ScenarioActionResult`, `ScheduledScenarioAction`, `ScenarioDefinition` (with `Validate()` and optional `Cleanup` actions run on completion/stop/reset), `ScenarioServices` (the service bundle for actions; register it in DI), `ScenarioExecutionContext` (`NextSequence`, `Capture`/`Recall` for replay), `ScenarioRun` (internal mutable run, schedule sorted by time), `ScenarioMeasurements`, `ScenarioOutcomeEvaluator` (pure, unit-testable), `ScenarioControlResult`, `ScenarioDescriptor`, `ScenarioEngine`.
- `Security/Scenarios/Actions/`: the 17 actions below plus `RestoreAutomaticModeAction` (cleanup; only acts if the device is in Manual, so it adds no audit noise), `ScenarioActionResults` (internal helper), enums `ControllerOperation`, `AccountAttemptKind`.
- `Security/Scenarios/Catalog/DefaultScenarios.Create()`: the 14 scenarios. Verified with a throwaway program: all 14 validate, ids unique, every expected detection id exists among the 17 rules.
- Engine decisions: all control operations (start/pause/resume/stop/reset/replay) require the Operator role (denials audited as AuthorizationDenied). One `SemaphoreSlim` gate serialises steps and control calls; `ActiveRunId`/`Current` are read under a separate `Lock`. Run records are saved on start, pause/resume and finish; realtime topic `scenarios` is published on actions, every 10 steps and on finish. `ResetAsync` aborts any active run, restores training defaults and clears `Current`.
- Rate limit note (10 commands/min per actor): `command-frequency` deliberately exceeds it (the final scripted SetAutomatic is rejected), so cleanup restores V-301 to Automatic.
- DI wiring for Step 2: register `ScenarioEngine` once as singleton and expose the same instance as `ISimulationObserver` and `IActiveScenarioProvider`; register it **before** `DetectionEngine` in the observer list so actions are evaluated in the same step. Register `DefaultScenarios.Create()` as `IEnumerable<ScenarioDefinition>`.
- Still to do in Step 2: a test per scenario proving its expected detections fire (event-rule windows are wall clock, so tests should use a fake TimeProvider), and tests for the engine lifecycle (pause stops elapsed time, stop gives Aborted, replay sets `ReplayOf`).

Original design (kept for reference):
- `ScenarioDefinition(Id, Name, Category, Description, DurationSeconds (simulated), ExpectedDetections, Actions: ScheduledScenarioAction(AtSeconds, IScenarioAction))`.
- `IScenarioAction.ExecuteAsync(ScenarioExecutionContext) → ScenarioActionResult`. The context holds ITrainingEnvironment, ControlCommandService, RegisterAccessService, AlarmService, UserAdministrationService, RunId, and a `NextSequence(actor)` helper based on `CommandReplayGuard.LastSequence`.
- One action class per file: SpoofSensor, InjectFault, ForceActuator, LinkState, LinkImpairment, ForceRegister, RegisterWrite, OperatorCommand, ReplayCommand, CommandBurst, AlarmAcknowledgeBurst, InterlockBypass, SetTankLevel, ConnectionAttempt, HistorianTamper, ControllerControl, RoleChangeAttempt.
- `ScenarioEngine : ISimulationObserver, IActiveScenarioProvider`:
  - One active run at a time. Elapsed time advances only while the run is Running.
  - Supports Start, Pause, Resume, Stop, Reset and `Replay(runId)`.
  - On completion: query `IDetectionStore` by `ScenarioRunId`; the outcome is Detected, PartiallyDetected, Missed or Aborted. Then call `RestoreDefaults()` and persist through `IScenarioRunStore`.
  - Generic measurements: max/min level per tank, overflow volume, alarms raised, interlock trips, safety violations, unattributed changes.
- The 14 scenarios (simulated seconds → action; expected rules):
  1. `unauthorized-command`: viewer `guest-viewer` sends StartPump P-101 and a V-301 position at t=2/6/10/14. Expected: repeated-rejected-commands, unauthorized-access. Duration 30.
  2. `sensor-spoofing`: t=2 spoof LT-102 = 30. Expected: mass-balance-inconsistency. Duration 120.
  3. `register-tampering`: t=1 ConnectionAttempt ENT→PLC-1 (blocked). t=2/4/6 invalid writes as anonymous `modbus:192.0.2.66` (HR300, HR5, HR250). t=10 ForceRegister PLC-1 HR100=900 and HR101=980. Expected: network-segmentation-violation, invalid-register-operations, controller-state-mismatch. Duration 60.
  4. `command-replay`: operator-ana sets P-101 manual, start, stop with fresh sequence numbers, then replays the captured start sequence 4×; t=20 SetAutomatic. Expected: command-replay. Duration 30.
  5. `denial-of-service`: L-SCADA-PLC1 latency 400 ms / loss 70 %, L-PLC1-IO loss 80 %, then a 15-command burst from operator-role `flood-client`. Expected: communication-failure, excessive-control-requests. Duration 60.
  6. `command-frequency`: operator-ana sets V-301 manual and oscillates it 20/80 % every 2 s for 20 s, then SetAutomatic. Expected: excessive-control-requests, conflicting-operator-commands.
  7. `role-escalation`: operator-bob makes 3× ChangeRole plus a RotateApiKey attempt. Expected: unauthorized-access.
  8. `plc-communication-interruption`: t=2 disconnect L-PLC1-IO; t=40 reconnect; t=45 reset PLC-1. Expected: communication-failure, controller-state-mismatch. Duration 60.
  9. `false-measurement-injection`: spoof FT-101 = 0 (V-101 open) and PT-101 = 8 bar. Expected: impossible-measurement-change, mass-balance-inconsistency. Duration 90.
  10. `controller-state-mismatch`: ForceRegister PLC-2 HR0 = 1 (P-102 is stopped) and PLC-1 HR2 = 0. Expected: controller-state-mismatch. Duration 40.
  11. `unexpected-actuator-activation`: ForceActuator V-302 = 100 and P-103 = 1; t=35 V-302 = 0. Expected: unexpected-valve-change, unexpected-pump-activity.
  12. `process-safety-violation`: SetTankLevel T-102 = 88, bypass interlocks, force P-101 and P-103 on. Expected: process-safety-violation, abnormal-tank-level. Duration 90.
  13. `data-integrity-violation`: t=5 TamperHistorian LT-103; t=15 FT-301. Expected: historian-integrity. Duration 30.
  14. `operator-misuse`: operator-carl sets T-101 to 90 and T-103 to 10 to create alarms, switches 4 devices to manual, acknowledges all alarms repeatedly, and restores Automatic at t=55. Expected: operator-misuse. Duration 60.
- Note: detection windows for event rules are wall-clock based (documented limitation). Process rules use simulated time.

### Step 2: Make the whole solution build and get to green, then the FIRST COMMIT, DONE (2026-11-21)
What was done:
- Infrastructure: in-memory stores for every interface (`Infrastructure/Persistence/InMemory`), `ChannelPersistenceQueue` + `PersistenceDispatcher` + `PersistenceWorker` (`Infrastructure/Persistence/Queue`; batches, 3 attempts with linear backoff, then counted as failed, drains on shutdown), `NullRealtimePublisher` until SignalR (Step 6).
- Simulation project: `SimulationRecoveryService` (restore on start, save on stop), `SimulationService` loop, `DetectionSettingsRecoveryService`, `InitialScenarioService`, `PlantConfigurationLoader` (JSON topology, round-trip tested) and `SimulationHostOptions` (`Simulation:Host:TopologyFile|InitialScenario|RunLoop|RestoreOnStart`).
- Protocols: `ModbusRequestProcessor` (strict MBAP, FC03/FC04, FC06/FC16 only when `Registers:WritesEnabled`, all-or-nothing FC16, exceptions 01/02/03/04 plus 0x0B for an unknown unit, malformed frames audited and the connection closed) and `ModbusTcpServer` (loopback only, connection limit, idle timeout). Config `Protocols:Modbus:*`; the legacy keys `Protocols:ModbusEnabled|ModbusPort` still work. `VirtualRegisterService` was deleted.
- Api: full endpoint set (see `src/Api/Endpoints/*`), X-Api-Key filter (`RequireRole`) and per-credential rate limit (`RequireCredentialRateLimit`). Insufficient role still returns 401 as before; Step 4 replaces this with policies (403). Enums are serialised as strings. Configured `Security:Users` are seeded as local accounts by `AccountSeedService`.
- `Storage:Provider=Postgres` currently throws a clear error (the legacy Npgsql stores and their tests were removed); Step 3 brings it back with EF Core. `deploy/compose.yaml` still sets Postgres, so compose is broken until Step 3.
- Domain fixes found by tests: `CommunicationLink.RestoreDefaults` now reports whether anything changed. Detection tuning: the mass-balance window is now 60 s (30 s was too short to catch FT-101 = 0). Scenario `process-safety-violation` now forces the PLC-1 fill setpoints (HR100=950, HR101=1000), because forced output registers do not override the loop's commands. `ScenarioEngine.Current` is refreshed every step.
- Tests: 12 architecture, 155 unit (shared `Tests/UnitTests/Support/TestRig` with a FakeTimeProvider), 79 integration (including scenario-to-incident workflow over HTTP and Modbus over a real TCP socket), 3 Node frontend tests. `wwwroot/app.js` has been adapted to the new API (it is replaced in Step 6).

Original plan:
- Infrastructure in-memory stores for all new interfaces:
  - events, telemetry, alarm history, plant state, incidents (with numbering, timeline, comments, version check), detections, rule settings, scenario runs, user accounts.
  - `ChannelPersistenceQueue` plus a hosted `PersistenceWorker`: bounded at 10,000 items, retries ×3 with backoff, then counted as failed.
- Simulation hosted services:
  - `SimulationRecoveryService` (IHostedService that restores the latest state on start; register it before the loop).
  - `SimulationService` loop (`PeriodicTimer(StepSeconds/TimeScale)` → `coordinator.StepAsync`).
  - `PlantConfigurationLoader` (optional JSON topology, plus an initial scenario).
- Protocols: rewrite Modbus on `RegisterAccessService`:
  - Strict MBAP validation (protocol id 0, length, unit id → controller); FC03 holding and FC04 input registers.
  - Exception codes 01/02/03/04, connection limit, timeouts. FC06/FC16 only when writes are enabled.
  - Delete `VirtualRegisterService`.
- Api: thin endpoints over the services for plant, devices, commands, simulation (pause/resume/reset/step), controllers, network, registers, alarms, events (paged), telemetry/trends, incidents (filter/sort/page/search, status/assign/priority/comments/timeline), scenarios (list/start/pause/resume/stop/reset/replay/runs), detections and rules, accounts, health (live/ready).
- Rewrite and port the existing tests to the new API. Do not weaken assertions. Then add unit tests for:
  - hydraulics, interlocks, alarms, PLC loops and safe state, network determinism
  - incident workflow, command service, each detection rule, each scenario (expected detection fires)
- Arch tests: extend to `.razor` `@code` blocks for the no-void rule, add layer-dependency checks for Security/Protocols/Simulation, and add a test-folder structure check.
- Run `dotnet build -c Release` and `dotnet test`. Then **commit** (author and committer `owlCoder`, no trailers) and verify with `git log --format='%an <%ae> | %cn <%ce>'`.
- Run a Mistral review: `cd ~/Desktop/scada && vibe -p "Review the diff of branch feature/full-scada-platform vs main for bugs, SOLID violations, thread-safety and security issues. Report findings only." --model mistral-large-4 --thinking high --max-turns 30 --output text`. Fix real findings.

### Step 3: Persistence (EF Core + PostgreSQL)
- `ScadaDbContext`, entity configurations, and a migration via a local `dotnet-ef` tool manifest.
- The migration must copy legacy tables (`telemetry_samples`, `security_events`, `incidents` from `deploy/postgres/init.sql`) when they exist, and never drop them.
- Concurrency: incident version check. Retention worker over `IRetentionTarget`. Keep the in-memory mode.
- `Tests/DatabaseTests` with Testcontainers.PostgreSql: real database, fail if Docker is missing, no silent skip. Include a migration test and a restart/recovery test.

### Step 4: Auth and observability
- Cookie login (`/account/login`, `/account/logout`, antiforgery, HttpOnly, SameSite=Strict, 30-minute sliding expiry), local accounts seeded from config.
- Dev accounts are generated into a gitignored `.scada/dev-credentials.txt`. Never log secrets.
- Centralised policies Viewer, Operator and Administrator (role hierarchy). Legacy `X-Api-Key` authentication handler when enabled. Optional OIDC (Keycloak in a compose profile, realm JSON without users plus a bootstrap script).
- ASP.NET rate limiter. Denied-request auditing through `IAuthorizationMiddlewareResultHandler`.
- Serilog plus OpenTelemetry (ASP.NET Core, runtime, custom meters for simulation, commands, detections, incidents, scenarios), correlation ID middleware, `/health/live` and `/health/ready`, optional Aspire dashboard container.

### Step 5: OPC UA server
- In the Protocols project: namespace Plant/Tanks/Pumps/Valves/Sensors/Controllers, values updated each step, subscriptions, method nodes for commands routed through `ControlCommandService` with role mapping from user tokens.
- Local self-signed PKI under a gitignored directory, localhost only.
- Integration test with the OPC UA client: read, subscribe, receive a data change.

### Step 6: Blazor HMI and SignalR
- New `src/Hmi` Razor class library, interactive server rendering, hosted by Api. Remove `wwwroot/app.js` and its Node tests; replace them with bUnit in `Tests/FrontendTests`.
- Pages: overview, live SVG plant diagram, tanks, pumps, valves, PLC/RTU, sensors, alarms, events, incidents, scenarios and results, communication diagnostics, register browser, trends (SVG), system health, identity, accounts/configuration (Administrator).
- Dark theme, confirmation dialogs for critical commands, role-based controls.
- `ScadaHub` with `[Authorize]`, initial state sync, and per-subscriber bounded channels (DropOldest) through a `RealtimeBroadcaster` that implements IRealtimePublisher.
- SignalR client tests.

### Step 7: Validation and delivery
- `Tests/EndToEndTests` (Playwright with `WebApplicationFactory.UseKestrel`), `Tests/PerformanceTests`, `Tests/ProtocolTests`, and a Docker smoke script (`scripts/smoke.sh`).
- Compose: api plus postgres, healthchecks, volumes, non-root, localhost-only ports, profiles `keycloak` and `observability`. Keep a simple in-memory option.
- CI jobs: restore/build/unit/arch/frontend; integration plus protocol; database (Testcontainers); e2e smoke; format (`dotnet format --verify-no-changes`); docker build; dependency check (`dotnet list package --vulnerable`).
- Docs: README plus the full list in the original prompt, section 20. Update `docs/implementation-progress.md` checkboxes only after verification.
- Final verification checklist (original prompt, section 24). Push the branch, open a PR or merge to `main` **without force-push**, and confirm CI is green.

## 4. How to resume

```bash
cd ~/Desktop/scada && git status && git branch --show-current
```

```bash
dotnet build src/Security/Security.csproj -c Release
```

Steps 1 and 2 are done; start with **Step 3** above. The `docs/implementation-progress.md` checklist exists but has no Phase B+ items ticked yet. Tick them only after the solution builds and tests pass.
