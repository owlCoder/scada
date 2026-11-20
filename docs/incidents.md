# Incidents

The background incident monitor observes `SecurityAlert` and `SafetyInterlock` events. Incident IDs are derived deterministically from the source event so replaying the same event does not create a duplicate.

Statuses are `Open` (0), `Acknowledged` (1) and `Resolved` (2). Status transitions cannot move backwards.

- `GET /api/incidents` requires Viewer access.
- `POST /api/incidents/{id}/status` requires Administrator access and a body such as `{"status":1,"actor":"ignored-client-label"}`.

The server records the authenticated identity in audit events. With PostgreSQL enabled, incident status survives application restarts. Detailed status-transition history is not modeled as a separate database table.
