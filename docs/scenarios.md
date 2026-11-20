# Security training scenarios

Scenarios generate synthetic security events. They do not scan networks, send attacks or modify physical devices.

| ID | Purpose |
| --- | --- |
| `unauthorized-command` | Simulate an unauthorized command attempt |
| `sensor-spoofing` | Simulate inconsistent sensor readings |
| `register-tampering` | Simulate a protected-register modification attempt |

`GET /api/scenarios` requires Viewer access. `POST /api/scenarios/{id}` requires Administrator access and a JSON body containing an `actor` field. The recorded event actor is taken from the authenticated identity, not the body. Generated `SecurityAlert` events are subsequently processed by the incident monitor.
