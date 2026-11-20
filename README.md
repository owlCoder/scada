# SCADA — симулатор индустријског система

Симулатор постројења за воду са надзором телеметрије, алармима и детекцијом безбедносних догађаја.

## Покретање

Потребан је .NET 10 SDK.

```bash
dotnet run --project src/Api
dotnet test Tests/UnitTests
```

API: `GET /api/plant`, `GET /api/events`, `POST /api/plant/tick`, `POST /api/plant/pump/{id}/command`.

Команде су искључиво виртуелне. Апликација не управља стварним индустријским уређајима.

## Структура

- `src/Domain` — модели и правила постројења
- `src/Application` — сервиси и уговори
- `src/Infrastructure` — складиште догађаја
- `src/Simulation` — симулација физичког процеса
- `src/Security` — детекција безбедносних догађаја
- `src/Api` — HTTP интерфејс
- `Tests/UnitTests` — јединични тестови

Пројекат користи .NET 10, dependency injection и јасно раздвојене одговорности.
