# Configuration reference

Defaults below are the checked-in application defaults, including CLR `false`/`null` defaults in options classes. Environment-specific settings and Compose overrides are called out explicitly. Run commands from the repository root.

ASP.NET Core loads `src/Api/appsettings.json`, then the environment-specific JSON, environment variables and command-line arguments (later sources override earlier ones). Use double underscores for environment nesting and numeric array indexes: `Simulation:TimeScale` → `Simulation__TimeScale`, `Security:Users:0:Username` → `Security__Users__0__Username`. Time spans use .NET format, for example `00:00:10` or `30.00:00:00`. Booleans use `true`/`false`. Configuration is bound at startup; restart to apply file/environment changes. Runtime account and rule edits use their stores.

This reference covers the application's configuration surface. ASP.NET Core, Serilog and Npgsql also expose their standard provider options; arbitrary keys are not simulator features.


## Storage, queue and retention

Source: [registration](../src/Api/Configuration/Registration/StorageRegistrationExtensions.cs), [queue options](../src/Infrastructure/Persistence/Queue/PersistenceQueueOptions.cs), [retention options](../src/Infrastructure/Persistence/Retention/RetentionOptions.cs).

| Key | Environment variable | Default | Effect |
| --- | --- | --- | --- |
| `Storage:Provider` | `Storage__Provider` | `Memory` | `Memory` or case-insensitive `Postgres`; other values fail startup. |
| `Storage:ApplyMigrations` | `Storage__ApplyMigrations` | `true` | Apply pending PostgreSQL EF migrations before seeding accounts. |
| `ConnectionStrings:Scada` | `ConnectionStrings__Scada` | unset | Required PostgreSQL connection string when Postgres is selected; never log it. |
| `Persistence:Capacity` | `Persistence__Capacity` | 10000 | Maximum queued work items; full queues drop incoming items. Valid 1–1000000. |
| `Persistence:MaxAttempts` | `Persistence__MaxAttempts` | 3 | Total write attempts, including the first; valid 1–10. |
| `Persistence:RetryDelay` | `Persistence__RetryDelay` | `00:00:00.200` | Linear retry delay multiplied by attempt number; 0–30 seconds. |
| `Persistence:BatchSize` | `Persistence__BatchSize` | 256 | Maximum work items per dequeue batch; valid 1–10000. |
| `Retention:Enabled` | `Retention__Enabled` | `true` | Run retention at startup and on its timer. |
| `Retention:Interval` | `Retention__Interval` | `01:00:00` | Purge interval; 1 second–7 days. |
| `Retention:DefaultMaxAge` | `Retention__DefaultMaxAge` | `30.00:00:00` | Default retained age; at least one minute. |
| `Retention:MaxAge:events` | `Retention__MaxAge__events` | unset | Override age of events. |
| `Retention:MaxAge:telemetry` | `Retention__MaxAge__telemetry` | unset | Override age of durable telemetry. |
| `Retention:MaxAge:alarm-history` | `Retention__MaxAge__alarm-history` | unset | Override alarm history age; use JSON or an environment launcher supporting hyphens in names. |
| `Retention:MaxAge:detections` | `Retention__MaxAge__detections` | unset | Override detection age. All overrides must be at least one minute. |

## Simulation

Source: [options](../src/Application/Simulation/SimulationOptions.cs), [host options](../src/Simulation/Configuration/SimulationHostOptions.cs).

| Key | Environment variable | Default | Effect |
| --- | --- | --- | --- |
| `Simulation:StepSeconds` | `Simulation__StepSeconds` | 0.5 | Simulated seconds per step; finite, >0 and ≤10. |
| `Simulation:TimeScale` | `Simulation__TimeScale` | 5 | Simulated seconds per wall second; 0.1–100. StepSeconds / TimeScale must be ≥0.005 seconds. |
| `Simulation:RandomSeed` | `Simulation__RandomSeed` | 1337 | Seed for deterministic virtual network impairment. |
| `Simulation:TelemetryIntervalSteps` | `Simulation__TelemetryIntervalSteps` | 2 | Sample all sensors every N steps; positive. |
| `Simulation:StatePersistenceIntervalSteps` | `Simulation__StatePersistenceIntervalSteps` | 20 | Queue checkpoint every N steps; positive. |
| `Simulation:SupervisoryTimeoutPolls` | `Simulation__SupervisoryTimeoutPolls` | 3 | Consecutive failed polls before Offline; positive. |
| `Simulation:StartPaused` | `Simulation__StartPaused` | `false` | Pause a newly created world; recovered pause state can supersede this. |
| `Simulation:Host:TopologyFile` | `Simulation__Host__TopologyFile` | `null` | Optional JSON SimulationTopology file; relative paths follow process working directory. Empty uses default plant/network/controllers. |
| `Simulation:Host:InitialScenario` | `Simulation__Host__InitialScenario` | `null` | Scenario ID to start once during host startup. |
| `Simulation:Host:RunLoop` | `Simulation__Host__RunLoop` | `true` | Enable background stepping; false permits manual-only operation. |
| `Simulation:Host:RestoreOnStart` | `Simulation__Host__RestoreOnStart` | `true` | Restore the latest stored simulation checkpoint before starting the loop. |

## Accounts, keys and sessions

Source: [security registration](../src/Api/Configuration/Registration/SecurityRegistrationExtensions.cs), [authentication registration](../src/Api/Configuration/Registration/AuthenticationRegistrationExtensions.cs), [session](../src/Api/Authentication/SessionSettings.cs), [development](../src/Api/Development/DevelopmentAccountSettings.cs).

| Key | Environment variable | Default | Effect |
| --- | --- | --- | --- |
| `Security:ViewerApiKey` | `Security__ViewerApiKey` | empty | Legacy shared Viewer key; empty disables it. |
| `Security:OperatorApiKey` | `Security__OperatorApiKey` | unset | Legacy shared Operator key; falls back to CommandApiKey only when unset. |
| `Security:CommandApiKey` | `Security__CommandApiKey` | empty | Legacy fallback Operator key in appsettings.json. |
| `Security:AdminApiKey` | `Security__AdminApiKey` | empty | Legacy shared Administrator key. |
| `Security:ApiKeys:Enabled` | `Security__ApiKeys__Enabled` | `true` | Register/use X-Api-Key authentication; false leaves cookie/OIDC sign-in. |
| `Security:Users:N:Username` | `Security__Users__N__Username` | empty / no users | Named account username at index N, e.g. 0; required and unique. |
| `Security:Users:N:Role` | `Security__Users__N__Role` | required | Viewer, Operator or Administrator; invalid/missing value fails startup. |
| `Security:Users:N:ApiKey` | `Security__Users__N__ApiKey` | empty | Optional unique key; store gets its hash. Must not duplicate shared role keys. |
| `Security:Users:N:Password` | `Security__Users__N__Password` | `null` | Optional bootstrap password, at least 12 characters. |
| `Security:Users:N:PasswordHash` | `Security__Users__N__PasswordHash` | `null` | Preferred precomputed PBKDF2-SHA256 password hash; takes precedence over Password. At least one credential is required. |
| `Security:Cookies:RequireSecure` | `Security__Cookies__RequireSecure` | `false` | Always mark session/CSRF cookies Secure; otherwise SameAsRequest. |
| `Security:Session:AbsoluteLifetime` | `Security__Session__AbsoluteLifetime` | `08:00:00` | Positive absolute session lifetime from issued time, including OIDC. |
| `Security:Lockout:MaxFailedAttempts` | `Security__Lockout__MaxFailedAttempts` | 5 | Consecutive password failures before account lock; 1–100. |
| `Security:Lockout:LockoutDuration` | `Security__Lockout__LockoutDuration` | `00:15:00` | Lock duration; 1 second–1 day. |
| `Security:DevelopmentAccounts:Enabled` | `Security__DevelopmentAccounts__Enabled` | `false`; Development JSON: `true` | Generate dev-viewer/dev-operator/dev-admin only in Development. |
| `Security:DevelopmentAccounts:CredentialsFile` | `Security__DevelopmentAccounts__CredentialsFile` | `null` | Override credential-file path relative to API content root. Default .scada/dev-credentials.txt beneath nearest solution directory, falling back to content root. |
| `Security:CommandRate:Limit` | `Security__CommandRate__Limit` | 10 | CommandMonitor budget per tracked identity/window; REST mutation filters also use it. |
| `Security:CommandRate:Window` | `Security__CommandRate__Window` | `00:01:00` | Rolling command-monitor window. |

Configured named users are seeded only when missing. Editing their configuration does not reset an existing account's role/password/key. Manage existing accounts with `/accounts` or the account API. Shared legacy keys are configuration-only and have no browser password. Cookie idle expiry is a fixed 30-minute sliding window; the absolute lifetime is configurable. Authentication source throttling is fixed at 10 failures/minute. HMI identity revalidation is fixed at one minute; neither has a configuration key.

## OIDC

Source: [OIDC settings](../src/Api/Authentication/OidcSettings.cs), [registration](../src/Api/Configuration/Registration/OidcRegistrationExtensions.cs).

| Key | Environment variable | Default | Effect |
| --- | --- | --- | --- |
| `Authentication:Oidc:Enabled` | `Authentication__Oidc__Enabled` | `false` | Enable code + PKCE browser sign-in. |
| `Authentication:Oidc:Authority` | `Authentication__Oidc__Authority` | empty | Required absolute provider URL when enabled. |
| `Authentication:Oidc:MetadataAddress` | `Authentication__Oidc__MetadataAddress` | `null` | Optional absolute discovery URL for server-side access. |
| `Authentication:Oidc:ClientId` | `Authentication__Oidc__ClientId` | empty | Required when enabled. |
| `Authentication:Oidc:ClientSecret` | `Authentication__Oidc__ClientSecret` | `null` | Confidential client secret. |
| `Authentication:Oidc:RequireHttpsMetadata` | `Authentication__Oidc__RequireHttpsMetadata` | `true` | Require HTTPS metadata; false logs a startup warning. Compose sets false for local Keycloak. |
| `Authentication:Oidc:RoleClaimType` | `Authentication__Oidc__RoleClaimType` | `roles` | Claim containing role names. |
| `Authentication:Oidc:ViewerRole` | `Authentication__Oidc__ViewerRole` | `scada-viewer` | Mapped Viewer role. |
| `Authentication:Oidc:OperatorRole` | `Authentication__Oidc__OperatorRole` | `scada-operator` | Mapped Operator role. |
| `Authentication:Oidc:AdministratorRole` | `Authentication__Oidc__AdministratorRole` | `scada-administrator` | Mapped Administrator role; highest mapped role wins. |
| `Authentication:Oidc:DisplayName` | `Authentication__Oidc__DisplayName` | `single sign-on` | Link text on login page. |

## Rate limiting and proxy trust

Source: [rate settings](../src/Api/RateLimiting/RateLimitSettings.cs), [proxy registration](../src/Api/Configuration/Registration/ForwardedHeadersRegistrationExtensions.cs).

| Key | Environment variable | Default | Effect |
| --- | --- | --- | --- |
| `RateLimiting:PermitsPerMinute` | `RateLimiting__PermitsPerMinute` | 1200 | Global fixed window per credential or anonymous IP; 1–1000000; health exempt. |
| `RateLimiting:LoginPermitsPerMinute` | `RateLimiting__LoginPermitsPerMinute` | 10 | Fixed window per sign-in source IP; 1–10000. |
| `ForwardedHeaders:KnownProxies:N` | `ForwardedHeaders__KnownProxies__N` | empty list | Trusted proxy IP at index N. Enables one hop of forwarded client IP/scheme. Invalid IP fails startup. |

## Register and Modbus access

Source: [register options](../src/Application/Registers/RegisterWriteOptions.cs), [Modbus options](../src/Protocols/Modbus/ModbusOptions.cs), [registration](../src/Api/Configuration/Registration/ProtocolRegistrationExtensions.cs).

| Key | Environment variable | Default | Effect |
| --- | --- | --- | --- |
| `Registers:WritesEnabled` | `Registers__WritesEnabled` | `false` | Opt into holding setpoint writes (100–199); requires Operator. |
| `Registers:MaxSetpointValue` | `Registers__MaxSetpointValue` | 1000 | Maximum accepted ushort setpoint value. |
| `Protocols:Modbus:Enabled` | `Protocols__Modbus__Enabled` | `false` | Enable IPv4 loopback Modbus TCP listener. |
| `Protocols:Modbus:Port` | `Protocols__Modbus__Port` | 1502 | TCP port; 1024–65535. |
| `Protocols:Modbus:MaxConnections` | `Protocols__Modbus__MaxConnections` | 16 | Concurrent TCP connections; 1–256. |
| `Protocols:Modbus:IdleTimeout` | `Protocols__Modbus__IdleTimeout` | `00:00:10` | Idle read timeout; >0 and ≤10 minutes. |
| `Protocols:Modbus:WriteActor` | `Protocols__Modbus__WriteActor` | `modbus-local` | Local Operator identity used for unauthenticated Modbus writes; cannot be blank. |
| `Protocols:Modbus:AllowWrites` | `Protocols__Modbus__AllowWrites` | `false` | Second explicit opt-in, in addition to Registers:WritesEnabled. |
| `Protocols:ModbusEnabled` | `Protocols__ModbusEnabled` | unset | Legacy alias that overrides nested Enabled when present. |
| `Protocols:ModbusPort` | `Protocols__ModbusPort` | unset | Legacy alias that overrides nested Port when present. |

## OPC UA

Source: [options](../src/Protocols/OpcUa/OpcUaOptions.cs), [server](../src/Protocols/OpcUa/OpcUaServerHost.cs).

| Key | Environment variable | Default | Effect |
| --- | --- | --- | --- |
| `Protocols:OpcUa:Enabled` | `Protocols__OpcUa__Enabled` | `false` | Enable OPC UA server and simulation observer. |
| `Protocols:OpcUa:Port` | `Protocols__OpcUa__Port` | 4840 | TCP port; 1024–65535. |
| `Protocols:OpcUa:AllowAnonymous` | `Protocols__OpcUa__AllowAnonymous` | `false` | Opt into anonymous Viewer sessions and SecurityPolicy None. |
| `Protocols:OpcUa:PkiDirectory` | `Protocols__OpcUa__PkiDirectory` | `.scada/pki` | Certificate directory, relative to nearest solution directory or API content root fallback. |
| `Protocols:OpcUa:ApplicationName` | `Protocols__OpcUa__ApplicationName` | `SCADA Training Plant OPC UA` | Nonblank discovery/application name. |
| `Protocols:OpcUa:AutoAcceptUntrustedCertificates` | `Protocols__OpcUa__AutoAcceptUntrustedCertificates` | `false` | When true, automatically trusts client certificates permanently; local training/tests only. |

`Protocols:OpcUa:BindAddress` is planned **(added in Step 7)** and is not present on this branch. Current code binds `127.0.0.1`; the final key/default/validation must be reconciled after merging. See [deployment](deployment.md).

## Logging, telemetry and host

Source: [appsettings](../src/Api/appsettings.json), [observability settings](../src/Api/Observability/ObservabilitySettings.cs), [registration](../src/Api/Configuration/Registration/ObservabilityRegistrationExtensions.cs).

| Key | Environment variable | Default | Effect |
| --- | --- | --- | --- |
| `Observability:ServiceName` | `Observability__ServiceName` | `scada-api` | OpenTelemetry service name and Serilog Service property. |
| `Observability:JsonLogs` | `Observability__JsonLogs` | `false` | Compact JSON console logs; Compose sets true. |
| `Observability:OtlpEndpoint` | `Observability__OtlpEndpoint` | empty in JSON | Absolute OTLP/gRPC endpoint; empty/unset disables export. |
| `Serilog:MinimumLevel:Default` | `Serilog__MinimumLevel__Default` | `Information` | Default minimum log level. |
| `Serilog:MinimumLevel:Override:Microsoft.AspNetCore` | `Serilog__MinimumLevel__Override__Microsoft.AspNetCore` | `Warning` | ASP.NET Core log override. |
| `Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore` | `Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore` | `Warning` | EF Core log override. |
| `Serilog:MinimumLevel:Override:System.Net.Http.HttpClient` | `Serilog__MinimumLevel__Override__System.Net.Http.HttpClient` | `Warning` | HTTP client log override. |
| `AllowedHosts` | `AllowedHosts` | `localhost;127.0.0.1` | ASP.NET Core allowed HTTP Host names; configure when using another hostname. |

Host settings such as `DOTNET_ENVIRONMENT` and `ASPNETCORE_URLS` are standard .NET host environment variables, not custom sections. Without an explicit environment the host uses Production. The Dockerfile sets `ASPNETCORE_URLS=http://+:8080`; local examples pass `--urls http://127.0.0.1:5080`. Compose substitution variables and defaults are listed in [deployment](deployment.md).

## Topology and persisted rule settings

`Simulation:Host:TopologyFile` deserializes a `SimulationTopology` object with `plant`, `network` and `controllers`, using camelCase and string enums. It is a separate JSON document, not an additional configuration section. Its complete nested schema is defined by [SimulationTopology](../src/Application/Simulation/SimulationTopology.cs), [plant definitions](../src/Domain/Process/Configuration), [network definitions](../src/Domain/Network/NetworkDefinition.cs) and [ControllerDefinition](../src/Domain/Control/ControllerDefinition.cs); validation rejects invalid references and definitions. Start from the definitions in `DefaultPlantDefinition`, `DefaultControllerDefinitions` and `DefaultNetworkDefinition`.

Detection settings (`ruleId`, `enabled`, `severity`, `threshold`, `windowSeconds`, `cooldownSeconds`) are stored runtime records, not appsettings keys. Get their rule-specific defaults from `GET /api/detections/rules` and update through the Administrator API or Accounts & rules HMI. They are recovered from storage on startup.

