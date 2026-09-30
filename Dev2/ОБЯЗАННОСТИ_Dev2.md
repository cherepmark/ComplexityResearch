# Участник Dev2 — дополнительный код и кастомные алгоритмы №10, №11

## Мой вклад в проект

### Дополнительный код (сервисы и модели)
| Файл | Что делает |
|------|-----------|
| `Services/ApproximationService.cs` | Аппроксимация Tapprox(n) = C·f(n) методом наименьших квадратов (кривая через начало координат), расчёт MSE и R² |
| `Services/ExportService.cs` | Экспорт результатов эксперимента в CSV и JSON (включая отдельные запуски) |
| `Services/OperationBenchmarkService.cs` | Операционный бенчмарк возведения в степень: n = 1..1000, измеряется количество элементарных операций |
| `Models/OperationsPoint.cs` | Точка операционного бенчмарка (n, измерено операций, теория) |
| `Models/ExperimentReport.cs` | Отчёт для экспорта (строки + метаданные + MSE) |
| `Models/ChartData.cs` | DTO серий графика между ViewModel и ChartControl |
| `Models/AlgorithmMetadata.cs` | Метаданные алгоритма для панели интерфейса |
| `Models/ComplexityPoint.cs` | Пара (n, время) для аппроксимации |

### Алгоритмы (кастомные)
| Файл | Алгоритм | Сложность |
|------|----------|-----------|
| `Algorithms/CustomAlgorithm.cs` | №10. Индивидуальный: быстрое возведение в степень | O(log n) |
| `Algorithms/KadaneAlgorithm.cs` | №11. Индивидуальный: алгоритм Кадане (максимальная сумма подмассива) | O(n) |

### Тесты
- `Tests/KadaneTests.cs` — корректность Кадане против эталонного перебора, все-отрицательные массивы, линейный рост оценки операций.

## Интеграция со скелетом Dev1

Мои алгоритмы наследуются от `AlgorithmBase` (скелет Dev1) и регистрируются
в `Algorithms/AlgorithmRegistry.cs` двумя строками:

```csharp
new CustomAlgorithm(),   // №10
new KadaneAlgorithm(),   // №11
```

Зависимости от скелета: `AlgorithmBase`, `OperationCounts`, `BenchmarkConfiguration`,
`StreamInput` (объявлен в `SumAlgorithm.cs` — используется Кадане для
масштабированного режима), `DataPreparationService`, `PreventOptimization`.

Мои сервисы (Approximation/Export/OperationBenchmark) в итоговой сборке
вызываются из `MainViewModel` — сами файлы сервиса уже интегрированы в скелет Dev1.
