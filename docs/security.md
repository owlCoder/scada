# Security

All endpoints except `/health` require the `X-Api-Key` header. Roles are `Viewer`, `Operator` and `Administrator`. A higher role includes lower-role permissions.

Configure unique keys using environment variables:

```bash
export Security__ViewerApiKey="replace-with-random-viewer-key"
export Security__OperatorApiKey="replace-with-random-operator-key"
export Security__AdminApiKey="replace-with-random-admin-key"
```

Named user credentials can also be configured:

```bash
export Security__Users__0__Username="alice"
export Security__Users__0__Role="Operator"
export Security__Users__0__ApiKey="replace-with-unique-random-user-key"
```

Names and keys must be unique. Named keys cannot duplicate shared role keys. The server derives audit identity from the authenticated credential, never from the client-supplied `actor` field. The `actor` request field is optional for backwards compatibility; clients should omit it. `GET /api/me` returns the authenticated username and role.

Command rate limits are tracked per credential fingerprint (10 accepted commands per minute). This applies to plant controls, virtual-register writes, incident status changes and security scenario execution. The dashboard holds the key only in page memory. Security response headers include `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and `Cache-Control`.

**Development use only:** there is no OIDC, password-based login, credential rotation workflow, TLS termination or production identity management. Do not expose the API or Modbus port to untrusted networks or connect to physical PLCs.
