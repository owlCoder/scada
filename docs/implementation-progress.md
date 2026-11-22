# Implementation Progress

Durable checklist for the full SCADA platform build-out (branch `feature/full-scada-platform`).
A box is ticked only after the feature is implemented **and** verified by a build plus tests.

Baseline (before this work): 110 .NET tests + 3 Node frontend tests passing on `main` at `e95fb31`.

## Overall status (2026-11-22)

The plan is finished: Steps 1–7 of the plan in `docs/SESSION-HANDOFF.md` (now a historical record), the electrical grid mode
and the dispatcher workspaces for both modes are all done, verified and merged to `main`, with CI green.
No planned work remains open.

## Phase A — Inspection
- [x] Repository cloned to Desktop, Git identity set to `owlCoder` (verified GitHub-linked email)
- [x] Baseline Release build and tests verified

## Phase B — Industrial domain model
- [x] Multi-tank / multi-pump / multi-valve plant definition
- [x] Level, flow, pressure sensors with faults and spoofing
- [x] Hydraulic step calculation, pump ramping, valve travel
- [x] Safety interlocks (overfill, dry-run, deadhead) and bypass
- [x] Alarm book (raise / clear / acknowledge)
- [x] Simulation clock, pause / resume / reset, deterministic stepping
- [x] Plant snapshot capture and restore

## Phase C — Controllers, protocols, network
- [x] Virtual PLC / RTU with scan cycles, I/O maps, registers, safe state
- [x] Virtual network: links, latency, loss, segmentation, metrics
- [x] Modbus TCP (MBAP validation, FC03/FC04, exceptions, multi-unit maps)
- [x] OPC UA server simulator

## Phase D — Cybersecurity engine
- [x] Scenario framework and 14 behavioural scenarios
- [x] Detection engine and rules
- [x] Incident management workflow, comments, timeline

## Phase E — Persistence
- [x] EF Core DbContext, configurations, migrations (legacy tables copied, never dropped)
- [x] Bounded asynchronous persistence queue
- [x] Plant state recovery after restart (PostgreSQL; covered by an API restart test)
- [x] Retention policies

## Phase F — Authentication and observability
- [x] Cookie login, password hashing, policies, API key compatibility, OIDC option (Keycloak not run live)
- [x] Serilog, OpenTelemetry, health endpoints

## Phase G/H — HMI and real-time
- [x] Blazor HMI pages
- [x] SignalR hub and realtime broadcasting

## Phase I/J — Validation and delivery
- [x] Database, protocol, E2E, performance, Docker smoke tests
- [x] Documentation
- [x] CI green, pushed

## Phase K — Electrical grid mode
- [x] Sign-in mode choice (Water / Grid) with a mode claim and switch
- [x] Network model: 110/20/0.4 kV sources, transformers with OLTC/AVR, lines, cables, switches, loads, PV, capacitors, RTUs
- [x] Newton–Raphson power flow (radial and meshed), short-circuit currents, 3I0 earth faults, IEC 60255 protection, breaker failure
- [x] Select-before-operate control, training actions, redaction of ground truth, versioned definitions (memory + PostgreSQL)
- [x] HMI: overview, live single-line diagram with faceplates, feeders, transformers, switches, loads, protection, alarms, measurements, instructor, scenarios, rules
- [x] Network editor with catalog, validation, versions and templates (default, urban cable, rural overhead)
- [x] IEC 60870-5-104 server (GI, spontaneous data, select/execute commands, live address map)
- [x] 10 grid detection rules and 8 grid attack scenarios that work on any drawn network
- [x] ISA-101 refresh of the water HMI

## Phase L — Dispatcher workspaces
- [x] Dispatcher contracts, design tokens and per-area stylesheets
- [x] Grid dispatcher backend: safety tags with command enforcement and a reported-value historian
- [x] Full-viewport dispatcher shell (top bar, alarm bar, status bar, quick switcher)
- [x] Grid workspace on `/grid/diagram`: `<GridSld>` single-line canvas, control window and bottom dock (alarms, events, commands, trend, tags)
- [x] Water P&ID workspace on `/diagram` (ISA-5.1 symbols, minimap, context menu, bottom dock)
- [x] Two operator-review polish rounds for control-room readability; temporary `/grid/sld-preview` route removed

## Verification log
- 2026-11-21, first commit (Phase B-D): `dotnet build scada.slnx -c Release` with 0 warnings; 246 .NET tests (12 architecture,
  155 unit, 79 integration) and 3 Node frontend tests pass. All 14 scenarios end as Detected, and 600 simulated seconds of
  normal operation produce no detections.
- 2026-11-21, second commit (review and live-run fixes): 263 .NET tests (12 architecture, 166 unit, 85 integration) and 3 frontend tests
  pass. Live API: 14/14 scenarios Detected back to back over HTTP, Modbus TCP read verified, dashboard verified in a browser.
- 2026-11-21, Step 3 (EF Core + PostgreSQL): 284 .NET tests (12 architecture, 166 unit, 86 integration, 20 database on Testcontainers
  PostgreSQL) and 3 frontend tests pass. Live API on the compose PostgreSQL: 14/14 scenarios Detected; runs, incidents, events and
  telemetry survive a restart; a fresh database migrates without warnings.
- 2026-11-21, Step 4 (auth and observability): 322 .NET tests (12 architecture, 172 unit, 118 integration, 20 database) and 3 frontend
  tests pass. Live: dev-account form sign-in, CSRF enforcement, 403 for low roles, correlation ids, no secrets in logs, 14/14 scenarios
  Detected. Keycloak and the Aspire dashboard were not started (low disk).
- 2026-11-21, Step 5 (OPC UA): Release build with zero warnings and 312 passing tests (12 architecture, 178 unit,
  122 integration). Integration coverage includes subscriptions, operator redaction, command authorization and auditing,
  certificate trust, loopback binding and shutdown.
- 2026-11-21, Steps 5–6 integrated on `feature/hmi` (review fixes, OPC UA, HMI foundation and pages): 403 .NET tests
  (13 architecture, 190 unit, 144 integration, 30 bUnit frontend, 27 database on Testcontainers PostgreSQL) pass. OPC UA review
  findings fixed (anonymous access and certificate auto-accept are opt-in, guarded update loop, bounded identity validation,
  start/stop race). Live: dev-admin sign-in, every HMI page renders in a real browser, live trends, and the Command replay
  scenario started from /scenarios through the confirmation dialog ends Detected. Found and fixed live: blazor.web.js was not
  served (404) and the CSP blocked `<base href>`, so the HMI was blank in a browser although bUnit passed.
- 2026-11-21, Step 7 and the Steps 3–6 review fixes: Release build with zero warnings; 520 .NET tests (13 architecture, 223 unit,
  149 integration, 40 bUnit, 30 database, 51 protocol, 6 performance, 8 Playwright E2E) pass on macOS; `dotnet format
  --verify-no-changes` is clean; `scripts/smoke.sh` passes against the compose PostgreSQL stack (scenario Detected).
  Measured (Linux container): 2,000 simulation steps in 1.02 s, detection 45.8 µs/step, Modbus p95 0.72 ms for
  50 clients x 200 reads, REST reads about 7,000 requests/s.
- 2026-11-22, grid mode: 963 .NET tests (13 architecture, 534 unit, 191 integration, 106 bUnit, 72 protocol, 6 performance,
  32 database, 9 Playwright E2E) pass on macOS; `dotnet format --verify-no-changes` clean; `scripts/smoke.sh` passes; CI green on
  all seven jobs. Live: grid sign-in, single-line diagram, select-before-operate opening and closing of a feeder breaker with
  loss-of-supply alarms and recovery. A normal simulated day produces zero grid detections and all 8 grid scenarios end Detected.
- 2026-11-22, dispatcher workspaces: Release build with zero warnings; 1,160 .NET tests (13 architecture, 558 unit,
  198 integration, 271 bUnit, 73 protocol, 6 performance, 32 database, 9 Playwright E2E) pass on macOS; `dotnet format
  --verify-no-changes` clean; CI green on `main`.
