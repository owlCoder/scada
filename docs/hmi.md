# Human-machine interface

The HMI uses authenticated Blazor interactive server components. Open `/account/login` first; every HMI page requires Viewer or higher, except `/accounts`, which requires Administrator. Roles are hierarchical: an Administrator is the instructor; an Operator is a trainee who can operate; a Viewer observes.

## Pages and permissions

| Route | What it shows | Available actions |
| --- | --- | --- |
| `/` | Tank levels, active alarm/open incident counts, simulation time and current scenario | Operator: pause/resume simulation |
| `/diagram` | Live SVG tanks, pumps, valves, pipes and selected device details | Everyone: select devices and follow detail links |
| `/tanks` | Levels, volume/capacity, LL/L/H/HH thresholds, overflow and cumulative overflow | Read only |
| `/pumps` | State, mode, speed, flow, pressure, health and interlock reason | Operator: start/stop, Manual/Automatic, reset a latched interlock |
| `/valves` | Role, position/setpoint, mode, flow, health and interlock | Operator: set position 0–100%, Manual/Automatic |
| `/sensors` | Device, reported value/unit, quality and health | Instructor additionally sees true value, injected fault, spoofing and drift; read only |
| `/controllers` | PLC/RTU state, communications, scan/failure counters, errors, setpoints/forces | Instructor: start/stop/reset controller, release forces |
| `/communications` | Link path/state, latency/loss, lost/timed-out counts and blocked attempts | Instructor: connect/disconnect, edit impairment |
| `/registers` | Unit/controller selector, holding/input images, tags, values, forced flags; optional all-address view | Operator: edit holding setpoints 100–199 only when register writes are enabled |
| `/alarms` | Current/cleared alarm records and latest 50 history transitions | Operator: acknowledge an unacknowledged alarm |
| `/trends` | Up to three distinct reported tags, 240 live samples per tag, UTC time axis | Everyone: select tags; one shared value scale, possibly mixed units |
| `/events` | Paged events with category, severity, source, actor, device, message and correlation ID | Everyone: category/actor/message filters and paging |
| `/incidents` | Paged incident number/title, category, state, severity, priority, assignee and UTC creation time | Everyone: status/severity/search filters, active-only filter, sort and paging |
| `/incidents/{Id:guid}` | Incident details, evidence, timeline, comments, version and resolution notes | Operator: valid status transitions, assignment, priority and comments while permitted by lifecycle |
| `/scenarios` | Catalog, expected detections, current progress/action log, latest 50 runs/outcomes | Instructor: start, pause/resume, stop, reset environment and replay a completed/stopped run |
| `/accounts` | Local accounts and detection rule settings | Instructor: role/enable changes, rotate password/key, revoke key, tune rules |
| `/identity` | Signed-in name/role, plant view and operation permission | Everyone: sign out |
| `/health` (browser HTML) | Storage probe, queue counters, simulation/subscriber count and detection metrics | Everyone: check storage |

`/health` without an HTML Accept header is the public JSON probe, not this page. There is no separate detection-list HMI page; use incident evidence, rule metrics and the detection API.

## Confirming changes

Pump commands/mode/interlock reset, valve position/mode, controller controls, network changes, register writes, all incident edits, all scenario controls, account changes and rule saves open a confirmation dialog. The action executes only after **Confirm**; **Cancel** closes it without execution. The dialog disables buttons while running, displays the result and refreshes the page afterward. Confirmation is a UI workflow, not a REST requirement.

Overview pause/resume and alarm acknowledgement execute directly. Read filters, device selection and health probes do not need confirmation. Credential rotation shows the new secret in a separate dialog only once; copy it securely before selecting Done.

Plant commands still enforce modes, interlocks, rate limits and reachable controller/field paths after confirmation. A valve travels to its setpoint at its stroke rate. Incident changes capture the displayed version; a concurrent change produces a conflict and refreshed details to review before retrying.

## Live updates and identity

Components subscribe to application topics and refresh from services, normally at most every 250 ms; alarms and health use one second. Queues hold 64 messages and drop oldest messages for slow consumers. This is a current-state display, not guaranteed delivery of every transition. Events page one follows new and persisted events; later pages stay stable until navigation/filtering. Incident pages follow updates while preserving filters and paging. Scenarios reload history when the current run ID/state changes. Accounts/rules load on entry and after local actions rather than subscribe to live changes.

Health storage checks run on entry and, when messages trigger refreshes, at most every 15 seconds, with a two-second timeout. The Check storage button requests a fresh probe. If the simulation is paused, there may be fewer refresh triggers.

Blazor circuits require a live connection; reconnect/reload after an interruption to refresh state. The external SignalR hub is documented separately in [API](api.md). Local password account identity is revalidated every minute within HMI circuits; a role change also invalidates that circuit identity. HTTP password-cookie requests validate each time and adopt role changes. OIDC circuit identity is not rechecked against the local account store. Session expiry, disablement or password changes require sign-in again.

## Redaction

For Viewers and Operators, `PlantViewRedactor` replaces sensor true values with reported values, clears sensor fault/drift/spoof fields and derives tank level/volume from the reported level sensor. Sensor health becomes Healthy for good quality or Warning otherwise. Administrators receive the original plant snapshot.

This is limited plant-view redaction, not an instructor/trainee secrecy boundary across all data: actuator faults, interlocks/overflow, controller forced-register metadata, scenario action logs, rule settings, events and incident evidence remain visible where the page/API permits them. Trends and telemetry contain reported measurements for every role. OPC UA uses operator measurements even for Administrators.
