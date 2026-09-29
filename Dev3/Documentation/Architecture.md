# Архитектура приложения

## Общий вид (MVVM)

```text
┌──────────────────────────── WPF (net9.0-windows) ───────────────────────────┐
│                                                                             │
│  Views/MainWindow.xaml ── привязки ──► ViewModels/MainViewModel             │
│        │                                   │                                │
│        │ (ChartControl, экспорт PNG)       │ команды/прогресс/отмена         │
│        ▼                                   ▼                                │
│  Controls/ChartControl               Services                               │
│  (собственный график)                ├── BenchmarkService      ──┐          │
│                                      ├── OperationBenchmarkService│          │
│                                      ├── StatisticsService      │          │
│                                      ├── ApproximationService   │          │
│                                      ├── CalibrationService     │          │
│                                      ├── DataPreparationService │          │
│                                      ├── ExportService          │          │
│                                      └── BenchmarkDatabase (SQLite)◄┘        │
│                                                   │                          │
│  Models (POCO, без логики UI)                     ▼                          │
│  ├── BenchmarkConfiguration  (Nmax, step, runs, seed, лимит времени)        │
│  ├── BenchmarkResult / StatisticsResult (агрегаты + отдельные запуски)      │
│  ├── OperationCounts / OperationCostModel (теоретическая модель)            │
│  └── OperationsPoint, ExperimentReport, ChartData, …                        │
│                                                                             │
│  Algorithms                                                                 │
│  ├── AlgorithmBase (абстрактный) ◄── 10 алгоритмов временного бенчмарка     │
│  ├── AlgorithmRegistry (единая точка регистрации)                           │
│  └── IOperationCountedAlgorithm ◄── 3 алгоритма возведения в степень        │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Поток выполнения эксперимента

1. `MainViewModel.RunExperimentAsync` — валидация полей, запуск в `Task.Run`
   (UI не блокируется), `IProgress<BenchmarkProgress>` + `CancellationToken`.
2. `BenchmarkService.RunAsync`:
   - хэш конфигурации (`BenchmarkDatabase.ComputeConfigHash`);
   - Force recalculation → удаление старых данных ключа;
   - JIT-прогрев на малом n;
   - для каждой точки: кэш БД ИЛИ измерение (данные вне таймера,
     прогревочный запуск, отдельные запуски → `Stopwatch` → БД);
   - лимит времени точки (`PointTimeoutException`).
3. `ApproximationService.Fit` — кривая Tapprox(n) = C·f(n), MSE, R².
4. `MainViewModel` — серии графика (эксперимент/теория/аппроксимация),
   MSE в GUI, авторский вывод, итоги (MSE, C) в БД.
5. Экспорт CSV/JSON, график → PNG.

## Принцип расширения

Новый алгоритм = класс-наследник `AlgorithmBase` + одна строка в
`AlgorithmRegistry`. Сервисы, график, таблица, БД и экспорт не меняются
(принцип открытости/закрытости).

## Потокобезопасность

- Эксперимент — в фоновом потоке; UI-обновления — через `Progress<T>`
  и `Dispatcher.BeginInvoke` (коллекция `Results` меняется только в UI-потоке).
- SQLite: короткоживущие соединения (одна операция — одно соединение),
  пул отключён; обращения к БД — только из фонового потока бенчмарка.

## Тесты

`ComplexityResearch.Tests` (xunit): корректность алгоритмов и сортировок,
матрицы (эталон и тождественная матрица), многочлены (naive = Horner = Math.Pow),
возведение в степень (результат + счётчик операций), аппроксимация/MSE
(ручной расчёт), БД/кэш (roundtrip, свежесть, force recalculation), генерация
точек, интеграция benchmark+БД, отмена.
