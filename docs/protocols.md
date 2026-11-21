# Plant protocols

Both protocol servers are disabled by default and hosted by the API process. They operate on the virtual controllers/plant only. [All configuration keys](configuration.md)

## Virtual registers and Modbus TCP

Register units are the 1-based configured controller order: default 1=PLC-1, 2=PLC-2, 3=RTU-1. Every unit has 256 holding and 256 input registers (0–255), not the old five-register plant map. Discover tagged registers with `GET /api/registers` or the Registers HMI.

| Area / address | Meaning | Write access |
| --- | --- | --- |
| Input 0+ | Measurements defined per controller | Read only |
| Holding 0+ | Actuator output image | Read only externally |
| Holding 100–199 | Loop setpoints | Operator, only when explicitly enabled |
| Holding 200 | Controller state | Read only |
| Holding 201 | Field communication state | Read only |
| Holding 202 | Scan counter | Read only |
| Holding 203 | Consecutive failures | Read only |
| Holding 210+ | Loop demand | Read only |

| Default unit | Input mapping | Holding outputs and configured setpoints |
| --- | --- | --- |
| 1 / PLC-1 | 0 LT-101, 1 LT-102 (level ×10); 2 FT-101, 3 FT-102, 4 FT-104 (flow ×100); 5 PT-101, 6 PT-103 (pressure ×100) | HR0 P-101, HR1 P-103 run (0/1); HR2 V-101, HR3 V-102, HR4 V-103 position ×10. HR100/101 T-102 pump start/stop=400/750; HR102/103 T-101 inlet open/close=450/800 |
| 2 / PLC-2 | 0 LT-102, 1 LT-103 (×10); 2 FT-103, 3 PT-102 (×100) | HR0 P-102 run; HR1 V-201, HR2 V-302 position ×10. HR100/101 T-103 pump start/stop=450/800 |
| 3 / RTU-1 | 0 LT-103 (×10), 1 FT-301 (×100) | HR0 V-301 position ×10; no configured control loops |

Sources: [ControllerRegisterMap](../src/Domain/Control/ControllerRegisterMap.cs), [DefaultControllerDefinitions](../src/Domain/Control/DefaultControllerDefinitions.cs). Values are ushort register images; plant units are m³, m³/min, bar and percent. Unmapped addresses can be read but have no process meaning. HR100–199 is the accepted write range even where no loop consumes a particular address. Setpoint maximum is 1000 by default.

REST reads use `/api/registers/{unit}/{holding|input}/{address}?count=1` (1–125 registers); writes use `POST /api/registers/{unit}/holding/{address}` with `{"value":650}` and Operator access. `Registers:WritesEnabled` defaults false.

Enable TCP reads for a local training run:

```bash
Protocols__Modbus__Enabled=true dotnet run --project src/Api -- --urls http://127.0.0.1:5080
```

The listener binds `127.0.0.1:1502`, limits connections to 16 and closes idle reads after 10 seconds by default. Nested `Protocols:Modbus` settings are preferred; legacy `Protocols:ModbusEnabled` and `Protocols:ModbusPort` still override them when set.

Supported functions: FC03 holding read, FC04 input read, FC06 single holding write and FC16 multiple holding write (up to 123 values). Both `Protocols:Modbus:AllowWrites=true` and `Registers:WritesEnabled=true` are required for writes. Modbus has no user authentication or API-key exchange: writes use local Operator actor `modbus-local` (configurable), and the command limiter groups writes by client host. Keep it isolated. FC16 validates address/value ranges before applying writes; it is not a database transaction.

Strict MBAP validation requires protocol ID 0 and matching length; malformed frames are audited and connections closed. Exceptions are 01 unsupported/disabled function, 02 illegal address, 03 illegal value, 04 device failure/rate limit and 0B unknown unit. Invalid/denied register operations are audit and detection inputs.

## OPC UA

Enable the optional server with `Protocols__OpcUa__Enabled=true`, then connect to `opc.tcp://localhost:4840`.
The listener binds to IPv4 loopback (`127.0.0.1`).

| Setting under `Protocols:OpcUa` | Default | Purpose |
| --- | --- | --- |
| `Enabled` | `false` | Starts the OPC UA server with the API host. |
| `Port` | `4840` | TCP port, from 1024 to 65535. |
| `AllowAnonymous` | `false` | Allows anonymous Viewer sessions and enables the unencrypted `None` endpoint. Local training only. |
| `ApplicationName` | `SCADA Training Plant OPC UA` | Server name shown in discovery. |
| `PkiDirectory` | `.scada/pki` | Absolute path or a path relative to the directory containing `scada.slnx`, with the API content root as a fallback. |
| `AutoAcceptUntrustedCertificates` | `false` | When `true`, every client certificate is trusted permanently. Use it only for local development and tests; otherwise copy client certificates into `<PkiDirectory>/trusted/certs`. |

Only when `AllowAnonymous=true`, the `None` endpoint accepts anonymous sessions for browsing, reading and subscriptions. With the default `false`, no `None` endpoint or anonymous token policy is offered. `Basic256Sha256`
is available for `Sign` and `SignAndEncrypt`. Use `SignAndEncrypt` for message confidentiality.
Secure endpoints accept `UserName` tokens for local SCADA accounts configured with `Security:Users:N:Password`
or created by the development account generator. Anonymous and Viewer sessions have read-only access.
Operator and Administrator sessions can call plant methods.

Under `Objects → Plant`, the application namespace is `urn:scada-training:plant`. Use the URI to resolve
the namespace index. String node IDs follow their browse paths, for example `Plant/Tanks/T-101/Level`.

| Folder | Read-only variables |
| --- | --- |
| `Tanks` | `Capacity` and `Volume` in m³, `Level` in percent |
| `Pumps` | `State`, `Mode`, `Health`, `Flow` in m³/min, `DischargePressure` in bar |
| `Valves` | `Position` and `Setpoint` in percent, `Mode`, `Health`, `Flow` in m³/min |
| `Sensors` | `Value`, `Unit`, `Quality`, `Health` |
| `Controllers` | `State`, `FieldCommunication`, `SupervisoryCommunication` |

Level, volume, flow and discharge pressure are available where the plant has the corresponding
instrumentation. Every session sees the operator view of those measurements. A background worker
updates the variables from simulation steps and publishes data changes to monitored items. It
keeps the latest pending snapshot if steps arrive faster than it can publish.

Call `StartPump` and `StopPump` on pumps, and `SetValvePosition` with one scalar `Double` (0–100)
on valves. Both actuator types offer `SetManual`, `SetAutomatic` and `ResetInterlock`. Switch the
device to Manual before operating it. Method calls go through the same command checks and audit
log as other supervisory commands, with source `OpcUa`. Anonymous and Viewer attempts return
`BadUserAccessDenied`.

On first start the server creates a self-signed certificate in `own/certs` and stores its private
key in `own/private` under the PKI directory. Import the server's public certificate into the
client's trust store. With automatic acceptance disabled, place trusted client certificates in
`trusted/certs` and issuer certificates in `issuer/certs`. Rejected certificates are recorded in
`rejected/certs`. The default `.scada/` directory is ignored by Git.


`Protocols:OpcUa:BindAddress` (default `127.0.0.1`, IPv4 only) selects the listener address. Containers set `0.0.0.0` and publish the port on the host loopback only; see [deployment](deployment.md#compose-variants-and-smoke-test). `Protocols:Modbus:BindAddress` works the same way.

## IEC 60870-5-104 (grid mode)

The API can also host an IEC 60870-5-104 slave exposing the grid RTUs (common address per RTU, IOA ranges 1000+ measurements, 2000+ switch status, 3000+ taps, 4000+ protection trips, 5000+ RTU status) with general interrogation, spontaneous COT 3 data, CP56Time2a time tags and select-before-operate switch/tap commands. It is disabled by default and binds IPv4 loopback on port 2404. See [iec104.md](iec104.md) for the full protocol, timer and security notes.
