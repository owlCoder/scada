# Grid-mode security: detection rules and attack scenarios

Grid mode (electrical distribution network) has its own security layer, parallel to the water one and sharing its
concepts: a detection engine over `IGridObserver` steps, findings in the shared `IDetectionStore`, incidents via
`IncidentService`, a scenario engine with one active Administrator-only run, and REST routes under `/api/grid/...`.
Water mode is untouched.

- Detection: `src/Security/Grid/Detection/` — `GridDetectionEngine : IGridObserver` evaluates every enabled
  `IGridDetectionRule` after each `GridStepReport` (snapshot + events) plus recent `SecurityEvent`s. Findings become
  `DetectionRecord`s, incidents, `Detections` realtime messages and `scada.detections` metrics exactly like the water
  `DetectionEngine`: deduplication by `rule:key:run` fingerprint, per-rule cooldown, correlation to the active scenario
  run, `Rules` / `Metrics` / `UpdateSettings` over `DetectionRuleSettings` (persisted through the shared
  `IDetectionRuleSettingsStore` and recovered at startup by `GridDetectionSettingsRecoveryService`).
- Scenarios: `src/Security/Grid/Scenarios/` — `GridScenarioEngine : IGridObserver` with catalog, start / pause / resume /
  stop / reset / replay, Administrator only (denials are audited as `AuthorizationDenied` events), one active run, run
  history through the shared `IScenarioRunStore` (`ScenarioRunRecord`), outcome `Detected` / `PartiallyDetected` /
  `Missed` / `Aborted` from expected-vs-observed detections. Actions act only through `IGridTraining`, `IGridControl`
  and read `IGridView`; all targets are resolved generically at run start, so every scenario works on any network the
  user drew. If a target cannot be found, starting fails with a clear message.
- REST: `/api/grid/scenarios...` (Viewer read, Administrator control, 202 + run location on start) and
  `/api/grid/detections` + `/api/grid/detections/rules|metrics` + `PUT /api/grid/detections/rules/{ruleId}`
  (Viewer read, Administrator tune) in `src/Api/Endpoints/GridSecurity/GridSecurityEndpoints.cs`, registered by
  `GridSecurityRegistrationExtensions.AddGridSecurityServices()`.

## Detection rules

All defaults are enabled. `Threshold` semantics are rule-specific; windows/cooldowns are seconds. Normal operation on
the default network over a full simulated day (0.2 % telemetry noise, daily load profile) produces **zero** findings —
covered by `GridNormalOperationTests`.

| Rule ID | Default severity | Threshold | Window (s) | Cooldown (s) | Meaning |
| --- | --- | --- | --- | --- | --- |
| `grid-unsolicited-switching` | Critical | 0 | 60 | 60 | Switch changed with cause Unattributed, or Operator without a matching accepted command (Industroyer-style direct operation) |
| `grid-mass-switching` | High | 3 openings | 60 | 300 | ≥ threshold breakers opened within the window, regardless of stated cause |
| `grid-measurement-inconsistency` | High | 2 % residual | 60 | 300 | Kirchhoff residual per bus (reported generation − load vs reported branch flows) or a single reported value deviating from its true value; 0.2 % noise stays below the default |
| `grid-protection-tampering` | Critical | 0 | 300 | 600 | `ProtectionSettingChanged` event or `SettingsChanged` snapshot flag (relay disabled / pickup raised) |
| `grid-tap-manipulation` | High | 3 changes | 60 | 300 | Rapid tap changes, a forced (unattributed) tap change, or the tap at its limit with the LV bus voltage outside limits |
| `grid-voltage-violation` | Medium | sustained whole window | 60 | 300 | Bus voltage outside its definition limits for the whole window; severity rises to High at 0.05 pu and Critical at 0.10 pu beyond the limit |
| `grid-overload` | Medium | 100 % loading | 60 | 300 | Branch loading above threshold for the whole window; High above 120 %, Critical above 150 % |
| `grid-rtu-communication-loss` | High | 0 | 60 | 300 | RTU offline event, or every measurement of an RTU has Bad quality |
| `grid-unauthorized-command` | High | 1 | 120 | 300 | Rejected commands whose reason is NotAuthorized / Replay / RateLimited, grouped by actor; plus ≥ 8 accepted commands from the `iec104` channel in the window |
| `grid-load-shedding-anomaly` | High | 100 customers | 60 | 300 | Load shed without a protection trip and without an accepted command from an operator console (hmi/rest); or many customers lost with no protection trip in the window |

Settings are tuned with `PUT /api/grid/detections/rules/{ruleId}` (Administrator) and recovered at startup; the same
validation rules as the water engine apply (finite non-negative threshold, window 1–86 400 s, cooldown 0–86 400 s).

## Attack scenarios

Catalog in `src/Security/Grid/Scenarios/Catalog/DefaultGridScenarios.cs`, mapped to the real incidents:
**Ukraine 2015** (BlackEnergy: remote access, spoofed SCADA, load shedding) and **Ukraine 2016 / Industroyer**
(direct breaker operation, protection tampering, FDI, RTU/serial denial of service).

| ID | Duration (s) | Exercise | Expected detections | Real-world analogue |
| --- | --- | --- | --- | --- |
| `grid-industroyer-breaker-opening` | 30 | Forced opening of 3 feeder breakers in quick succession | `grid-unsolicited-switching`, `grid-mass-switching` | Industroyer 2016 payload |
| `grid-oltc-manipulation` | 120 | OLTC forced to max tap → overvoltage at consumers | `grid-tap-manipulation`, `grid-voltage-violation` | Voltage manipulation via tap changers |
| `grid-protection-tampering-fault` | 60 | The relays of one feeder breaker are disabled and their pickup raised, then three-phase short circuits on every feeder head trip several breakers (the tampered feeder only falls to incomer backup) | `grid-protection-tampering`, `grid-mass-switching` | Industroyer protection tampering |
| `grid-false-data-injection` | 90 | Feeder current/voltage measurements spoofed to hide an overload | `grid-measurement-inconsistency` | Ukraine 2015 KillDisk/FDI style |
| `grid-load-shedding-spoofing` | 60 | 8 shed-load commands from an unexpected iec104 actor | `grid-load-shedding-anomaly`, `grid-unauthorized-command` | Ukraine 2015 BlackEnergy blackout |
| `grid-rtu-denial-of-service` | 60 | Feeder RTU offline, then a breaker opened in the blind period | `grid-rtu-communication-loss`, `grid-unsolicited-switching` | Industroyer serial/RTU kill |
| `grid-unauthorized-commands` | 40 | Viewer commands (NotAuthorized) + replayed sequence number | `grid-unauthorized-command` | Ukraine 2015 stolen credentials |
| `grid-capacitor-manipulation` | 120 | AVR switched to manual, all capacitor steps in one iec104 burst, then the OLTC stepped to its limit by iec104 commands → overvoltage plus the command burst | `grid-voltage-violation`, `grid-unauthorized-command` | Coordinated reactive-power attack |

Targets are selectors resolved at run start against the running definition: `FeederBreakers` (most loaded first),
`MostLoadedFeeder`, `FeederProtection`, `TransformerWithOltc`, `LargestLoads`, `FeederRtu`, `RtuBreaker`, `AnyBreaker`,
`AllCapacitors`, `FeederCurrentMeasurements`, `HighestLoadBusVoltageMeasurement`. A scenario whose target does not exist
on the drawn network (e.g. OLTC scenario on a network without a tap changer) refuses to start with a clear error.

Lifecycle mirrors the water engine: start returns 202 + run location; pause/resume only stop scenario scheduling; stop and
reset produce `Stopped`/`Aborted`; replay links `replayOf`; completion runs the cleanup actions, restores training
defaults (`RestoreTrainingDefaultsAsync`) and persists the run with expected/observed detections and the outcome.
Measurements per run include customers lost, max loading, min/max voltage, alarms raised and unattributed changes.

## Tests

- `Tests/UnitTests/Security/Grid/...` with scripted fakes `SecurityFakeGridView` / `SecurityFakeGridTraining` /
  `SecurityFakeGridControl` and two fake networks (`SecurityFakeGridNetworks.Default/Alternative`): each rule fires on
  its pattern and stays silent on normal data; dedup/cooldown; incidents; scope reset between runs; a full simulated day
  produces zero findings; every scenario resolves targets on both networks, runs to completion, issues the expected
  training/control calls and is scored Detected; back-to-back runs; role checks and audit; clear start failures.
- `Tests/UnitTests/Grid/GridRealEngineSecurityTests.cs` (+ `Support/GridRealEngineRig.cs`) runs the **real** merged stack —
  `GridEngine` + `GridCoordinator.StepAsync` with the real `GridDetectionEngine`, `GridScenarioEngine`,
  `GridControlService` and `GridTrainingService` over in-memory stores and a fake clock (deterministic, no wall-clock
  waits): a full simulated day on the default network with the default seed produces **zero** findings, each of the 8
  catalog scenarios ends `Completed`/`Detected` with its expected rule ids observed and all actions succeeded, and after
  the run's cleanup/restore the network is back to a sane state (all breakers closed but the normally-open tie, no
  faults/spoofs/active alarms, taps/capacitors/protections at definition defaults, converged); all 8 also run back-to-back
  on one engine instance (this exercises the scenario engine's re-entrancy guard and the process-wide replay guard).
- `Tests/IntegrationTests/Api/GridSecurityApiTests.cs` drives the REST surface (roles, 401/403/404/400, run lifecycle,
  detections/incidents per run, rule tuning) against a fake view, **and** the real engine: with the hosted loop disabled
  the test steps `GridCoordinator` directly — a full simulated day produces zero grid findings through
  `/api/grid/detections`, and each catalog scenario started through `POST /api/grid/scenarios/{id}/start` ends
  `Detected` with the expected detections observed and the network sane again (checked through the admin view).
