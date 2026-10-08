# Nova

Учебный проект необанка: API, веб-сайт, приложение для ПК и Android, админка сотрудников и панель владельца.
**Не является кредитной организацией.**

## Стек

- .NET 9, C#
- ASP.NET Core Minimal API
- EF Core 9 + PostgreSQL 17 (Npgsql)
- xUnit
- Docker Compose
- Позже: .NET MAUI (приложение), Blazor (админка)

## Структура

```
Nova.sln
├─ src/
│  ├─ Nova.Domain          сущности, бизнес-правила, роли и права (без зависимостей)
│  ├─ Nova.Infrastructure  EF Core: DbContext, конфигурации таблиц, миграции
│  └─ Nova.Api             HTTP API
├─ tests/
│  └─ Nova.Tests           unit-тесты домена
└─ docker-compose.yml      PostgreSQL (+ pgAdmin по профилю tools)
```

## Ключевые решения

| Решение | Зачем |
|---|---|
| `numeric(19,4)` для всех денег | Никаких ошибок округления float |
| Двойная запись (`ledger_entries`) | Каждый перевод — дебет + кредит, баланс сверяется по проводкам |
| `idempotency_key` в переводах | Повторный запрос не спишет деньги дважды |
| `xmin` как row version | Оптимистическая блокировка счёта при параллельных переводах |
| CHECK `balance >= -credit_limit` | Уход в минус невозможен даже при баге в коде |
| Схемы `clients / accounts / transactions / staff / audit` | Права в БД выдаются на домен целиком |
| Права, а не роли, в проверках | Роль — набор прав, меняется без правки кода |
| Аудит-лог с цепочкой SHA-256 | Удаление или правка записи обнаруживается автоматически |
| Владелец без права `rates.approve` | Принцип четырёх глаз: ставки владельца подтверждает бухгалтерия |

## Запуск

Нужно: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) и Docker Desktop.

```powershell
# 1. Инструмент миграций (один раз)
dotnet tool install --global dotnet-ef

# 2. База данных
copy .env.example .env
docker compose up -d

# 3. Первая миграция
dotnet ef migrations add Init -p src/Nova.Infrastructure -s src/Nova.Api -o Persistence/Migrations

# 4. API (миграции накатятся автоматически в Development)
dotnet run --project src/Nova.Api

# 5. Тесты
dotnet test
```

Проверка:

| URL | Что показывает |
|---|---|
| http://localhost:5080/health | Состояние API и БД |
| http://localhost:5080/api/system/stats | Количество записей по таблицам |
| http://localhost:5080/api/system/roles | Матрица ролей и прав |
| http://localhost:5080/api/system/audit/verify | Целостность аудит-лога |
| http://localhost:5080/openapi/v1.json | OpenAPI-спецификация |

pgAdmin: `docker compose --profile tools up -d` → http://localhost:5050 (admin@nova.local / admin).

## План

- [x] Неделя 1 — солюшн, Docker, модель БД, роли, аудит, тесты домена
- [ ] Неделя 2 — генератор данных (200 тыс. клиентов, 5 млн операций), партиционирование, индексы
- [ ] Неделя 3 — регистрация, вход, JWT, 2FA
- [ ] Неделя 4 — переводы, выписка
- [ ] Неделя 5 — приложение MAUI
- [ ] Неделя 6 — сайт, админка, README со скриншотами
