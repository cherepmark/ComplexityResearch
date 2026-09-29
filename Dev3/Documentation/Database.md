# База данных и кэш

## Файл базы данных

SQLite: `%LOCALAPPDATA%\ComplexityResearch\benchmark.db`
(путь виден в интерфейсе, блок «База данных и кэш»). Пакет — `Microsoft.Data.Sqlite`.

## Схема

```sql
CREATE TABLE Experiments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ConfigHash   TEXT NOT NULL,   -- ключ кэша (SHA-256)
    AlgorithmName TEXT NOT NULL,
    Complexity   TEXT NOT NULL,
    StartN       INTEGER NOT NULL,
    NMax         INTEGER NOT NULL,
    StepN        INTEGER NOT NULL,
    RunsPerPoint INTEGER NOT NULL,
    Seed         INTEGER NOT NULL,
    CostAddition REAL NOT NULL,   -- константы стоимости (параметры модели)
    CostMultiplication REAL NOT NULL,
    CostComparison     REAL NOT NULL,
    CostAssignment     REAL NOT NULL,
    CostSwap           REAL NOT NULL,
    CostArrayAccess    REAL NOT NULL,
    MSE          REAL,            -- итоги аппроксимации
    ApproximationC REAL,
    Conclusion   TEXT,
    CreatedUtc   TEXT NOT NULL);

CREATE TABLE Measurements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ExperimentId INTEGER NOT NULL REFERENCES Experiments(Id) ON DELETE CASCADE,
    N              INTEGER NOT NULL,   -- размер входных данных
    RunNumber      INTEGER NOT NULL,   -- номер отдельного запуска (1..K)
    ElapsedTimeNs  REAL NOT NULL,      -- время запуска (для операций-бенчмарка 0)
    StepCount      REAL NOT NULL,      -- количество элементарных операций
    TheoreticalNs  REAL NOT NULL,      -- теоретическое значение для этой точки
    ExperimentDate TEXT NOT NULL);     -- дата/время измерения (UTC)
```

Минимально требуемые поля — Algorithm, n, RunNumber, ElapsedTime, StepCount,
ExperimentDate — присутствуют; дополнительно хранятся параметры эксперимента
и теоретическое значение.

## Ключ кэша

```text
SHA-256 ( режим | алгоритм | класс сложности | StartN | Nmax | step |
          запусков | seed | block limit | 6 констант стоимости )
```

В хэш входят константы стоимости: после калибровки ключ меняется, и старые
результаты не считаются подходящими (теоретические значения изменились).
Режим («TIME» для времени, «OPS» для операций) не даёт смешивать данные
разных типов бенчмарка.

## Режимы работы

| Переключатель в GUI            | Поведение                                                              |
|--------------------------------|------------------------------------------------------------------------|
| Use cache ✓                    | точка, уже измеренная с этим ключом, берётся из БД (пометка «Из кэша») |
| Use cache ✗                    | все точки измеряются заново, но сохраняются в БД                        |
| Force recalculation ✓          | ВСЕ эксперименты с данным ключом удаляются ДО запуска — старые и новые данные не смешиваются |

Количество запусков в кэше должно совпадать с текущим `RunsPerPoint`,
иначе точка пересчитывается (нельзя сравнивать среднее по 3 и по 5 запускам).

## Гарантии

- БД пишется только из фонового потока бенчмарка (UI не блокируется);
- частичные результаты (отмена, таймаут) тоже сохраняются;
- `Measurements` хранит отдельные запуски — статистика может быть пересчитана
  из БД без повторных измерений;
- при Force recalculation удаление идёт по ключу целиком (`ON DELETE CASCADE`).
