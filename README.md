# SCADA — симулатор индустријског система

Модуларна .NET 10 апликација за локалну симулацију постројења за воду, телеметрију, безбедносне догађаје и управљање инцидентима. Нема повезивања са стварним PLC уређајима.

## Покретање

За покретање API-ја и PostgreSQL базе у Docker-у види [упутство за Docker Compose](docs/postgresql.md).

Потребан је .NET 10 SDK.

```bash
dotnet restore scada.slnx
dotnet test scada.slnx
export Security__ViewerApiKey="замени-дугачким-случајним-кључем"
export Security__OperatorApiKey="замени-другим-случајним-кључем"
export Security__AdminApiKey="замени-трећим-случајним-кључем"
dotnet run --project src/Api --urls http://127.0.0.1:5080
```

Контролна табла: `http://127.0.0.1:5080/`. Унеси један од конфигурисаних API кључева у поље за повезивање. Сваки API захтев, осим провере здравља, захтева `X-Api-Key`.

## Модули

| Путања | Намена |
| --- | --- |
| `src/Domain` | Резервоар, пумпа, вентил и процесна правила |
| `src/Application` | Сервиси, телеметрија и инциденти |
| `src/Infrastructure` | Меморијско и PostgreSQL складиште |
| `src/Simulation` | Позадинска симулација и надзор догађаја |
| `src/Security` | Улоге, API кључеви и надзор команди |
| `src/Protocols` | Виртуелна регистарска мапа |
| `src/Api` | HTTP API и контролна табла |
| `Tests/UnitTests` | Јединични тестови |
| `Tests/IntegrationTests` | Интеграциони тестови |
| `Tests/ArchitectureTests` | Провере зависности између модула |

## API

| Метода | Путања | Минимална улога |
| --- | --- | --- |
| GET | `/health` | Без кључа |
| GET | `/api/plant` | Viewer |
| GET | `/api/telemetry?count=100` | Viewer |
| GET | `/api/events` | Viewer |
| GET | `/api/incidents` | Viewer |
| GET | `/api/scenarios` | Viewer |
| GET | `/api/registers` | Viewer |
| GET | `/api/registers/{address}` | Viewer |
| POST | `/api/plant/tick` | Operator |
| POST | `/api/plant/pump/P-101/command` | Operator |
| POST | `/api/plant/valve/V-101/command` | Operator |
| POST | `/api/registers/{address}` | Operator |
| POST | `/api/incidents/{id}/status` | Administrator |
| POST | `/api/scenarios/{id}` | Administrator |

## Документација

- [Архитектура](docs/arhitektura.md)
- [Безбедност и улоге](docs/bezbednost.md)
- [PostgreSQL складиште](docs/postgresql.md)
- [Виртуелни регистри](docs/protokoli.md)
- [Инциденти](docs/incidenti.md)
- [Синтетички безбедносни сценарији](docs/scenariji.md)

## Ограничења

Постоји опциони Modbus TCP сервер само за читање, ограничен на 127.0.0.1. OPC UA није имплементиран. Нема индивидуалних корисничких налога, OIDC пријаве, трајног складишта статуса инцидената ни производног система за обраду редова. PostgreSQL је опционалан; подразумевано се користи меморија. API кључеви и веб табла намењени су искључиво изолованом развојном окружењу.
