# Implementation Progress

Durable checklist for the full SCADA platform build-out (branch `feature/full-scada-platform`).
A box is ticked only after the feature is implemented **and** verified by a build plus tests.

Baseline (before this work): 110 .NET tests + 3 Node frontend tests passing on `main` at `e95fb31`.

## Overall status (2026-11-21)

Steps 1–4 of the 7-step plan in `docs/SESSION-HANDOFF.md` are done and merged to `main` (about 60 % of the work):
scenario engine, simulation/detection/incidents/Modbus/API, PostgreSQL persistence, authentication and observability.

Remaining:
- Step 5: OPC UA server.
- Step 6: Blazor HMI and SignalR. This is the largest remaining step.
- Step 7: E2E, performance and protocol tests, Docker smoke test, CI, documentation, PR.

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
- [ ] OPC UA server simulator

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
- [ ] Blazor HMI pages
- [ ] SignalR hub and realtime broadcasting

## Phase I/J — Validation and delivery
- [ ] Database, protocol, E2E, performance, Docker smoke tests
- [ ] Documentation
- [ ] CI green, pushed

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
