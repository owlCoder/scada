# Plant protocols

## Virtual registers and Modbus TCP

The virtual register map is backed by the simulated plant:

| Address | Value | Scale | Writable through HTTP |
| --- | --- | --- | --- |
| 0 | Tank level | percent × 10 | No |
| 1 | Pump state | 0 or 1 | Yes |
| 2 | Outlet valve state | 0 or 1 | Yes |
| 3 | Inflow rate | units/s × 100 | No |
| 4 | Outflow rate | units/s × 100 | No |

HTTP routes: `GET /api/registers`, `GET /api/registers/{address}`, and `POST /api/registers/{address}`. Writes require Operator access.

Optional Modbus TCP is disabled by default:

```bash
export Protocols__ModbusEnabled=true
export Protocols__ModbusPort=1502
```

The server binds only to `127.0.0.1`. It supports function 03 (read holding registers) and rejects writes by default.

## OPC UA

Enable the optional server with `Protocols__OpcUa__Enabled=true`, then connect to `opc.tcp://localhost:4840`.
The listener binds to IPv4 loopback (`127.0.0.1`).

| Setting under `Protocols:OpcUa` | Default | Purpose |
| --- | --- | --- |
| `Enabled` | `false` | Starts the OPC UA server with the API host. |
| `Port` | `4840` | TCP port, from 1024 to 65535. |
| `AllowAnonymous` | `true` | Allows anonymous Viewer sessions and enables the `None` endpoint. |
| `ApplicationName` | `SCADA Training Plant OPC UA` | Server name shown in discovery. |
| `PkiDirectory` | `.scada/pki` | Absolute path or a path relative to the directory containing `scada.slnx`, with the API content root as a fallback. |
| `AutoAcceptUntrustedCertificates` | `true` | Development default. Set it to `false` to require clients to be explicitly trusted. |

The `None` endpoint accepts anonymous sessions for browsing, reading and subscriptions. `Basic256Sha256`
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
