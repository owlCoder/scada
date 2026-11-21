# Grid HMI and session mode

The sign-in form offers **Water treatment plant** (default) and **Electrical distribution grid**. Password sign-in stores the `SimulationMode` name in `ScadaClaimTypes.Mode` (`scada:mode`). Older sessions and OIDC sign-ins default to Water. Cookie role renewal preserves mode. The revalidating circuit provider still validates credentials and role; a mode change uses a full browser POST and creates a fresh circuit.

The top bar posts an antiforgery-protected, authenticated `/account/mode` request. It toggles mode using the existing cookie ticket, preserves the original session start/credential/issued claims, and redirects to `/` or `/grid`. It does not extend the absolute session lifetime. API keys alone cannot create a mode cookie. `/` redirects Grid sessions to `/grid`; existing water routes and controls retain their behavior. Navigation changes with mode, with events, incidents, scenarios (water scenarios for now), accounts, health and my account shared.

## Pages

All grid data pages inherit `GridPageBase` → `ConfirmedComponentBase` → `RealtimeComponentBase`, subscribe to `Grid`, `GridAlarms` and `GridStatus`, and read through `IGridView` and the single `HmiSession.GridSnapshot()` redaction boundary.

| Route | Purpose |
| --- | --- |
| `/grid` | Load, generation, import, losses, supplied customers, alarm priorities, transformer loading, voltage ranges and feeder cards |
| `/grid/diagram` | Live SVG single-line diagram generated from definition positions, terminals and line waypoints; mouse/touch pan, wheel/button zoom, keyboard selection and faceplates |
| `/grid/feeders` | Branch currents, power, losses and loading bars |
| `/grid/transformers` | Transformer loading, taps and AVR faceplates |
| `/grid/switches` | Switch state, selections, last operator/cause and faceplates |
| `/grid/loads` | Demand, supplied customers and load faceplates |
| `/grid/protection` | Relay settings, pickup/trip state; confirmed instructor setting changes |
| `/grid/alarms` | Priority, simulation time, active/returned and acknowledged/unacknowledged state; confirmed acknowledgement |
| `/grid/measurements` | RTU reported telemetry and quality; instructor-only true values and spoof indicators |
| `/grid/instructor` | Administrator-only fault injection/clear, spoof/clear, RTU offline/online, forced switch/tap and restoration of training defaults |
| `/grid/editor` | Administrator-only network draft editor and version administration |

Water and grid share a calm grey shell, tables and cards. The persistent top-bar banner shows the newest three unacknowledged alarms in the selected mode. Grid voltage levels use heavy/medium/fine strokes rather than colors. De-energized sections are dimmed and dashed. Breakers use filled/hollow squares, disconnectors and load-break switches use distinct contact symbols, transformers use two circles and a tap label, and loads/PV/capacitors/sources have dedicated symbols.

Faceplates use `IGridControl` with `Session.Actor` and source `hmi`. Every service mutation, including switch selection/cancellation, requires `ConfirmDialog`. Select open/close → confirm reserves a switch; Execute → confirm operates it. Cancel selection → confirm releases it. A visible **10-second local execution window** is deliberately independent of the service's authoritative ownership/expiry/interlock checks. The contracts do not expose selection expiry or last-operation timestamps; the HMI shows its local countdown and the available last operator/cause. An externally-created selection must be reselected in this circuit before execution. Transformer tap/AVR, load shed/restore, capacitor steps and protection reset follow the same confirmation flow. Instructor mutations use `IGridTraining`; services remain responsible for role enforcement.

## Redactor integration across worktrees

This branch contains contracts but not Agent A's grid services or `Application.Grid.GridViewRedactor`. No engine, training implementation or production placeholder redactor is supplied here. Grid pages show an unavailable state until grid interfaces are registered by Agent A. Operator grid reads fail closed if the shared redactor is missing. `HmiSession` resolves the shared static `GridViewRedactor.ForOperators(GridSnapshot)` delegate from the Application assembly once; after merge it uses that implementation automatically. This temporary late binding can be replaced by the direct method reference after the shared class is present. Only frontend tests inject `HmiFakeGridRedactor` and `HmiFakeGrid*` services. The production `GridHmiRegistrationExtensions` registers only the equipment catalog and adds one line to the existing registration chain.

## Editing a network

1. Start from the active definition, load a saved version into the draft, or confirm **Start empty**. Draft changes never mutate the active definition. Replacing/deleting drafts requires confirmation.
2. Click a palette item to add it, or drag it to the canvas. Move nodes with pointer dragging; coordinates snap to whole grid units (40 SVG units per grid unit). Position fields provide a keyboard alternative.
3. Adding a line/cable/transformer/switch starts terminal connection mode. Click the first bus terminal, then the second bus for a line/transformer or an adjacent element for a switch. Existing lines/transformers/switches can be reconnected with **Connect selected**. Use the element list for line terminals and keyboard-friendly selection. **Cancel connection** leaves the draft's current connections intact.
4. Select a line, enable **Add waypoints**, and click the canvas to add snapped waypoints. The property panel can clear waypoints. Deletion leaves dangling references visible to validation rather than silently deleting related equipment.
5. Edit equipment properties and RTU assignments. The catalog includes typical cable and overhead conductor R/X/B/current values, 20/0.4 kV transformers at 250/400/630/1000 kVA, 110/20 kV transformers at 20/31.5/40 MVA, and residential/commercial/industrial/constant load profiles. These are illustrative training values, not certified design data.
6. Every edit calls `IGridDefinitionAdministration.Validate`. Validation messages remain visible and block save. Enter a version comment, choose **Save new version**, and confirm; this calls `SaveAsync` with the actor.
7. The version list shows version, author, creation time, comment and active state. **Activate version** explicitly confirms that it **restarts the grid simulation**, then calls `IGridRuntime.ActivateVersionAsync`. Saving alone does not activate.

The only JavaScript is the first-party ES module `_content/Hmi/grid-pointer.js`, imported via Blazor interop. It provides pointer capture/release and client-to-SVG coordinate transforms only. Draft, connection, dragging, viewport and command state stay in C#. There is no CDN, inline script, inline style or CSP relaxation. Pointer modules and realtime subscriptions are disposed on navigation.

## Validation

`Tests/FrontendTests/Support/HmiTestHost.cs` registers all grid interface fakes. Tests cover every grid page, mode navigation/root redirect/top-bar form, operator truth isolation, live refresh/disposal, diagram symbols/values, confirmed SBO selection/execution/cancellation/expiry, taps/AVR/load/capacitor/protection commands, alarm acknowledgement, instructor access, editor terminal connection/validation/catalog/save/activation. Integration tests exercise mode form defaults/failure retention, mode claims/redirects, legacy and OIDC defaults, invalid modes, CSRF, authentication, role renewal and original session lifetime preservation.

Build with `dotnet build scada.slnx -c Release --disable-build-servers` and run ArchitectureTests, UnitTests, IntegrationTests, FrontendTests and ProtocolTests using `dotnet test Tests/<Project> -c Release --no-build`. DatabaseTests require Docker and EndToEndTests require Playwright Chromium. In a restricted workspace, set `NUGET_HTTP_CACHE_PATH` to a writable cache directory before restore/build/format.
