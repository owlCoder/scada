# PostgreSQL складиште

Подразумевано се користи меморијско складиште. PostgreSQL се укључује експлицитно.

## Локално покретање

Из директоријума `deploy`:

```bash
export POSTGRES_PASSWORD="постави-јаку-лозинку"
docker compose up -d
```

Потом у терминалу за API:

```bash
export Storage__Provider=Postgres
export ConnectionStrings__Scada="Host=127.0.0.1;Port=5432;Database=scada;Username=scada;Password=$POSTGRES_PASSWORD"
export Security__CommandApiKey="постави-дугачак-случајан-кључ"
dotnet run --project src/Api --urls http://127.0.0.1:5080
```

SQL шема је у `deploy/postgres/init.sql`. Docker је примењује приликом првог стварања празног volume-а. Ако база већ постоји, шему је потребно применити посебно. Апликација не прави и не мења табеле аутоматски.

У PostgreSQL се чувају мерења и догађаји. Тренутно стање симулатора остаје у меморији и ресетује се приликом рестартовања. Писање у базу је синхроно; недоступност базе може да заустави ажурирање симулације док се не отклони грешка. За продукционо решење потребни су ред за упис, миграције, retention политика и мониторинг.

Немој излагати порт базе јавној мрежи.
