# Training user guide

The simulator is a virtual water treatment plant. All attacks, controllers and network impairments operate inside the simulation. Levels are percentages, volume is m³, flow is m³/min and pressure is bar. The default clock advances five simulated seconds per wall second.

## Prepare an exercise

1. Start the API using [quick start](../README.md#quick-start-in-memory) or the [deployment guide](deployment.md), then sign in at `/account/login`.
2. The instructor uses an Administrator account; trainees use Operator or Viewer accounts. Development credentials are randomly generated in `.scada/dev-credentials.txt`. Never distribute the Administrator password to a trainee.
3. Open Overview and Plant diagram. Identify T-101/T-102/T-103, transfer pumps P-101/P-103, treatment pump P-102 and the inlet/outlet valves. Check communications and controllers are healthy.
4. Use `/identity` to confirm the role. Trainees see reported sensor values; instructors can compare them with ground truth on Sensors. Some scenario details/evidence are visible to trainees, so an instructor may choose which pages to use during assessment.

## Start and observe a scenario

As instructor, open Scenarios, read the catalog description/expected detections, select Start and confirm. Only one run can be active. Watch its state, elapsed simulated time, action results and progress. The [catalog](scenarios.md) explains all 14 exercises.

As trainee, use the diagram and Trends to establish what changed. Select up to three relevant tags: for example LT-101, LT-102 and FT-102. Compare level changes with flows and command activity rather than trusting a single instrument. Controllers shows field/supervisory failures; Communications shows loss and latency; Registers shows setpoints and output images.

Scenario Pause freezes scheduled scenario actions while the plant keeps running. Overview Pause freezes the simulation; Resume restarts it. Instructor scenario Stop marks the run Aborted and restores training defaults. At normal completion, the run records an outcome based on expected rule IDs observed, not on a trainee score.

## Respond to alarms and operate safely

Open Alarms to see source, type, severity and active/acknowledged state. Acknowledge only after reviewing the condition; acknowledgement records responsibility and does not remove its cause. Review recent alarm history for raise/clear/acknowledge transitions.

Operators can switch a pump or valve to Manual, confirm, then issue a start/stop or valve position command. Confirm the device and target in the dialog. Interlocks, network path and command-rate limits still apply. Valves move over time. After handling the condition, restore Automatic so the controller resumes control. A latched pump interlock may require Reset interlock after correcting the process condition.

A rejected command shows a reason: wrong mode, safety trip, unavailable communications or rate limit can all be part of the exercise. Avoid repeated blind retries; inspect plant, controller and communication state first.

## Investigate an incident

1. Open Incidents. Filter active records by status/severity or search title/description; sort by priority/severity/time and open the incident number.
2. Read source, affected devices, evidence and timeline. In Events, filter actor/category/message and record correlation IDs. Compare this evidence with alarm history, reported trends and controller/register state.
3. As Operator, acknowledge the incident and assign it; move to InProgress while investigating. Add comments describing observations and containment actions. Each change asks for confirmation.
4. Resolve only when the exercise condition is understood and handled. Resolution notes are required. A resolved incident can be reopened or closed; Closed is terminal. If another user changed the record, review the refreshed version and retry the intended update.
5. The instructor reviews scenario action results, measurements, expected/observed detections and final outcome alongside the incident timeline. Use Replay to create a new run linked to the earlier record.

Viewer accounts can inspect evidence but cannot edit incidents or operate devices. Account and detection-rule changes belong to the instructor and are on Accounts & rules. [All HMI permissions](hmi.md)

## Finish and reset

Instructor Reset environment on Scenarios aborts an active run, restores training defaults and clears the current run display; recorded history remains. For a completely fresh plant/controller/network world, reset the scenario first and then use the simulation-reset API described in [operations](operations.md#reset-a-training-exercise). Process reset does not erase historical incidents, events or telemetry.

In-memory records disappear when the process stops. PostgreSQL records survive restart; live Trends starts a new in-memory window, while `/api/telemetry` provides durable history. Use the [operations runbook](operations.md) for health checks and recovery.
