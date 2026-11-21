# Security training scenarios

The [built-in catalog](../src/Security/Scenarios/Catalog/DefaultScenarios.cs) contains 14 exercises. They modify only the virtual environment or invoke platform services: they do not scan networks or attack physical devices. Catalog durations and action offsets are simulated seconds; at the default time scale 120 simulated seconds takes about 24 wall seconds, subject to execution overhead.

| ID | Duration (s) | Exercise | Expected detection rule IDs |
| --- | --- | --- | --- |
| `unauthorized-command` | 30 | Viewer attempts pump/valve commands | repeated-rejected-commands, unauthorized-access |
| `sensor-spoofing` | 120 | LT-102 freezes at reported 30% | mass-balance-inconsistency |
| `register-tampering` | 60 | Blocked enterprise probe, invalid writes and forced PLC-1 setpoints | network-segmentation-violation, invalid-register-operations, controller-state-mismatch |
| `command-replay` | 30 | Replay captured sequence-numbered start command | command-replay |
| `denial-of-service` | 60 | Loss/latency on PLC-1 paths plus command burst | communication-failure, excessive-control-requests |
| `command-frequency` | 40 | Oscillate V-301 between 20/80% | excessive-control-requests, conflicting-operator-commands |
| `role-escalation` | 30 | Operator attempts Administrator privileges/key rotation | unauthorized-access |
| `plc-communication-interruption` | 60 | Disconnect PLC-1 field I/O, reconnect and reset | communication-failure, controller-state-mismatch |
| `false-measurement-injection` | 90 | Open inlet, spoof FT-101 to zero and PT-101 to 8 bar | impossible-measurement-change, mass-balance-inconsistency |
| `controller-state-mismatch` | 40 | Force PLC-2/PLC-1 output images | controller-state-mismatch |
| `unexpected-actuator-activation` | 60 | Unattributed drain opening and standby pump start | unexpected-valve-change, unexpected-pump-activity |
| `process-safety-violation` | 90 | Near-full T-102, bypassed interlocks and forced fill/drain setpoints/pumps | process-safety-violation, abnormal-tank-level, unexpected-pump-activity |
| `data-integrity-violation` | 30 | Alter live LT-103/FT-301 historian values without their hashes | historian-integrity |
| `operator-misuse` | 60 | Generate alarms, leave devices Manual and repeatedly acknowledge alarms | operator-misuse |

## Lifecycle and results

Use the Scenarios HMI or [scenario REST routes](api.md#scenarios-detections-and-accounts). Public access is not allowed: Viewer can inspect catalog/current/history, while HTTP/HMI controls require Administrator. The engine's internal service role minimum is Operator, but transport policy intentionally reserves instructor controls for Administrator.

Start creates a Running record and returns HTTP 202 plus its run location. Only one run can be active. Pause/resume changes scenario scheduling, not the plant clock; simulation Pause is separate. Stop/reset produces a Stopped run with Aborted outcome. Normal completion produces Completed and one of Detected (all expected rule IDs observed), PartiallyDetected (some) or Missed (none). Pending is used while active. Replay creates a new run with `replayOf` linking to the old UUID; it executes the current catalog definition rather than restoring a recording of the previous plant world.

Actions are serialized with lifecycle controls. Current state is updated every step, published on actions/every 10 steps/finish, and saved on start/lifecycle/finish. History records include action results, measurements (levels/overflow/alarms/interlocks and other observed changes), expected/observed detections and outcome. Historical records survive a PostgreSQL restart, but active engine execution is not resumed from them.

Completion/stop runs cleanup and restores defaults: sensor spoof/fault state, bypass, network impairments, controller forces/setpoints/state, plus scenario-specific Automatic-mode cleanup. This does not restore initial tank volumes, erase records or repair already altered historian samples. Reset clears Current. For a fully fresh world, reset scenario then simulation as described in [operations](operations.md#reset-a-training-exercise).

Several event-based rules use wall-clock windows while physical behavior/actions use simulated time. Changing time scale can affect detection timing/outcomes. Default scenario tests use fake time where appropriate and cover back-to-back runs.

## Detection rules

The 17 rules also include findings outside scenarios. Administrators can tune them through Accounts & rules or PUT `/api/detections/rules/{ruleId}`. Current settings are returned by GET `/api/detections/rules`; each threshold has rule-specific meaning. Settings are stored and recovered at startup, not read from a generic appsettings detection section.

| Rule ID | Default severity | Threshold | Window (s) | Cooldown (s) |
| --- | --- | --- | --- | --- |
| abnormal-tank-level | High | 0 | 60 | 120 |
| mass-balance-inconsistency | High | 3 | 60 | 300 |
| impossible-measurement-change | High | 2 | 60 | 120 |
| unexpected-pump-activity | High | 0 | 60 | 60 |
| unexpected-valve-change | High | 0 | 60 | 60 |
| repeated-rejected-commands | Medium | 3 | 120 | 300 |
| excessive-control-requests | Medium | 8 | 60 | 300 |
| invalid-register-operations | High | 3 | 120 | 300 |
| communication-failure | High | 30 | 10 | 300 |
| controller-state-mismatch | High | 6 | 60 | 300 |
| conflicting-operator-commands | Medium | 4 | 60 | 300 |
| unauthorized-access | High | 3 | 300 | 300 |
| command-replay | High | 1 | 300 | 300 |
| network-segmentation-violation | High | 1 | 300 | 300 |
| historian-integrity | Critical | 4 | 60 | 600 |
| process-safety-violation | Critical | 0 | 60 | 300 |
| operator-misuse | Medium | 4 | 60 | 300 |

Defaults come from [rule implementations](../src/Security/Detection/Rules). All default rules are enabled. Deduplication/cooldowns are scoped per run; findings attach evidence to correlated incidents rather than create an incident for every sample. [Incident workflow](incidents.md)
