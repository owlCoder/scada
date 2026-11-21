# Security

## Roles

There are three roles, and a higher role includes everything a lower role can do.

| Role | Can do |
| --- | --- |
| `Viewer` | Read plant state, events, incidents and trends |
| `Operator` | Everything a Viewer can, plus operate devices, acknowledge alarms and work incidents |
| `Administrator` | Everything an Operator can, plus act as instructor: scenarios, training controls and account management |

Every protected endpoint uses one of the policies `Viewer`, `Operator` or `Administrator`. The responses are:

| Response | When |
| --- | --- |
| **401** | No credentials, or invalid credentials |
| **403** | Valid credentials, but the role is too low |
| **429** | The client is being throttled |

Every 403 is audited as an `AuthorizationDenied` event. These events feed the unauthorized-access detection rule.

## Signing in

There are three ways to authenticate.

1. **Browser session (cookie).** Sign in at `GET /account/login`, which posts the form to `POST /account/login`. The session cookie `scada.session` is HttpOnly, SameSite=Strict and Secure over HTTPS, and lasts 30 minutes with sliding expiry. `POST /account/logout` ends the session. Both forms are protected by an antiforgery token.
   - **Every request rechecks the account.** A disabled or locked account, or a password changed after sign-in, ends the session. A role change applies to the next request.
   - **State-changing `/api` requests** made with a session must send an antiforgery token in the `X-CSRF-TOKEN` header. Get the token from `GET /account/antiforgery`.
   - **Failed sign-ins** all show the same message. They are throttled per IP: 10 failures per minute, plus the sign-in rate limit below. An account locks for 15 minutes after 5 consecutive failures; change this with `Security__Lockout__MaxFailedAttempts` and `Security__Lockout__LockoutDuration`. Passwords are never logged.
2. **API key (`X-Api-Key`).** Keys are for scripts, the Modbus write path and the current dashboard. Keys are checked against the account store on every request, so rotating or revoking a key, or disabling its account, takes effect immediately. Set `Security__ApiKeys__Enabled=false` to turn API keys off completely.
3. **Single sign-on (OIDC, optional).** Set `Authentication__Oidc__Enabled=true`, plus `Authority`, `ClientId` and `ClientSecret`. Set `MetadataAddress` too if the API reaches the identity provider at a different address than browsers do.
   - **Roles** come from the `roles` claim (configurable). `scada-viewer`, `scada-operator` and `scada-administrator` map to the three SCADA roles, and the highest role wins. An identity without one of these roles cannot sign in.
   - **Login flow:** the login page shows a single sign-on link, `/account/login/oidc`. It uses the authorization code flow with PKCE.

## Accounts

Configured users are created on first start and are then managed through `/api/accounts` (Administrator only).

```bash
export Security__Users__0__Username="alice"
export Security__Users__0__Role="Operator"
export Security__Users__0__PasswordHash="pbkdf2-sha256$210000$..."   # preferred: no clear-text password in configuration
export Security__Users__0__ApiKey="replace-with-unique-random-user-key"   # optional
```

Notes on configured users:

- **`Password`** can be used instead of `PasswordHash`, for local testing.
- **Passwords** are stored as PBKDF2-SHA256 hashes with 210,000 iterations, and must be at least 12 characters.
- **A user with no password** gets a random one that nobody knows, so it can only use its API key until an administrator rotates the password.
- **Names and keys** must be unique. A named key cannot duplicate one of the shared legacy role keys (`Security__ViewerApiKey`, `Security__OperatorApiKey`, `Security__AdminApiKey`).

### Development accounts

In the `Development` environment, `appsettings.Development.json` enables `Security:DevelopmentAccounts`.

- **What it creates:** the accounts `dev-viewer`, `dev-operator` and `dev-admin`, each with a random password and API key.
- **Where the credentials go:** `.scada/dev-credentials.txt` at the repository root. The file is gitignored, readable by its owner only, and only the `SCADA_DEV_*` lines are rewritten. Credentials are never logged.
- **Production:** never. The feature is off in every other environment.

## Keycloak (optional)

```bash
docker compose -f deploy/compose.yaml -f deploy/compose.keycloak.yaml up -d
```

```bash
scripts/keycloak-bootstrap.sh
```

- **The realm** is imported from `deploy/keycloak/scada-realm.json`. It contains the three roles and the `scada-api` client (code flow with PKCE, and a `roles` claim in tokens), but no users.
- **The bootstrap script** creates `sso-viewer`, `sso-operator` and `sso-administrator` with random passwords. It writes them to `.scada/dev-credentials.txt` and never prints them.
- **Required settings:** `KEYCLOAK_ADMIN_PASSWORD`, `SCADA_OIDC_CLIENT_SECRET` and `SCADA_OIDC_ENABLED=true`.

## Rate limits

| Scope | Default | Setting |
| --- | --- | --- |
| All requests, per signed-in credential (or per IP when anonymous) | 1200 per minute | `RateLimiting__PermitsPerMinute` |
| Sign-in attempts, per IP | 10 per minute | `RateLimiting__LoginPermitsPerMinute` |
| State-changing commands, per credential | 10 accepted per minute | `CommandMonitor` |

Health endpoints are not rate-limited. A rejected request gets 429 with `Retry-After`, and is audited at most once per minute per client.

## Other protections

- **Audit identity** always comes from the authenticated credential, never from a client-supplied `actor` field.
- **Response headers** on every response: CSP (`default-src 'self'`), `X-Frame-Options: DENY`, `X-Content-Type-Options`, `Referrer-Policy` and `Cache-Control: no-cache, no-store`.
- **Redirects:** the login form only follows local `returnUrl` values.

**Development and training use only.** The simulator has no TLS termination of its own. Do not expose the API or the Modbus port to untrusted networks, and never connect it to physical PLCs.
