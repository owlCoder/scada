# Session Handoff: Full SCADA Platform Build-out

Read this first in a new session. It records exactly where the previous session stopped.
The original task is the "MASTER IMPLEMENTATION PROMPT — COMPLETE SCADA PROJECT" (paste it again in the new session together with this file).

## 0. Team setup (from 2026-11-21, after Step 4)
- `main` on GitHub (owlCoder/scada) contains Steps 1–4 (fast-forward from feature/full-scada-platform, normal push).
- The lead (Claude) coordinates. Codex CLI (`codex exec -m gpt-6.2-sol -c model_reasoning_effort=high`, workspace-write sandbox with network)
  implements Step 5 in the worktree `../scada-opcua` on branch `feature/opcua` (brief: `.scada/agents/opcua-brief.md`).
  A second Codex run fixes the Mistral review findings for Steps 3–4 in `../scada-fixes` on branch `fix/review-steps3-4`
  (brief: `.scada/agents/review-fixes-brief.md`; review: `.scada/review/mistral-steps3-4.txt`, 14 findings, 10 and 12 skipped).
  A third Codex run builds the remaining HMI pages in `../scada-hmi-pages` on `feature/hmi-pages`, branched from the lead's
  HMI foundation on `feature/hmi` (brief: `.scada/agents/hmi-pages-brief.md`).
  Mistral (`vibe --model mistral-large-4 --enabled-tools read_file`) reviews diffs read-only
  (prompts and results in `.scada/review/`). The lead built the Step 6 foundation on `feature/hmi`; merge order is
  fix/review-steps3-4 → main, feature/opcua → main, feature/hmi + feature/hmi-pages → main. Run every test after each merge,
  including the Docker-based DatabaseTests. Agent reports are written to the scratchpad (`codex-*-report.md`).
- Step 6 foundation (commit 7ff09c6 on feature/hmi):
  - `src/Hmi` with no prerendering, `HmiSession` (redacted plant for non-instructors) and `RealtimeComponentBase`.
  - Pages done: overview, diagram, pumps, valves, alarms and identity. `ConfirmDialog` covers critical actions.
  - `Infrastructure/Realtime/RealtimeBroadcaster`: one bounded DropOldest channel per subscriber, implementing `IRealtimePublisher` and `IRealtimeFeed`.
  - `Api/Realtime/ScadaHub` at `/hubs/scada` (Viewer) with `GetSnapshot`, plus `RealtimeHubForwarder`; non-instructors get redacted plant data.
  - `ScadaRevalidatingAuthenticationStateProvider` (1 minute) and an `Issued` claim. Hub paths answer 401/403.
  - The old `app.js`/`index.html` and the Node tests were replaced by bUnit (`Tests/FrontendTests`).
  - The SignalR client is pinned to 10.0.15, because 10.0.0 has advisory GHSA-334f-vvp4-7g6p. The local runtime is ASP.NET Core 10.0.0; use a patched runtime image in Step 7.
- Disk: the Docker build cache was pruned on 2026-11-21 (from 1.8 GB to 28 GB free). Do not prune volumes; they belong to other projects.

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

Second commit (review + live-run fixes, 2026-11-21):
- Mistral Large 4 review (read-only: `vibe --prompt-file .scada/review/prompt.txt --enabled-tools read_file ...`; headless bash is denied, so give it a diff file under the gitignored `.scada/review/`). All 11 findings fixed: the API key authenticator now reads the account store on every request (disable, role change and key rotation/revocation take effect at once; accounts are seeded in `Program.cs` before the first request via `AccountSeeder`); failed keys are audited as `Authentication` events and throttled per source IP (10/min, then 429); register writes resolve and write under one engine lock; Modbus writes need `Protocols:Modbus:AllowWrites` in addition to `Registers:WritesEnabled` and are rate-limited per client host; Viewers and Operators get `PlantViewRedactor.ForOperators` (no true values, spoofing or faults; tank level = reported level), Administrators see ground truth; LRU replay guard; release-forces on an unknown controller returns 404; minimum loop period; minimal anonymous `/health/ready`; CSP header; `IPlantView` split from `ITrainingEnvironment`.
- Problems found by running all 14 scenarios back to back in a live process (now covered by `ScenariosRunBackToBackInOneProcessAreEachDetected`): detection deduplication and event windows are scoped per scenario run, and rule state is reset when the scope changes; historian-integrity reports each altered sample once; loop-less outputs (V-102/V-103/V-201/V-301/V-302) are held at `IoPoint.AutomaticPosition` in automatic mode (before this they stayed closed forever after a fail-safe); `RestoreDefaults` also resets controllers latched in SafeState; scenarios use distinct identities; `false-measurement-injection` first opens V-101; `process-safety-violation` also forces the PLC-2 setpoints and expects `unexpected-pump-activity`.
- Live verification: API run with Modbus on, all 14 scenarios over HTTP back to back gave 14/14 Detected, the plant was healthy afterwards (no forces, spoofs, bypass or down links), no errors in the log, and the dashboard works in a browser.
- Note for Step 6: the realtime plant topic must also be redacted for non-instructors.

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

### Step 3: Persistence (EF Core + PostgreSQL), DONE (2026-11-21)

What was built:
- `src/Infrastructure/Persistence/Postgres`:
  - `ScadaDbContext` (schema `scada`, snake_case columns) with one static `*EntityConfiguration.Apply` per entity, entities that map to and from the application records, and a Postgres store for every store interface.
  - Incident numbers come from the `incident_number_seq` sequence. `version` is the concurrency token; a stale update returns false.
  - Search uses `ILIKE` with an explicit escape character. Npgsql otherwise sends `ESCAPE ''`, and `%` or `_` would act as wildcards.
- Migration `InitialSchema` (generated with the local `dotnet-ef` 10.0.12 from `.config/dotnet-tools.json`):
  - It runs `LegacyDataImport.Sql`, which copies `public.security_events`, `public.telemetry_samples` (as tags `LT-101` and `P-101.running`) and `public.incidents` when they exist. Status 2 maps to Resolved. A `Created` timeline entry is added and numbering continues. The legacy tables are never altered or dropped.
  - `ScadaDatabaseMigrator` runs from `Program.cs` before `AccountSeeder`. `Storage:ApplyMigrations=false` disables it.
- Retention:
  - `Persistence/Retention/RetentionWorker` covers every `IRetentionTarget` in both storage modes.
  - Configured through `Retention:Interval|DefaultMaxAge|MaxAge:<target>`.
- Health:
  - `IStorageProbe` has memory and Postgres implementations.
  - `/health/ready` returns 503 when storage is unreachable and reports no error text; `/health` reports the provider.
- Architecture rules:
  - Generated EF migrations are excluded from the source-structure rules.
  - `override void` is allowed (OnModelCreating, Up, Down).
- Compose and logging:
  - Compose no longer mounts `init.sql`. The image is pinned to `postgres:17.7-alpine`; the floating `17-alpine` snapshot was corrupted in the local Docker cache after the disk filled up.
  - The host port is configurable with `POSTGRES_PORT`.
  - `.dockerignore` excludes `.scada`.
  - EF logging is at Warning.
- `Tests/DatabaseTests` (20 tests, Testcontainers): migrations and model drift, the legacy import, all stores including concurrent incident updates, retention, a full API restart on the same database, and the storage probe. They fail if Docker is unavailable.
- Dev secrets for the compose stack are in `.scada/dev-credentials.txt` (POSTGRES_PORT=55432 locally, because 5432 is taken by another project's container).
- Live verification (API on the compose PostgreSQL, TimeScale 20, fresh database): all 14 scenarios Detected back to back over HTTP. After a restart on the same database, 14/14 runs, 21/21 incidents, 312 events and the durable telemetry (3190 LT-101 samples) were all still there. A first start on an empty database logs no warnings or errors. Note: `/api/trends` is the live in-memory historian window; `/api/telemetry` reads the durable history.
- The Docker image build was not run, because only about 4 GB of disk was free; it belongs to Step 7's smoke test.

Original plan:
- `ScadaDbContext`, entity configurations, and a migration via a local `dotnet-ef` tool manifest.
- The migration must copy legacy tables (`telemetry_samples`, `security_events`, `incidents` from `deploy/postgres/init.sql`) when they exist, and never drop them.
- Concurrency: incident version check. Retention worker over `IRetentionTarget`. Keep the in-memory mode.
- `Tests/DatabaseTests` with Testcontainers.PostgreSql: real database, fail if Docker is missing, no silent skip. Include a migration test and a restart/recovery test.

### Step 4: Auth and observability, DONE (2026-11-21)

What was built:
- Authentication (`src/Api/Authentication`):
  - The default policy scheme `Scada` forwards to `ApiKey` when an `X-Api-Key` header is present, else to `Cookies`.
  - `ApiKeyAuthenticationHandler` wraps `ApiKeyAuthenticator` and answers 429 when the client is throttled.
  - Cookie `scada.session`: HttpOnly, SameSite=Strict, 30-minute sliding expiry. `CookieSessionValidator` rechecks the account store on every request (disable, lock or password change ends the session; a role change applies immediately).
  - `/api` paths get 401/403 instead of redirects.
  - Optional OIDC (code + PKCE). `OidcRoleMapper` maps `roles` to the highest SCADA role, and an identity with no SCADA role is rejected.
- Authorization (`src/Api/Authorization`):
  - Policies Viewer/Operator/Administrator are built from `MinimumRoleRequirement` (role hierarchy).
  - `RequireRole` now means `RequireAuthorization`: insufficient role is **403** (was 401).
  - `AuditingAuthorizationResultHandler` audits 403s as `AuthorizationDenied`.
  - `http.Principal()` and `http.Actor()` are rebuilt from claims; endpoint code was unchanged.
- Sign-in (`Endpoints/SignIn`):
  - `GET/POST /account/login` (server-rendered, no script, antiforgery form, only local `returnUrl`).
  - `POST /account/logout` (antiforgery checked explicitly; the middleware only flags failures).
  - `/account/login/oidc` and `GET /account/antiforgery`.
  - `CookieAntiforgeryMiddleware` requires `X-CSRF-TOKEN` for cookie-authenticated state-changing `/api` calls.
- Security layer:
  - `PasswordAuthenticator`: lockout (5 failures, 15 minutes; `Security:Lockout`), per-IP throttle, timing-safe handling of unknown users, audit without the password, and `ValidateSessionAsync`.
  - `UserCredential` gains `Password`/`PasswordHash`; the API key is optional.
  - `Development/DevelopmentAccountGenerator` (Development only, `appsettings.Development.json`) creates dev-viewer, dev-operator and dev-admin, and rewrites only the `SCADA_DEV_*` lines of `.scada/dev-credentials.txt` (mode 600).
- Rate limiting: a global fixed window per credential or IP (1200/min), the `login` policy (10/min per IP), health endpoints exempt. `RateLimitAuditor` audits rejections at most once per minute per partition.
- Observability:
  - Serilog via `AddSerilog` (no frozen bootstrap logger, so the test hosts work), with JSON output optional.
  - OpenTelemetry tracing (ASP.NET Core, Npgsql) and metrics (ASP.NET Core, runtime, Npgsql, `Scada`, `Scada.Runtime`). OTLP export runs only when `Observability:OtlpEndpoint` is set.
  - `Application/Diagnostics/ScadaInstrumentation` is a static `Meter`, incremented at the source (commands, events, detections, incidents, scenario runs, steps).
  - `CorrelationIdMiddleware` sets the response header, a log property, the trace tag and baggage; `EventPublisher` fills `SecurityEvent.CorrelationId` from that baggage.
- `Cache-Control` is now `no-cache, no-store` plus `Pragma: no-cache` (the values antiforgery requires, so it no longer overrides them with a warning).
- Compose: Keycloak lives in `deploy/compose.keycloak.yaml`, not a profile, because compose interpolates required variables of inactive profiles too. There is a realm JSON with no users, plus `scripts/keycloak-bootstrap.sh`. The Aspire dashboard is a profile (`observability`).
- Tests: integration tests went from 86 to 118 (cookie sign-in, CSRF, lockout, session revocation, role change, logout, OIDC challenge with static metadata, role mapping, 403 auditing, rate limits, correlation id, command metrics, dev accounts). Unit tests went from 166 to 172 (PasswordAuthenticator). 17 existing assertions were changed from 401 to 403, for authenticated callers with too low a role only.
- Live verification (Development environment, in-memory storage):
  - Dev accounts were generated (file mode 600). Form sign-in as dev-operator returned 302, and `/api/me` showed Operator.
  - A cookie API write without the CSRF token got 400; with the token, 200. A viewer key on an admin endpoint got 403.
  - The correlation id was echoed. No password or key appeared in the log.
  - The login page rendered in the browser with no CSP errors.
  - All 14 scenarios were Detected back to back with no ERR lines in the log. The Keycloak and Aspire containers were not started (low disk); OIDC is covered by the challenge test and the role-mapping tests.

Original plan:
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

Steps 1–4 are done; start with **Step 5** above. Tick `docs/implementation-progress.md` items only after the solution builds and tests pass. `dotnet test scada.slnx` needs a working Docker engine for `Tests/DatabaseTests`.


## 5. Steps 3–6 review resolution (`fix/review-step6`)

Verified `.scada/review/mistral-hmi.txt` and `mistral-fixes.txt` against the merged code before changing it.

- F1/F6: extended the shared plant redactor; REST, HMI (Pumps, Valves, Diagram, Overview), hub snapshots and live operator messages all use it. Independent safety trips remain visible. Current domain interlock reasons originate in the safety evaluator, not injected faults; defensive filtering also hides fault-specific diagnoses when a hidden fault exists.
- F2/F3/F4/F5: actor-checked, audited simulation pause/resume; confirmation for pause/resume and alarm acknowledgement; denied incident mutation audits; serialized account updates with self/last-enabled-administrator guards.
- F9/F10/F11: logged refresh failures with a shared recoverable banner on every live page; awaited, cancellable Overview/Alarms reads with load-id guards; cached lifetime token and awaited pump disposal.
- F12/F13/F15: page cap 10,000, UI/server filter cap 200 with trimming, and scenario publication after successful saves (including periodic realtime publications). Failed starts do not reserve an active run; failed pause/resume saves restore the previous state.
- Persistence/auth review: stable telemetry UUIDs, conflict-ignoring inserts and EF-generated `AddTelemetrySampleIdentity`; three-attempt password conflict budget; warning-only advisory-unlock errors; PBKDF2 verification capped at 1,000,000 iterations. Regression tests include ambiguous commits, existing-schema upgrades and unlock failure following migration failure.
- F7 is intentional field diagnostics: forced-register flags/counts are already documented operator-visible register data. No requirement here hides register forces. F8 is intentional health diagnostics: documented counters are also available from public readiness probes and contain no secrets. F14 is the deliberate health HTML/JSON negotiation contract; monitors should request JSON, now explicit in the operations guide. These are not authorization defects. F6 duplicates F1.
- Seeder review item 4 is intentional bootstrap-only behavior (already implemented with `TryCreateAsync`); clarified restart and lost-development-credentials recovery in the security guide. Clock-skew item 5 is deferred as requested; strict future-session rejection remains.


Validation for this review fix:

- `dotnet build scada.slnx -c Release --disable-build-servers`: passed, zero warnings/errors (native macOS and Linux SDK 10.0.100).
- `dotnet ef migrations add AddTelemetrySampleIdentity --project src/Infrastructure --startup-project src/Infrastructure --output-dir Persistence/Postgres/Migrations --namespace Infrastructure.Persistence.Postgres.Migrations`: generated the migration/designer/snapshot. Database tests verified the model has no pending changes.
- `dotnet test Tests/<Project> -c Release --no-build`: all eight suites passed in Linux containers: ArchitectureTests 13, UnitTests 223, IntegrationTests 149, FrontendTests 40, ProtocolTests 51, PerformanceTests 6, DatabaseTests 30, EndToEndTests 8; total 520, zero skips.
- Docker 29.9.3 was available. DatabaseTests used PostgreSQL 17.7 Testcontainers with isolated databases; atomic sign-in `UPDATE ... RETURNING` tests passed.
- Native macOS architecture/unit/frontend tests passed. Native OPC UA hit unavailable-keychain errors, the database API-restart test timed out during host startup, and native Chromium was denied Mach-port access by the sandbox. Linux reruns resolved these environment failures. Remaining stalled native integration/protocol/performance attempts were cancelled after the Linux replacements completed.
- Browser command used `PLAYWRIGHT_BROWSERS_PATH=$HOME/.cache/ms-playwright SCADA_E2E_REQUIRED=1 dotnet test Tests/EndToEndTests -c Release --no-build`. The Linux container used a separate host cache subdirectory to avoid treating macOS Chromium installation markers as Linux binaries.
- One unchanged protocol stalled-client timing test timed out on the first Linux run; the complete protocol suite passed on the next run. No assertion or timeout was weakened.
- `git diff --check`: passed. No push, merge, rebase or other worktree changes.
- Commit attempt: sandbox denied creating `/Users/danijel/Desktop/scada/.git/worktrees/scada-review6/index.lock` during both staging and commit. Changes are deliberately left uncommitted for the lead, per the task instructions.
