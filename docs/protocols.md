# Virtual registers and Modbus TCP

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

The server binds only to `127.0.0.1`. It supports function 03 (read holding registers) and rejects writes. It is a training simulator, not a PLC gateway. OPC UA is not implemented.
