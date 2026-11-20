# PostgreSQL и локални Docker Compose

Подразумевано се користи меморијско складиште. PostgreSQL је опционалан.

## Цео систем у Docker-у

Из корена репозиторијума:

```bash
export POSTGRES_PASSWORD="замени-јаком-лозинком"
export SCADA_VIEWER_KEY="замени-дугачким-случајним-кључем"
export SCADA_OPERATOR_KEY="замени-другим-случајним-кључем"
export SCADA_ADMIN_KEY="замени-трећим-случајним-кључем"
docker compose -f deploy/compose.yaml up --build -d
```

Контролна табла: `http://127.0.0.1:5080/`.

Сервиси су везани за loopback интерфејс домаћина. Не излагати портове јавној мрежи.

## Локални .NET процес уз PostgreSQL

Могуће је покренути само базу:

```bash
docker compose -f deploy/compose.yaml up -d postgres
export Storage__Provider=Postgres
export ConnectionStrings__Scada="Host=127.0.0.1;Port=5432;Database=scada;Username=scada;Password=$POSTGRES_PASSWORD"
export Security__ViewerApiKey="замени-дугачким-случајним-кључем"
export Security__OperatorApiKey="замени-другим-случајним-кључем"
export Security__AdminApiKey="замени-трећим-случајним-кључем"
dotnet run --project src/Api --urls http://127.0.0.1:5080
```

## Шема и ограничења

SQL шема је у `deploy/postgres/init.sql`. Docker је примењује само приликом првог стварања празног volume-а. За постојећу базу шему треба применити ручно. Апликација не прави миграције.

Мерења, догађаји и инциденти са тренутним статусом су трајни, али тренутно стање постројења остаје у меморији. Упис у базу је синхрон и може да успори симулацију ако база није доступна. За производно окружење неопходни су миграције, ретенција, retry механизам и поуздана редна обрада.
