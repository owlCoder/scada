# REST and SignalR API

Routes are defined in [src/Api/Endpoints](../src/Api/Endpoints). JSON REST responses use camelCase properties and string enums; timestamps are ISO 8601 UTC offsets, durations are .NET TimeSpan strings and IDs are UUIDs. Query enums use their names. Send JSON bodies with `Content-Type: application/json`. Roles below are minimums: Viewer (V), Operator (O), Administrator (A); Public needs no sign-in.

## Authentication and shared responses

Use an `X-Api-Key` header or the `scada.session` browser cookie; optional OIDC creates a cookie session. Cookie-authenticated mutations under `/api` also require `X-CSRF-TOKEN`, fetched using `/account/antiforgery` with the same cookie jar. API-key requests do not require CSRF tokens. Caller identity comes from verified credentials; legacy command `actor` fields are ignored.

All protected routes can return **401** missing/invalid credentials, **403** insufficient role, and **429** throttling. Cookie API CSRF failure returns **400**. Malformed JSON, enum/type binding or required parameters can also return **400**. Invalid route constraints generally produce **404**. The global rate limiter returns `Retry-After: 60`; endpoint command-budget rejections need not include it. These shared codes apply to every table below. Endpoint-specific status codes supplement them; unhandled server/storage errors can produce 5xx and are not application result records.

All state-changing protected endpoints below use the credential command budget. HTTP response bodies may be empty, a JSON error string or a typed result; clients must inspect status before deserializing. An HTTP `200` result means accepted/completed by that service, not necessarily that a traveling valve has reached position.

## Health and browser sign-in

| Method | Path / input | Role | Response and codes |
| --- | --- | --- | --- |
| GET | `/health` | Public | 200 `{status:"healthy",storage}`; storage is provider name, no database probe. With `Accept: text/html`, routes to the protected HMI health page instead |
| GET | `/health/live` | Public | 200 `{status:"live"}` |
| GET | `/health/ready` | Public | 200 ready / 503 degraded: `{status,storage:{provider,available},persistence:{pending,capacity,dropped,failed},simulation:{isPaused,stepCount}}`; storage timeout 2 s, degraded at ≥90% queue capacity |
| GET | `/account/login?returnUrl=&error=` | Public | 200 HTML form plus antiforgery cookie/field; error=sso displays generic SSO failure |
| POST | `/account/login` form: `username,password,returnUrl` and antiforgery field | Public | 302 local redirect plus session cookie; 400 invalid CSRF; 401 generic failure HTML; 429 throttle HTML/status |
| POST | `/account/logout` with valid antiforgery form/header | Public | 302 to login; 400 invalid CSRF |
| GET | `/account/login/oidc?returnUrl=` | Public | 302 provider challenge when enabled; 404 disabled |
| GET | `/account/antiforgery` | Public | 200 `{token,headerName:"X-CSRF-TOKEN"}` plus CSRF cookie |
| GET | `/api/me` | V | 200 `{username,role}` |

OIDC middleware also handles the configured `/signin-oidc` callback, validating the provider response and mapping roles; it is not a REST resource. Invalid remote sign-in redirects to `/account/login?error=sso`. Health paths are exempt from the HTTP global limiter.

## Plant and simulation

| Method | Path / JSON input | Role | Success body; additional errors |
| --- | --- | --- | --- |
| GET | `/api/plant` | V | 200 PlantSnapshot (role-redacted) |
| GET | `/api/plant/devices/{id}` | V | 200 Tank/Pump/Valve/SensorSnapshot; 404 unknown |
| POST | `/api/commands` `{deviceId,type,value?:number,sequenceNumber?:long}` | O | 200 CommandResult; command errors below |
| POST | `/api/plant/pump/{id}/command` `{running:bool,sequenceNumber?:long,actor?:string}` | O | CommandResult; command errors |
| POST | `/api/plant/valve/{id}/command` `{position?:number,open?:bool,sequenceNumber?:long,actor?:string}` | O | CommandResult; position wins over open (true=100, false=0); 400 neither present; command errors |
| POST | `/api/plant/devices/{id}/mode/{mode}` no body; mode=`manual` or `automatic` | O | CommandResult; 400 invalid mode; command errors |
| GET | `/api/simulation` | V | 200 SimulationStatus |
| POST | `/api/simulation/pause` no body | O | 200 SimulationStatus |
| POST | `/api/simulation/resume` no body | O | 200 SimulationStatus |
| POST | `/api/simulation/step` optional `{count:1}` | O | 200 `{executed,status:SimulationStatus}`; 400 count outside 1–1000; forces steps even when paused |
| POST | `/api/simulation/reset` no body | A | 200 PlantSnapshot; recreates world, preserves pause flag; historical stores and scenario lifecycle are separate |

Command `type`: `StartPump`, `StopPump`, `SetValvePosition`, `SetManual`, `SetAutomatic`, `ResetInterlock`. Valve values must be 0–100. Commands normally require Manual mode for actuator operation. Optional sequence numbers must advance for the actor. CommandResult is `{accepted,deviceId,reason,message}`: 404 UnknownDevice, 400 InvalidValue, 403 NotAuthorized, 429 RateLimited, 503 CommunicationUnavailable, 409 other rejections (including mode/interlock/replay conditions). See [CommandRejectionReason](../src/Domain/Process/Commands/CommandRejectionReason.cs).

## Controllers, network and registers

| Method | Path / input | Role | Success body; additional errors |
| --- | --- | --- | --- |
| GET | `/api/controllers` | V | 200 ControllerSnapshot[] |
| POST | `/api/controllers/{id}/{operation}` no body | A | 200 `{controller,operation}`; operation=start/stop/reset/release-forces; 400 invalid operation; 404 unknown/unavailable controller |
| GET | `/api/network` | V | 200 NetworkSnapshot |
| POST | `/api/network/links/{id}/connect` no body | A | 200 empty; 409 unknown or already connected |
| POST | `/api/network/links/{id}/disconnect` no body | A | 200 empty; 409 unknown or already disconnected |
| POST | `/api/network/links/{id}/impairment` `{latencyMilliseconds,packetLossPercent}` | A | 200 empty; 400 unknown link/invalid impairment |
| GET | `/api/registers` | V | 200 ControllerRegisterView[] |
| GET | `/api/registers/{unit:int}/{area}/{address:int}?count=1` | V | 200 `{status,values:ushort[],message}`; area=holding/input; 400 invalid area/count (1–125)/IllegalValue; 404 unit/address outside range or unknown map region |
| POST | `/api/registers/{unit:int}/holding/{address:int}` `{value:ushort}` | O | 200 `{status,message}`; 403 writes disabled/denied; 400 value above configured max; 404 unknown unit/non-setpoint address |

Units are 1-based controller positions (default 1=PLC-1, 2=PLC-2, 3=RTU-1). Images span 0–255; only holding addresses 100–199 can be written when `Registers:WritesEnabled=true`, up to `Registers:MaxSetpointValue` (1000). [Protocol/register mapping](protocols.md)

## Alarms, events and telemetry

| Method | Path / input | Role | Response and additional codes |
| --- | --- | --- | --- |
| GET | `/api/alarms` | V | 200 ProcessAlarm[] |
| GET | `/api/alarms/history?count=100` | V | 200 AlarmHistoryRecord[] (store clamps count to 1–1000) |
| POST | `/api/alarms/{key}/acknowledge` no body | O | 200 empty; 404 missing/already acknowledged |
| GET | `/api/events` query below | V | 200 PagedResult<SecurityEvent> |
| GET | `/api/events/recent?count=100` | V | 200 SecurityEvent[] from bounded recent buffer; count clamps to buffer capacity (default 5000) |
| GET | `/api/telemetry?tagId=&count=100&from=&to=` | V | 200 TelemetrySample[] from store; 400 count outside 1–3600 |
| GET | `/api/telemetry/tags` | V | 200 string[] available in live historian |
| GET | `/api/telemetry/integrity` | V | 200 `{isValid,samplesChecked,firstInvalidSequence?,tagId?}` over newest 2000 live samples |
| GET | `/api/trends/{tagId}?count=240` | V | 200 TelemetryPoint[] from live window (unknown tag gives []); 400 count outside 1–3600 |

Event query: `category,actor,deviceId,search,from,to,page=1,pageSize=50`. Search matches messages; timestamp filters use DateTimeOffset values. PagedResult is `{items,page,pageSize,totalCount,totalPages}`. Paging is normalized to page 1–1000000 and pageSize 1–200. Queued records may not yet appear in store queries. Trends/tags/integrity read the in-memory historian and do not reload durable samples on restart.

## Incidents

| Method | Path / JSON input | Role | Success body; additional errors |
| --- | --- | --- | --- |
| GET | `/api/incidents` query below | V | 200 PagedResult<Incident> |
| GET | `/api/incidents/{id:guid}` | V | 200 `{incident,comments:IncidentComment[],timeline:IncidentTimelineEntry[],allowedTransitions:IncidentStatus[]}`; 404 missing |
| GET | `/api/incidents/{id:guid}/timeline` | V | 200 IncidentTimelineEntry[]; 404 missing |
| POST | `/api/incidents/{id:guid}/status` `{status,notes?:string,expectedVersion?:int}` | O | IncidentChange; 400 invalid enum/missing resolution notes/invalid text; 404 missing; 409 transition/version conflict |
| POST | `/api/incidents/{id:guid}/assign` `{assignee?:string,expectedVersion?:int}` | O | IncidentChange; 400 invalid assignee; 404 missing; 409 closed/version conflict; null/blank unassigns |
| POST | `/api/incidents/{id:guid}/priority` `{priority,expectedVersion?:int}` | O | IncidentChange; 400 invalid priority; 404 missing; 409 closed/version conflict |
| POST | `/api/incidents/{id:guid}/comments` `{text:string}` | O | IncidentChange; 400 blank or >4000 characters; 404 missing; 409 closed/concurrent update |

Query: `status,severity,category,search,assignedTo,deviceId,scenarioRunId,activeOnly=false,sortBy=CreatedAt,descending=true,page=1,pageSize=25`. Sort fields: [IncidentSortField](../src/Application/Incidents/IncidentSortField.cs). IncidentChange is `{status,incident?,timeline,message,succeeded}`; successful mutations return 200. Status names are Open, Acknowledged, InProgress, Resolved, Closed; transition and note requirements are in [incidents](incidents.md). `expectedVersion` is optional for status/assign/priority, recommended to avoid overwriting stale work; REST comments do not expose it, though HMI comments capture versions internally. Priorities: Low, Normal, High, Urgent.

## Scenarios, detections and accounts

| Method | Path / JSON input | Role | Success body; additional errors |
| --- | --- | --- | --- |
| GET | `/api/scenarios` | V | 200 ScenarioDescriptor[] |
| GET | `/api/scenarios/current` | V | 200 ScenarioRunRecord, or 204 none |
| GET | `/api/scenarios/runs?limit=50` | V | 200 ScenarioRunRecord[]; minimum 1; PostgreSQL additionally caps at 1000 |
| GET | `/api/scenarios/runs/{runId:guid}` | V | 200 ScenarioRunRecord; 404 missing |
| POST | `/api/scenarios/{id}` no body | A | 202 ScenarioControlResult + Location of run; 404 unknown ID; 409 another run active/start failure |
| POST | `/api/scenarios/{id}/start` no body | A | Same as preceding start alias |
| POST | `/api/scenarios/active/pause` no body | A | 200 ScenarioControlResult; 409 absent/wrong state |
| POST | `/api/scenarios/active/resume` no body | A | 200 ScenarioControlResult; 409 absent/wrong state |
| POST | `/api/scenarios/active/stop` no body | A | 200 ScenarioControlResult (Aborted outcome); 409 no active run |
| POST | `/api/scenarios/reset` no body | A | 200 ScenarioControlResult; aborts active run, restores defaults and clears current |
| POST | `/api/scenarios/runs/{runId:guid}/replay` no body | A | 202 ScenarioControlResult + Location; 409 missing run or other replay/start failure |
| GET | `/api/detections?ruleId=&scenarioRunId=&since=&limit=200` | V | 200 DetectionRecord[]; limit clamps to 1–1000 |
| GET | `/api/detections/rules` | V | 200 RuleDescriptor[] with current settings |
| GET | `/api/detections/metrics` | V | 200 RuleExecutionMetrics[] |
| PUT | `/api/detections/rules/{ruleId}` `{enabled,severity,threshold,windowSeconds,cooldownSeconds}` | A | 200 DetectionRuleSettings; 400 invalid settings; 404 unknown rule |
| GET | `/api/accounts` | A | 200 AccountSummary[]; no credential hashes/secrets |
| POST | `/api/accounts/{username}/role` `{role}` | A | 200 AccountOperationResult; 400 invalid role; 404 missing |
| POST | `/api/accounts/{username}/enabled` `{enabled:bool}` | A | 200 AccountOperationResult; 404 missing |
| POST | `/api/accounts/{username}/password/rotate` no body | A | 200 AccountOperationResult with one-time secret; 404 missing |
| POST | `/api/accounts/{username}/api-key/rotate` no body | A | 200 AccountOperationResult with one-time secret; 404 missing |
| POST | `/api/accounts/{username}/api-key/revoke` no body | A | 200 AccountOperationResult; 404 missing |

Account operations can also return 409 concurrency conflict or 403 service denial. Result is `{status,message,secret?,succeeded}`. Save rotation secrets securely: only their hashes are stored. Runtime APIs do not create/delete local users; seed missing accounts through configuration. Rule settings require a finite nonnegative threshold, window 1–86400 seconds and cooldown 0–86400 seconds, with a defined severity.

## Response record shapes

Fields below use JSON camelCase. Linked source records define nested enums and exact types; nullable fields remain present unless a serializer/framework response omits them.

| Shape | Fields / source |
| --- | --- |
| PlantSnapshot | `plantName,revision,simulationTime,stepCount,interlocksBypassed,tanks[],pumps[],valves[],sensors[],alarms[]`; [all device snapshots](../src/Domain/Process/Snapshots) |
| TankSnapshot | `id,name,capacity,volume,levelPercent,thresholds:{lowLow,low,high,highHigh},isOverflowing,totalOverflowVolume` |
| PumpSnapshot | `id,name,sourceTankId,targetTankId,dischargeValveId,state,mode,fault,speed,flowRate,dischargePressure,health,interlockReason,lastChangeCause,lastChangedBy,startCount` |
| ValveSnapshot | `id,name,role,tankId,position,setpoint,mode,fault,flowRate,health,interlockReason,lastChangeCause,lastChangedBy` |
| SensorSnapshot | `id,name,kind,deviceId,unit,reportedValue,quality,trueValue,fault,driftOffset,isSpoofed,spoofedValue,health`; see redaction below |
| SimulationStatus | `isPaused,simulationTime,stepCount,stepSeconds,timeScale,randomSeed,revision` |
| ControllerSnapshot | `id,name,kind,state,fieldCommunication,supervisoryCommunication,scanCycleMilliseconds,scanCount,consecutiveFailures,totalFailures,commandsIssued,lastScanAt,lastError,actuatorIds[],sensorIds[],registers[],forcedRegisterCount` |
| RegisterSnapshot | `area,address,tag,value,isForced` |
| ControllerRegisterView | `unitId,controllerId,name,kind,registers[]` |
| NetworkSnapshot | `segments:[{id,name,zone}],links:LinkSnapshot[],allowedConduits:[{from,to}],blockedAttempts` |
| LinkSnapshot | `id,name,sourceNode,targetNode,segmentId,state,latencyMilliseconds,packetLossPercent,timeoutMilliseconds,sent,delivered,lost,timedOut,averageLatencyMilliseconds,healthPercent` |
| ProcessAlarm | `key,sourceId,type,severity,message,isActive,raisedAt,clearedAt,isAcknowledged,acknowledgedBy,acknowledgedAt` |
| AlarmHistoryRecord | `id,timestamp,alarmKey,sourceId,type,severity,transition,message,actor` |
| SecurityEvent | `id,timestamp,category,severity,source,message,actor,deviceId,scenarioRunId,correlationId` |
| TelemetrySample | `sequence,timestamp,simulationTime,tagId,value,quality,hash` |
| TelemetryPoint | `timestamp,simulationTime,value,quality` |
| Incident | `id,number,title,description,category,severity,priority,source,status,assignedTo,createdAt,updatedAt,acknowledgedBy,acknowledgedAt,resolvedBy,resolvedAt,resolutionNotes,deviceIds[],scenarioRunId,correlationKey,evidence[],version,displayId,isActive`; [record](../src/Domain/Incidents/Incident.cs) |
| Incident evidence | `kind,reference,summary,timestamp` |
| IncidentTimelineEntry | `id,incidentId,timestamp,actor,kind,description,fromStatus,toStatus` |
| IncidentComment | `id,incidentId,author,text,createdAt` |
| ScenarioDescriptor | `id,name,category,description,durationSeconds,expectedDetections[],steps[]` |
| ScenarioRunRecord | `runId,scenarioId,scenarioName,category,state,startedBy,startedAt,endedAt,simulationStart,elapsed,duration,progressPercent,actions[],measurements:{name:number},expectedDetections[],observedDetections[],outcome,replayOf` |
| ScenarioActionLog | `offset,executedAt,action,succeeded,detail` |
| ScenarioControlResult | `succeeded,message,run` |
| DetectionRecord | `id,timestamp,ruleId,ruleName,severity,category,title,description,deviceId,scenarioRunId,fingerprint,evidence:[{kind,reference,detail}],incidentId` |
| RuleDescriptor | `id,name,description,category,settings` |
| DetectionRuleSettings | `ruleId,enabled,severity,threshold,windowSeconds,cooldownSeconds` |
| RuleExecutionMetrics | `ruleId,name,enabled,evaluations,findings,suppressed,totalMilliseconds,lastFindingAt` |
| AccountSummary | `username,displayName,role,isEnabled,hasApiKey,createdAt,passwordChangedAt,lastLoginAt,isLocked` |

## SignalR

Connect with a Viewer-or-higher identity to **`/hubs/scada`** using an ASP.NET Core SignalR client. Standard negotiation is `POST /hubs/scada/negotiate` (handled by SignalR, not a custom REST endpoint). Use a session cookie or an `X-Api-Key` header where the client supports headers; the server does not implement an API-key query parameter. Unauthenticated/forbidden connections receive 401/403.

The only application hub method is **`GetSnapshot()`**. It returns `{plant:PlantSnapshot,simulation:SimulationStatus,scenario:ScenarioRunRecord|null,at:DateTimeOffset}`. Call it after initial connection and after reconnecting. There are no command or subscribe/unsubscribe hub methods: a connection joins the server-assigned instructor or operator group automatically.

Server messages invoke **`Realtime(topic, payload)`** with two arguments, not a RealtimeMessage envelope. In JSON SignalR protocol the invocation is conceptually `{"type":1,"target":"Realtime","arguments":["plant",{...}]}` (SignalR supplies framing). The internal `publishedAt` field is not forwarded. SignalR uses its own JSON serialization options: camelCase fields, with enums serialized numerically by default; the REST string-enum configuration does not configure SignalR.

| Topic | Payload |
| --- | --- |
| `plant` | PlantSnapshot |
| `telemetry` | TelemetrySample[] |
| `controllers` | ControllerSnapshot[] |
| `network` | NetworkSnapshot |
| `alarms` | ProcessAlarm[] after step transitions or acknowledgement |
| `events` | SecurityEvent |
| `incidents` | Incident |
| `scenarios` | ScenarioRunRecord, or `{reset:true}` on reset |
| `detections` | DetectionRecord |
| `simulation` | SimulationStatus |

The internal `events-persisted` topic refreshes HMI queries after queue writes but is absent from `RealtimeTopics.All`, so the hub forwarder does not subscribe to it. Delivery is bounded and lossy; no replay cursor exists. Reload authoritative state after reconnecting and query REST for history. Group membership is assigned on connection; reconnect after identity/role changes.

Plant payloads and GetSnapshot use the same redaction as REST/HMI: only Administrators see original sensor truth/spoof fields and true tank contents. Other data, including scenario actions, controller forces and incident evidence, is not globally redacted. [Exact redaction scope](hmi.md#redaction)
