# Grid HMI and session mode

The sign-in form offers **Water treatment plant** (default) and **Electrical distribution grid**. Password sign-in stores the `SimulationMode` name in `ScadaClaimTypes.Mode` (`scada:mode`). Older sessions and OIDC sign-ins default to Water. Cookie role renewal preserves mode. The revalidating circuit provider still validates credentials and role; a mode change uses a full browser POST and creates a fresh circuit.

The top bar posts an antiforgery-protected, authenticated `/account/mode` request. It toggles mode using the existing cookie ticket, preserves the original session start/credential/issued claims, and redirects to `/` or `/grid`. It does not extend the absolute session lifetime. API keys alone cannot create a mode cookie. `/` redirects Grid sessions to `/grid`; existing water routes and controls retain their behavior. Navigation changes with mode, with events, incidents, scenarios (water scenarios for now), accounts, health and my account shared.

## Pages

All grid data pages inherit `GridPageBase` → `ConfirmedComponentBase` → `RealtimeComponentBase`, subscribe to `Grid`, `GridAlarms` and `GridStatus`, and read through `IGridView` and the single `HmiSession.GridSnapshot()` redaction boundary.

| Route | Purpose |
| --- | --- |
| `/grid` | Load, generation, import, losses, supplied customers, alarm priorities, transformer loading, voltage ranges and feeder cards |
| `/grid/diagram` | **Dispatcher workspace**: the `<GridSld>` canvas fills the area; right dock = control window for the selected element; resizable bottom dock with Alarms / Events / Commands / Trend / Tags tabs |

| `/grid/diagram` | Live SVG single-line diagram generated from definition positions, terminals and line waypoints; mouse/touch pan, wheel/button zoom, keyboard selection and faceplates (being replaced by the dispatcher workspace that hosts the `<GridSld>` canvas) |
| `/grid/sld-preview` | Temporary dev route rendering only the `<GridSld>` canvas full-screen (may be removed once the workspace lands) |
| `/grid/feeders` | Branch currents, power, losses and loading bars |
| `/grid/transformers` | Transformer loading, taps and AVR faceplates |
| `/grid/switches` | Switch state, selections, last operator/cause and faceplates |
| `/grid/loads` | Demand, supplied customers and load faceplates |
| `/grid/protection` | Relay settings, pickup/trip state; confirmed instructor setting changes |
| `/grid/alarms` | Priority, simulation time, active/returned and acknowledged/unacknowledged state; confirmed acknowledgement |
| `/grid/measurements` | RTU reported telemetry and quality; instructor-only true values and spoof indicators |
| `/grid/instructor` | Administrator-only fault injection/clear, spoof/clear, RTU offline/online, forced switch/tap and restoration of training defaults |
| `/grid/scenarios` | Grid attack-scenario catalog, current run with progress and action log, run history with outcomes and expected vs observed detections; Administrators start/pause/resume/stop/reset/replay, each confirmed (driven by `GridScenarioEngine`) |
| `/grid/rules` | Administrator-only grid detection rule settings and execution metrics, edited with validation like the water rules editor (driven by `GridDetectionEngine` / `GridDetectionRuleAdministrationService`) |
| `/grid/editor` | Administrator-only network draft editor and version administration |

Water and grid share a calm grey shell, tables and cards. In grid mode the nav shows the grid pages plus **Scenarios** (pointing at `/grid/scenarios`) and, for Administrators, **Detection rules** (`/grid/rules`); events, incidents, health and my account stay shared. The persistent top-bar banner shows the newest three unacknowledged alarms in the selected mode. Grid voltage levels use heavy/medium/fine strokes rather than colors. De-energized sections are dimmed and dashed. Breakers use filled/hollow squares, disconnectors and load-break switches use distinct contact symbols, transformers use two circles and a tap label, and loads/PV/capacitors/sources have dedicated symbols.

## Single-line diagram canvas (`GridSld`)

`<GridSld>` (parameters unchanged: `Snapshot`, `Definition`, `Tags`, `SelectedId`/`SelectedIdChanged`, `OnAction`, `CanOperate`) is the dispatcher canvas that the grid workspace hosts full-screen; `/grid/sld-preview` renders it standalone for development. It fills its container on the dark `--c-canvas` background and keeps all state in C#:

- **Geometry** — `SldModelBuilder` turns the definition into static SVG geometry once per definition: busbars as thick bars, line polylines through waypoints and inline switches, bus→element stubs, one dashed substation frame per RTU (`SldFramesLayer`, re-rendered only when the definition changes). Dynamic per-tick state comes from `SldStateBuilder` into `SldElementState` records, so each `<SldElementG @key>` re-renders only when its own state record changes.
- **States** — closed switches are filled with the voltage colour (`--c-kv-*`), open ones hollow with `--c-open`, bad/unknown quality `--c-invalid` with "?"; de-energised sections `--c-dead`; quality flags U/B from reported measurements only (never spoof indicators). View modes: *Voltage levels* (default), *Energisation*, *Loading* (heat scale `--c-load-*`, also as a toggleable overlay). Energised branches show flow arrows in the measured power direction.
- **LOD** — zoom decides detail: low shows element ids of busbars/feeders, medium shows names, high shows measurement value boxes (kV at buses, A/% at branches, MW at loads, tap at transformers) placed by `SldLabelPlacer`, a greedy slot ring that guarantees no two boxes overlap.
- **Markers** — tag badges from `Tags` (DNO red, Earthed green earth glyph, WIP amber, "i"), protection-tripped flag, alarm bell in the priority colour blinking while unacknowledged, and a `--c-focus` halo on the selection.
- **Interaction** — drag empty space to pan, wheel zooms around the cursor, `+`/`−`/`0`/arrows/Tab/Enter/ContextMenu/Escape keyboard control, zoom buttons and *Fit* in the top-right toolbar, minimap (bottom-left, click/drag to navigate), search box and substation/feeder quick list (top-left, zooms to and selects the element). Hover shows a tooltip; click selects (`SelectedIdChanged`); double-click raises `OnAction(OpenControl)`; right-click opens a role-aware context menu (`SelectOpen`/`SelectClose`/`PlaceTag`/`ShowTrend`/`ShowDetails`/`OpenControl` — viewers get read-only items only).
- **JavaScript** — only the first-party ES module `_content/Hmi/js/sld.js`: pointer capture, client↔SVG transforms, overlay placement via the CSSOM (no inline styles) and a resize observer reporting the container size. Without it (tests, prerender) the canvas falls back to centre-based zoom and fixed-size minimap math.

Faceplates use `IGridControl` with `Session.Actor` and source `hmi`. Every service mutation, including switch selection/cancellation, requires `ConfirmDialog`. Select open/close → confirm reserves a switch; Execute → confirm operates it. Cancel selection → confirm releases it. A visible **10-second local execution window** is deliberately independent of the service's authoritative ownership/expiry/interlock checks. The contracts do not expose selection expiry or last-operation timestamps; the HMI shows its local countdown and the available last operator/cause. An externally-created selection must be reselected in this circuit before execution. Transformer tap/AVR, load shed/restore, capacitor steps and protection reset follow the same confirmation flow. Instructor mutations use `IGridTraining`; services remain responsible for role enforcement.


## Dispatcher workspace (`/grid/diagram`)

`/grid/diagram` is a full-viewport workspace, not a page with side panels. It hosts the `<GridSld>` canvas as-is
(`Snapshot` from `HmiSession.GridSnapshot()`, `Definition`, `Tags` from `IGridTagging`, two-way `SelectedId`, `OnAction`,
`CanOperate`) and wires its `OnAction` requests to the docks:

- `OpenControl` selects the element and opens the right dock; `SelectOpen`/`SelectClose` select the element, open the
  control window and start the select-before-operate flow (confirmation dialog included); `ShowTrend` opens the bottom
  dock on the Trend tab pre-selecting that element's measurements; `PlaceTag` opens the tag form; `ShowDetails` opens the
  Details tab of the control window.
- Right dock (`--w-dock-right`, collapsible) = **control window** (`GridControlWindow`, replaces the old `GridFaceplate`,
  which the switches/loads/transformers pages now also use): header with element id, name, type icon, state badge and tag
  badges, Control/Details tabs, and per element type — switch (state, last operation, SBO buttons with a countdown bar of
  `Grid:SelectTimeoutSeconds − 2`, Execute/Cancel, disabled with the blocking reason when a `DoNotOperate`/`Earthed`/
  `WorkInProgress` tag is on the switch or its connected line/transformer, or when the owning RTU has an active
  communication-loss alarm; protection relays on the breaker with Reset), transformer (tap gauge with min/max/neutral,
  AVR toggle, Raise/Lower, loading bar, HV/LV voltages), line/cable (current vs rating, loading bar, P/Q at both ends,
  energised), load (demand, customers supplied/total, shed/restore), capacitor (step stepper), bus (voltage vs pu limits),
  generator (P/Q, connected) — plus the element's reported measurements with quality and a 60-point mini sparkline from
  `IGridHistory`, and the tag list with place/remove (kind + reason, each confirmed through `IGridTagging`).
- Bottom dock (`--h-dock-bottom`, collapsible, drag-resizable via the `js/workspace-dock.js` module) with tabs:
  **Alarms** (ISA-18.2 table with `--c-p1..p4` priority pills, blinking unacknowledged rows, acknowledge single with
  confirmation via `IGridControl`, row click selects + centres the element), **Events** and **Commands** (grid-source
  events from `IEventStore`, newest first, live on the `events` topics; Commands keeps the Audit/CommandRejected/
  AuthorizationDenied categories = the supervisory command log with actor, channel and result), **Trend** (SVG multi-series
  chart, up to 4 measurements from `IGridHistory`, time axis in simulated time), **Tags** (all active tags, remove with
  confirmation).
- Keyboard (forwarded by the JS module): `Esc` deselects, `Enter` opens the control window for the selection, `Del` cancels
  a running selection countdown. Dock collapsed flags, bottom dock height and the active tab are remembered per user in
  `localStorage` by the JS module (all state in C#, JS only forwards geometry and persists the layout).
- `IGridTagging`/`IGridHistory` are resolved optionally (`GetService`); when they are not registered the tag/trend sections
  are hidden instead of failing. The only JavaScript is the first-party ES module `_content/Hmi/js/workspace-dock.js`.

## Redactor integration

All parts are merged on `main`: the real `GridEngine`/`GridCoordinator`/`IGridControl`/`IGridTraining` are registered by
`GridRegistrationExtensions`, and `HmiSession.GridSnapshot()` calls `GridViewRedactor.ForOperators(snapshot, definition)`
directly for everyone who is not an Administrator — so operators see reported telemetry only, including the
measurement-derived totals (import/losses). The frontend tests inject `HmiFakeGrid*` services plus the real
`GridScenarioEngine`/`GridDetectionEngine`/`GridDetectionRuleAdministrationService` over them, so `/grid/scenarios` and
`/grid/rules` are covered by bUnit tests. The shared incidents page lists grid incidents like any other (grid detection
raises them through the same `IncidentService`).

## Editing a network

1. Start from the active definition, load a saved version into the draft, or confirm **Start empty**. Draft changes never mutate the active definition. Replacing/deleting drafts requires confirmation.
2. Click a palette item to add it, or drag it to the canvas. Move nodes with pointer dragging; coordinates snap to whole grid units (40 SVG units per grid unit). Position fields provide a keyboard alternative.
3. Adding a line/cable/transformer/switch starts terminal connection mode. Click the first bus terminal, then the second bus for a line/transformer or an adjacent element for a switch. Existing lines/transformers/switches can be reconnected with **Connect selected**. Use the element list for line terminals and keyboard-friendly selection. **Cancel connection** leaves the draft's current connections intact.
4. Select a line, enable **Add waypoints**, and click the canvas to add snapped waypoints. The property panel can clear waypoints. Deletion leaves dangling references visible to validation rather than silently deleting related equipment.
5. Edit equipment properties and RTU assignments. The catalog includes typical cable and overhead conductor R/X/B/current values, 20/0.4 kV transformers at 250/400/630/1000 kVA, 110/20 kV transformers at 20/31.5/40 MVA, and residential/commercial/industrial/constant load profiles. These are illustrative training values, not certified design data.
6. Every edit calls `IGridDefinitionAdministration.Validate`. Validation messages remain visible and block save. Enter a version comment, choose **Save new version**, and confirm; this calls `SaveAsync` with the actor.
7. The version list shows version, author, creation time, comment and active state. **Activate version** explicitly confirms that it **restarts the grid simulation**, then calls `IGridRuntime.ActivateVersionAsync`. Saving alone does not activate.

The only JavaScript is the first-party ES modules `_content/Hmi/grid-pointer.js` (editor) and `_content/Hmi/js/sld.js` (diagram canvas), imported via Blazor interop. They provide pointer capture/release, client-to-SVG coordinate transforms, overlay placement and resize observation only. Draft, connection, dragging, viewport and command state stay in C#. There is no CDN, inline script, inline style or CSP relaxation. Pointer modules and realtime subscriptions are disposed on navigation.

## Validation

`Tests/FrontendTests/Support/HmiTestHost.cs` registers all grid interface fakes plus the real grid security services. Tests cover every grid page (including `/grid/scenarios` and `/grid/rules`: role gating, confirmation dialogs, catalog/current-run/history rendering, rule editing with validation), mode navigation/root redirect/top-bar form, operator truth isolation, live refresh/disposal, diagram symbols/values, confirmed SBO selection/execution/cancellation/expiry, taps/AVR/load/capacitor/protection commands, alarm acknowledgement, instructor access, editor terminal connection/validation/catalog/save/activation, and grid incidents appearing on the shared incidents page. Integration tests exercise mode form defaults/failure retention, mode claims/redirects, legacy and OIDC defaults, invalid modes, CSRF, authentication, role renewal and original session lifetime preservation. `Tests/EndToEndTests/Hmi/GridHmiBrowserTests.cs` drives the whole flow in a real browser: sign in choosing Grid → overview totals → diagram → confirmed select-before-operate open/close of a feeder breaker with customers-supplied drop, loss-of-supply alarm and recovery → editor page loads for an administrator and denies an operator.

Build with `dotnet build scada.slnx -c Release --disable-build-servers` and run ArchitectureTests, UnitTests, IntegrationTests, FrontendTests and ProtocolTests using `dotnet test Tests/<Project> -c Release --no-build`. DatabaseTests require Docker and EndToEndTests require Playwright Chromium. In a restricted workspace, set `NUGET_HTTP_CACHE_PATH` to a writable cache directory before restore/build/format.
