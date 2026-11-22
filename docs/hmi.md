# Human-machine interface

The HMI uses authenticated Blazor interactive server components. Open `/account/login` first; every HMI page requires Viewer or higher, except `/accounts`, which requires Administrator. Roles are hierarchical: an Administrator is the instructor; an Operator is a trainee who can operate; a Viewer observes.

## Dispatcher shell

Every page runs inside a full-viewport dispatcher shell (`Components/Layout/MainLayout.razor`, styled by `wwwroot/css/shell.css` with colours only from `css/tokens.css`). The shell itself never scrolls; the content area does. From top to bottom:

- **Top bar** (`--h-topbar`, `TopBar.razor`): system and plant/grid name, mode badge plus the existing `POST /account/mode` switch form, simulation state (Running/Paused) with simulated clock and wall clock, communication health of the current mode (healthy RTUs for grid, healthy links for water, from the mode's snapshot), signed-in user and role, and a `POST /account/logout` sign-out form.
- **Alarm bar** (`--h-alarmbar`, `AlarmBar.razor`): unacknowledged-alarm counts per ISA-18.2 priority (P1–P4, token colours `--c-p1..p4`) and the newest unacknowledged alarms of the current mode, each coloured by priority and blinking (`blink-unack`) while unacknowledged. Operators additionally get an **Acknowledge** button per item and **Acknowledge all visible**; both go through the existing services (`AlarmService` in water mode, `IGridControl` with `GridCommandType.AcknowledgeAlarm` in grid mode) and the standard `ConfirmDialog` — Viewers see the alarms but no buttons. Clicking an item selects it (`ShellState.SelectedAlarmKey`) and opens the alarm list display (`/alarms` or `/grid/alarms`).
- **Icon rail** (`--w-rail`, `Rail.razor`, replacing the old 220 px sidebar): slim icon+label navigation grouped into Process, Operations, System and Administration (Administration only for Administrators), mode-aware (water displays vs grid displays) from `DisplayCatalog`. The main content fills the rest of the window.
- **Status bar** (`--h-statusbar`, `StatusBar.razor`): Blazor circuit connection state (reported by `js/shell.js`), age of the last realtime update, the element the current page selected (pages publish it through the scoped `ShellState.Selected`), keyboard hints and a `?` help button.

Workspace displays — `/diagram` (water) and `/grid/diagram` (grid) — get the full content area with no padding (`content-workspace`, detected by route in `MainLayout`); all other pages keep their normal padding.

**Quick switcher and shortcuts.** `Ctrl+K` opens the quick switcher (`Components/Shared/QuickSwitch.razor`): type to filter displays, and in grid mode also element ids of the network definition; choosing an element publishes `ShellState.RequestFocus(id)` (so the diagram workspace can zoom to it) and navigates to the diagram. `F2` opens the alarm list, `F3` the diagram, `Esc` closes overlays, `Ctrl+Shift+A` acknowledges the selected alarm (Operator, with confirmation), `?` toggles the shortcut help overlay (`ShortcutHelp.razor`). Keys are forwarded by the small first-party ES module `wwwroot/js/shell.js` through JS interop (`registerKeys`, `watchConnection`); all shortcut decisions and state stay in C# (`MainLayout.HandleKey`). Without JS the shell works normally, minus shortcuts and the live connection indicator.

`ShellState` (`Hmi/Services/Shell/ShellState.cs`, scoped per circuit) is the small shared channel between shell and pages: `Selected` (element selected on the current page, shown in the status bar), `RequestFocus`/`FocusRequested` (quick-switcher element focus), `SelectedAlarmKey`, `LastUpdateAt`/`Touch`, `Connection` and the `Changed` notification event.

## Look and feel (ISA-101)

Water pages follow the same high-performance style as the grid pages (see [hmi-grid.md](hmi-grid.md)): a calm dark-grey palette where colour is reserved for abnormal states, the persistent alarm bar, and consistent cards, tables, page headers, forms and empty states. Styling is split per area: the dispatcher shell in `wwwroot/css/shell.css`, the single-line diagram in `css/sld.css`, the grid docks in `css/dock.css`, the water P&ID in `css/pid.css`, and page-level styles in clearly separated sections of `src/Hmi/wwwroot/hmi.css` — the base theme and grid sections at the top, and a marked `WATER HMI — ISA-101 refresh` section at the end that only re-declares water-specific selectors or introduces water-prefixed class names, so grid pages are unaffected. There are no inline styles or scripts.

The plant diagram is a full-area ISA-101 / ISA-5.1 P&ID dispatcher workspace (`/diagram`, styled entirely in `src/Hmi/wwwroot/css/pid.css` with `pid-`-prefixed classes; colours only from `tokens.css`). The SVG canvas fills the workspace area on dark `--c-canvas`: drag to pan, mouse wheel zooms at the cursor, the toolbar has zoom in/out and Fit, and a minimap in the corner shows the whole plant with the viewport rectangle (click to centre). Keyboard: `+`/`−` zoom, `0` fits, arrow keys pan, `1`–`4` switch the bottom dock tabs, `Esc` closes the context menu or clears the selection. Process lines are grey with animated dashes and direction arrows while flowing; tanks show level fill and LL/L/H/HH limit marks; pumps are ISA circles (arrow = running, stop bars = stopped, red "!" = faulted); valves are bow-ties with a stem and position %; instruments are ISA bubbles (LT/FT/PT) connected to their process point with reported value, unit and quality (`--c-invalid` when not Good); PLC/RTU stations are boxes with field/supervisory comms state; active alarms are bells coloured by priority (`--c-p1..p4`, blinking while unacknowledged). Labels sit in fixed slots per symbol and the level of detail drops with zoom (values and instruments fade out) so nothing overlaps. Right-click a device for a role-aware context menu (Start/Stop, Auto/Manual, Set position…, Trend, Details — viewers get Trend/Details only); double-click or click opens the control window. All viewport state stays in C#; the only JavaScript is the small `wwwroot/js/pid.js` ES module (pointer capture and client-to-canvas coordinate mapping) loaded through Blazor JS interop.

The right control window (`PlantFaceplate`) covers every selectable element: tank (level gauge with limits, volume, inflow/outflow, overflow), pump (state, mode, speed, flow, pressure, health, interlock; Start/Stop/Manual/Automatic/Reset interlock for operators), valve (position/setpoint with number input and slider, mode, flow; Set position and mode for operators), instrument (reported value/unit, quality, health; true value/fault/drift for instructors only) and controller (state, scan, comms, failures, actuators/sensors). Every device window also shows a sparkline trend of its primary tag from `TelemetryHistorian`. All state changes go through `ConfirmDialog` and the existing `ControlCommandService`; viewers are read-only. Operators see only reported values (redaction unchanged).

The bottom dock has four tabs: **Alarms** (ISA-18.2 table with priority colours, blinking unacknowledged rows, acknowledgement via `AlarmService` after confirmation, unacknowledged-count badge on the tab), **Events** (live recent events), **Commands** (accepted/rejected operator commands from the event log, source `Hmi`) and **Trend** (multi-series SVG chart of up to 4 reported tags from `TelemetryHistorian`; the context menu's Trend action adds the device's tag and opens the tab).

The overview shows KPI cards per tank (level, `LevelBar`, inline-SVG sparkline of recent `TelemetryHistorian` samples — no JS), an active-alarm count, an open-incident count, and an alarm summary by priority. The alarms page presents ISA-18.2 columns: priority, raised time, duration, source, message, state (active/cleared) and acknowledgement, with unacknowledged active alarms highlighted. Layouts are responsive down to narrower widths (the workspace stacks the control window and dock). Shared presentational components live in `src/Hmi/Components/Shared` (`LevelBar` with limit/setpoint marks, `Sparkline`, `EmptyState`, `PlantFaceplate`, `StatusPill`, `PageControls`, `ConfirmDialog`, banners); water-workspace components live in `src/Hmi/Components/Pages/Water` (`PidDiagram`, `PidMinimap`, `PidContextMenu`, `PidDock` with `PidAlarmsTab`/`PidEventsTab`/`PidCommandsTab`/`PidTrendTab`).

## Pages and permissions

| Route | What it shows | Available actions |
| --- | --- | --- |
| `/` | KPI cards: tank levels with limit bars and sparklines, active alarm count, open incident count; alarm summary by priority; simulation state; current scenario | Operator: pause/resume simulation |
| `/diagram` | Full-area P&ID workspace: pan/zoom canvas with minimap, ISA symbols (tanks with LL/L/H/HH, pumps, valves, instrument bubbles, controller stations, alarm bells), level-of-detail by zoom, right-click context menu, right control window for the selected device, bottom dock with Alarms/Events/Commands/Trend tabs | Everyone: select, pan/zoom, inspect, trend; Operator: device commands and alarm acknowledgement after confirmation |
| `/tanks` | Levels, volume/capacity, LL/L/H/HH thresholds, overflow and cumulative overflow | Read only |
| `/pumps` | State, mode, speed, flow, pressure, health and interlock reason | Operator: start/stop, Manual/Automatic, reset a latched interlock |
| `/valves` | Role, position/setpoint, mode, flow, health and interlock | Operator: set position 0–100%, Manual/Automatic |
| `/sensors` | Device, reported value/unit, quality and health | Instructor additionally sees true value, injected fault, spoofing and drift; read only |
| `/controllers` | PLC/RTU state, communications, scan/failure counters, errors, setpoints/forces | Instructor: start/stop/reset controller, release forces |
| `/communications` | Link path/state, latency/loss, lost/timed-out counts and blocked attempts | Instructor: connect/disconnect, edit impairment |
| `/registers` | Unit/controller selector, holding/input images, tags, values, forced flags; optional all-address view | Operator: edit holding setpoints 100–199 only when register writes are enabled |
| `/alarms` | ISA-18.2 current alarm list (priority, raised time, duration, state, acknowledgement) and latest 50 history transitions | Operator: acknowledge an unacknowledged alarm |
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

Simulation pause/resume, alarm acknowledgement, pump commands/mode/interlock reset, valve position/mode, controller controls, network changes, register writes, all incident edits, all scenario controls, account changes and rule saves open a confirmation dialog. The action executes only after **Confirm**; **Cancel** closes it without execution. The dialog disables buttons while running, displays the result and refreshes the page afterward. Confirmation is a UI workflow, not a REST requirement.

Read filters, device selection and health probes do not need confirmation. Credential rotation shows the new secret in a separate dialog only once; copy it securely before selecting Done.

Plant commands still enforce modes, interlocks, rate limits and reachable controller/field paths after confirmation. A valve travels to its setpoint at its stroke rate. Incident changes capture the displayed version; a concurrent change produces a conflict and refreshed details to review before retrying.

## Live updates and identity

Components subscribe to application topics and refresh from services, normally at most every 250 ms; alarms and health use one second. Queues hold 64 messages and drop oldest messages for slow consumers. This is a current-state display, not guaranteed delivery of every transition. Events page one follows new and persisted events; later pages stay stable until navigation/filtering. Incident pages follow updates while preserving filters and paging. Scenarios reload history when the current run ID/state changes. Accounts/rules load on entry and after local actions rather than subscribe to live changes.

Refresh failures are logged and shown in a shared error banner; later updates retry and clear the banner after recovery. Asynchronous history/count loads are awaited and cancelled when their page is disposed. Search filters are limited to 200 characters in the UI and stores; paging is capped at page 10,000.

Health storage checks run on entry and, when messages trigger refreshes, at most every 15 seconds, with a two-second timeout. The Check storage button requests a fresh probe. If the simulation is paused, there may be fewer refresh triggers.

Blazor circuits require a live connection; reconnect/reload after an interruption to refresh state. The external SignalR hub is documented separately in [API](api.md). Local password account identity is revalidated every minute within HMI circuits; a role change also invalidates that circuit identity. HTTP password-cookie requests validate each time and adopt role changes. OIDC circuit identity is not rechecked against the local account store. Session expiry, disablement or password changes require sign-in again.

## Redaction

For Viewers and Operators, `PlantViewRedactor` replaces sensor true values with reported values, clears sensor fault/drift/spoof fields and derives tank level/volume from the reported level sensor. Sensor health becomes Healthy for good quality or Warning otherwise. Pump/valve fault fields become None, scenario actuator attribution is hidden, and interlock bypass state is hidden. Actuator health reflects observable safety trips rather than injected fault diagnoses. Independent overfill, dry-run, deadhead and pressure trips remain visible, even alongside an injected fault; fault-specific interlock diagnostics are hidden. Administrators receive the original plant snapshot.

This is limited plant-view redaction, not an instructor/trainee secrecy boundary across all data: real safety trips/overflow, controller forced-register metadata, scenario action logs, rule settings, events and incident evidence remain visible where the page/API permits them. Trends and telemetry contain reported measurements for every role. OPC UA uses operator measurements even for Administrators.
