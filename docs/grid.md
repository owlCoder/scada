# Grid simulation mode

The API can run a second, independent simulation mode next to the water plant: a Central-European 110/20/0.4 kV
distribution network. The mode is selected on the sign-in page; water code paths are untouched. All grid state lives in
`GridEngine` (one lock-serialised `GridWorld`), driven by `GridSimulationService` and exposed through
`GridCoordinator` to REST, realtime, observers (grid detection, grid scenarios, IEC 60870-5-104) and the HMI.

## Model and equations

Topology (`Domain.Grid.Network.GridWorld`):

- Switch states decide connectivity. BFS from the source slack nodes marks the energized island; every other node gets
  0 V and its loads report loss of supply.
- Lines are pi-models (`R+jX` per km × length, charging `B` split to both ends). Transformers are pi-models with an
  off-nominal tap on the HV terminal: `Yff = (1/Z + Ysh)/τ²`, `Yft = -1/(Z·τ)`, `Ytt = 1/Z + Ysh`, with
  `Z = (Pcu + j√(uk²−Pcu²)) · (Sbase/Srated) · (Vlv,bus/Vlv,rated)²` and `τ = (Vhv,rated/Vlv,rated)/(Vhv,bus/Vlv,bus) / (1 + (tap−neutral)·step%)`.
- The 110 kV infeed is a slack bus (`VoltageSetpointPu`, default 1.0) behind its Thevenin impedance
  `Z = (Sbase/Sk")·(1+j10)/√101`.
- Capacitor banks are shunt admittances `j·Q·steps/stepsMax/Sbase` with `Q ∝ V²` in the snapshot.

Power flow (`Domain.Grid.PowerFlow.GridPowerFlowSolver`): polar Newton–Raphson in per-unit on a 100 MVA base with
per-voltage-level impedance bases. Unknowns are `|V|, δ` of every non-slack energized node; the Jacobian is evaluated by
central finite differences of the power mismatch `S_specified − V·(YV)*`. Loads have a ZIP voltage dependency
(`P(V) = P·(0.4 + 0.3·V + 0.3·V²)`); PV plants are PQ injections following the sun curve
`max(0, sin(π·(h−6)/12))`; the solver therefore handles radial and meshed (tie closed) networks identically.
Convergence tolerance is 1e-6 pu, max 30 iterations, with damping when a voltage delta exceeds 0.2 pu. On divergence the
last good state is kept and a `SolverDivergence` alarm is raised.

Short circuit: an injected `ThreePhaseShortCircuit`/`EarthFault` on a line is modelled as a fault node in the middle of the
line (bus faults at the bus). The Thevenin impedance at the fault point is read from the inverse of the network admittance
(`Z = Y⁻¹` restricted to the energized block), `I_f = V_f/Z_th`, and the resulting voltage drop is superimposed on the
pre-fault solution (earth faults use a residual equivalent `Z0 ≈ 3·Z1`, which also yields the 51N earth current per branch).

Protection (`Domain.Grid.Protection.GridProtectionCurves`, IEC 60255): operate time
`SI: t = TMS·0.14/((I/Is)^0.02−1)`, `VI: t = TMS·13.5/((I/Is)−1)`, `EI: t = TMS·80/((I/Is)²−1)`, definite time `t = TMS`.
Each relay accumulates `dt/t_op` per simulation sub-step (0.02 s while a fault is present); at 1.0 it trips its breaker.
Closing onto a persistent fault re-trips (the relay re-accumulates because the breaker operation counter changed).
`SwitchStuck` makes a breaker refuse to open; the busbar/incomer 51 with a longer delay then clears the fault (backup protection).

OLTC/AVR: every `AvrDelaySeconds` the tap moves one step toward `AvrTargetLvKv` while the error exceeds the deadband
(`target·deadband%/2`). Tap commands in automatic mode are rejected (set manual first).

Alarms (`GridAlarmType`) have pickup delay and hysteresis: under/overvoltage raise at the bus limit and clear 0.01 pu
inside it (2 s delay), overload raises above 100 % and clears below 98 % (5 s), high overload above 110 %; plus
protection trip, loss of supply per load, communication loss per RTU, tap at limit and solver divergence.

Measurements: every RTU reports voltage/current/power/tap/switch-state for its elements plus frequency (50 Hz ± 0.005).
Analog values carry ±0.2 % seeded noise; an offline RTU reports `Bad` quality with frozen last values; a spoof overrides the
reported value (ground truth `TrueValue`/`IsSpoofed` is visible to Administrators only).

Daily profiles (`Domain.Grid.Profiles.GridProfiles`): 24-point residential (evening peak 19–20 h), commercial (daytime)
and industrial (shift) curves interpolated at the simulated time of day, plus small seeded noise. The simulation starts at
`Grid:StartTimeOfDay` (default 08:00) and advances `Grid:StepSeconds` per step.

## Default network

`DefaultGridDefinition.Create()` (`src/Domain/Grid/Definition/DefaultGridDefinition.cs`):

- 110 kV infeed `GRID` (Sk" 2500 MVA) → `TR1` 110/20 kV, 31.5 MVA, uk 12 %, OLTC ±10 × 1.5 %, AVR target 20.5 kV,
  deadband 1.5 %, delay 30 s → 20 kV busbar `MV` with incomer breaker `Q0` (backup 51, 1200 A / 1.2 s) and busbar
  undervoltage `MV-27` (0.8 pu / 5 s).
- Three feeders `F1`–`F3`, each with a busbar breaker `QF1`–`QF3` and 51 (SI, 220 A / 0.15), 50 (1500 A / 0.08 s) and 51N
  (50 A / 0.2 s): F1 urban cable NA2XS(F)2Y 3x1x150 (0.206+j0.122 Ω/km, 300 A), F2 mixed, F3 rural overhead Al/Fe 95/15
  (0.306+j0.365 Ω/km, 330 A).
- 11 distribution substations 20/0.4 kV (0.63/1.0 MVA, uk 6 %, Dyn11) with disconnectors, each feeding a 0.4 kV bus with
  aggregated consumers (residential ~190, commercial ~85, industrial ~12 customers; peaks 0.38–0.68 MW).
- 2 PV plants (0.8 MW at 0.4 kV, 1.5 MW at 20 kV), one 20 kV capacitor bank `CAP1` (2 Mvar, 4 steps), and a normally-open
  tie load-break switch `TIE` between the ends of F2 and F3 for restoration.
- RTUs: `RTU1` (common address 1, the 110/20 kV station) and `RTU2`–`RTU4` (CA 2–4, one per feeder).
- Diagram positions form a left-to-right single-line diagram (source left, vertical busbar, feeders as rows).

At 08:00 the default network converges in ~3 iterations with all voltages in 0.95–1.05 pu, max loading ≈ 71 % and no
alarms; at the 19:00 evening peak it stays below 100 % loading.

## Network templates

`GridTemplateCatalog` (`src/Domain/Grid/Definition/Templates/`) lists the built-in starting points for the network
editor (`/grid/editor`, Administrator → "Start from template"): the default network plus two realistic alternatives,
each a factory returning a fresh, validator-clean `GridDefinition` (no errors, no warnings — every element is assigned
to an RTU). All three converge at 08:00 and at the 19:00 evening peak with voltages inside 0.95–1.05 pu, no branch
above 100 % loading and no active alarms, resolve the targets of all eight grid attack scenarios through the real
`GridScenarioTargetResolver`, and keep every 51/50 pickup above the maximum load current of its element over a full
simulated day.

- `UrbanCableGridTemplate` (`urban-cable-20kv`): a 20 kV urban cable network fed from one 110/20 kV substation with
  two 40 MVA transformers (uk 12.5 %, OLTC ±10 × 0.9 %, AVR target 20.6 kV) on separate 20 kV busbar sections joined by
  a normally-closed bus coupler breaker (`BC`, 50 m coupler cable). Four cable feeders (`NA2XS(F)2Y 3x1x150`,
  0.206+j0.122 Ω/km, 300 A, 0.4–1.0 km sections) form two open rings: each ring is two feeders tied at their far ends by
  a tie cable with a normally-open ring point (`NOP-A`/`NOP-B`, load-break switches) for restoration. Fourteen compact
  20/0.4 kV substations (1000/630 kVA, uk 6 %, Dyn11) carry a commercial-heavy mix (10 commercial, 4 residential loads,
  one critical), a 2.4 Mvar 4-step capacitor bank sits on section A, and five RTUs cover the station (CA 1) and one
  feeder each (CA 2–5). Feeder breakers carry 51 (SI, 160 A) + 50 (2500 A) + 51N (40 A); incomers and coupler carry
  backup 51/50 and busbar undervoltage 27.
- `RuralOverheadGridTemplate` (`rural-overhead-20kv`): a rural 20 kV overhead network fed from one 110/20 kV 20 MVA
  transformer (uk 11 %, OLTC ±8 × 1.25 %, AVR target 20.4 kV — the long feeders need the regulator for voltage drop).
  Three long Al/Fe 95/15 feeders (0.306+j0.365 Ω/km, 330 A; 20/21/12 km, the last FC section Al/Fe 50/8 210 A) carry
  ten pole-mounted 20/0.4 kV transformers (100/250/400 kVA, Dyn5) with residential village loads. Two large PV parks
  (PV1 3.0 MW at the end of feeder A, PV2 2.5 MW at the end of feeder B) cause reverse power flow at midday — the
  deliberate exception: at 12:00 generation exceeds load and power is exported towards the 110 kV infeed, while
  voltages stay inside 0.9–1.1 pu and no branch overloads; the AVR keeps the tap strictly inside ±8 over the whole day.
  Feeder breakers use a reclosing-style scheme (50 1800 A / 51 SI 150 A / 51N 25 A — protection only, the engine has no
  automatic reclose logic), plus incomer backup 51/50 and busbar 27; a 1.8 Mvar 3-step capacitor bank at the 20 kV
  busbar covers the capacitor scenario target. Four RTUs: station (CA 1) and one per feeder (CA 2–4).

Both templates use clean left-to-right diagram positions (source left, busbar, feeders as rows, substations below
their ring node). Tests: `Tests/UnitTests/Grid/Templates/` (validation, catalog shape, convergence at 08:00/19:00,
midday reverse power, AVR tap range, protection pickups vs maximum load currents, scenario target resolution with the
real `GridScenarioTargetResolver`, and a full simulated day on the real `GridEngine` + real `GridDetectionEngine`);
the editor picker is covered by `Tests/FrontendTests/Pages/GridTemplatePickerTests.cs`.

## Security model

- `GridControlService` (`IGridControl`, Operator): role check → command rate limit → replay guard (sequence numbers) →
  atomic select-before-operate with `Grid:SelectTimeoutSeconds` (default 30 s, wall clock) → audit of every accept/reject.
  Disconnectors refuse to operate under load (> 1 A); an offline owning RTU yields `CommunicationLoss`.
- `GridTrainingService` (`IGridTraining`, Administrator, audited, `Scenario` events): inject/clear faults, spoof
  measurements, change protection settings, force switches/taps, toggle RTUs, restore training defaults.
- `GridViewRedactor.ForOperators` hides injected faults, true values, spoofing, protection setting changes and the
  attribution of unattributed changes; operators still see reported switch states and real protection trips.
- `GridCoordinator` publishes `SecurityEvent`s (audit, authorization-denied, communication, process-alarm, scenario,
  device-change) and the realtime topics `grid`, `grid-alarms`, `grid-status`; `IGridObserver`s are notified after the
  engine lock is released and observer exceptions are logged, not propagated.

## Options

Section `Grid` (see [configuration.md](configuration.md)):

| Key | Default | Meaning |
| --- | --- | --- |
| `Grid:Enabled` | `true` | Run the hosted grid loop and load the active definition on startup |
| `Grid:StepSeconds` | `1` | Simulated seconds per step (0,60] |
| `Grid:TimeScale` | `60` | Simulated seconds per wall second; the loop uses a wall-clock timer |
| `Grid:RandomSeed` | `104` | Seed for noise, profiles jitter and measurement noise |
| `Grid:SelectTimeoutSeconds` | `30` | Select-before-operate timeout (wall clock) |
| `Grid:StartTimeOfDay` | `08:00` | Simulated time of day at startup (HH:mm) |

The loop timer is deliberately wall-clock (`new PeriodicTimer(period)`), like the water `SimulationService`: tests may
replace the DI `TimeProvider` with a fake one, and a fake-time timer would fire thousands of steps per `Advance()`.

## Storage

`IGridDefinitionStore` keeps immutable versions of network definitions; exactly one is active. The in-memory
implementation is seeded with the default network as version 1. With `Storage:Provider=Postgres` the
`PostgresGridDefinitionStore` uses table `scada.grid_definitions` (jsonb definition, version, created_by/at, comment,
active) with a unique partial index on `active` and a table lock for version allocation; migration `AddGridDefinitions`.
On startup `GridSimulationService` loads the active version into the engine.

## REST

Base path `/api/grid`, same API-key/cookie + CSRF + rate-limit conventions as the plant endpoints.

| Endpoint | Role | Purpose |
| --- | --- | --- |
| `GET /api/grid` and `/api/grid/snapshot` | Viewer | Snapshot; redacted unless Administrator |
| `GET /api/grid/status` | Viewer | Paused, simulated time, step count, definition name/version |
| `GET /api/grid/definition` | Viewer | Live definition |
| `GET /api/grid/definitions` | Viewer | All stored versions |
| `GET /api/grid/measurements` | Viewer | Reported measurements (redacted unless Administrator) |
| `POST /api/grid/commands` | Operator | `GridCommand` (SBO switches, taps, capacitor steps, load shed, protection reset, alarm ack) |
| `POST /api/grid/runtime/pause` / `resume` | Operator | Pause/resume the loop |
| `POST /api/grid/runtime/reset` | Administrator | Reset the world to the active definition |
| `POST /api/grid/definitions/{version}/activate` | Administrator | Load a stored version into the engine |
| `POST /api/grid/definitions/validate` | Administrator | Validate a definition (errors block, `Warning:` items do not) |
| `POST /api/grid/definitions` | Administrator | Save a new immutable version |
| `POST /api/grid/training` | Administrator | Training actions: `fault`, `clearFault`, `spoof`, `protection`, `switch`, `tap`, `rtu`, `restore` |

Rejections map to 403 (not authorized), 429 (rate limited) and 400 (everything else) with the `GridCommandResult` body.

## Source map

- Definition, validator and editor templates: `src/Domain/Grid/Definition/`, `src/Domain/Grid/Definition/Templates/`
- Physics: `src/Domain/Grid/Network/GridWorld.cs`, `PowerFlow/`, `Protection/`, `Profiles/`
- Engine, coordinator, redaction: `src/Application/Grid/`
- Control/training security: `src/Security/Grid/Control/`
- Stores and migration: `src/Infrastructure/Persistence/{InMemory,Postgres}/`, `Migrations/`
- Hosted loop: `src/Simulation/HostedServices/GridSimulationService.cs`
- REST and registration: `src/Api/Endpoints/Grid/`, `src/Api/Configuration/Registration/GridRegistrationExtensions.cs`
- Tests: `Tests/UnitTests/Grid/`, `Tests/IntegrationTests/Grid/`, `Tests/DatabaseTests/Stores/GridEngineDefinitionStoreTests.cs`
