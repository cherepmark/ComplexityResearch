# Участник Dev3 — кастомный алгоритм №12 и документация

## Мой вклад в проект

### Алгоритм (кастомный)
| Файл | Алгоритм | Сложность |
|------|----------|-----------|
| `Algorithms/ReverseArrayAlgorithm.cs` | №12. Индивидуальный: реверс массива двумя указателями | O(n) |

Оборот массива на месте: n/2 обменов, движение указателей от краёв к центру;
масштабированный режим для n > 50 млн (блок 50 млн элементов, проходы ∝ n).

### Документация (вся)
| Файл | Содержание |
|------|-----------|
| `README.md` | Описание проекта, сборка/запуск/тесты, методика, БД и кэш |
| `Documentation/Algorithms.md` | Все 12 алгоритмов: описание, идея, псевдокод, Best/Average/Worst, операции, применение, плюсы/минусы |
| `Documentation/Architecture.md` | Архитектура MVVM, поток выполнения, потокобезопасность |
| `Documentation/Database.md` | Схема SQLite, ключ кэша, режимы Use cache / Force recalculation |
| `Documentation/BenchmarkMethodology.md` | Методика измерений, эмпирический Nmax (5–10 с), таймауты |
| `Documentation/flowcharts.md` | Блок-схемы (БСА) всех 12 алгоритмов |
| `Documentation/Report.md` | Шаблон отчёта по лабораторной (16 разделов) |

### Тесты
- `Tests/ReverseTests.cs` — реверс против `Array.Reverse`, инволютивность (двойной реверс), линейный рост оценки операций.

## Интеграция со скелетом Dev1

Алгоритм наследуется от `AlgorithmBase` (скелет Dev1) и регистрируется
в `Algorithms/AlgorithmRegistry.cs` одной строкой:

```csharp
new ReverseArrayAlgorithm()   // №12
```

Зависимости от скелета: `AlgorithmBase`, `OperationCounts`, `BenchmarkConfiguration`,
`StreamInput` (объявлен в `SumAlgorithm.cs` — используется для масштабированного
режима), `DataPreparationService`, `PreventOptimization`.
