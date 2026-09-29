namespace ComplexityResearch.Models;

/// <summary>
/// Одна строка экспортируемого отчёта (CSV/JSON).
/// Отдельный сериализуемый класс: BenchmarkResult содержит вложенные
/// вычислимые свойства, а отчёту нужен плоский формат.
/// </summary>
public sealed class ExperimentRow
{
    /// <summary>Логический размер входных данных n.</summary>
    public long N { get; set; }

    /// <summary>Режим измерения: Direct / Scaled.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Количество учитываемых запусков.</summary>
    public int Runs { get; set; }

    /// <summary>Среднее время, нс.</summary>
    public double MeanNs { get; set; }

    /// <summary>Минимальное время, нс.</summary>
    public double MinNs { get; set; }

    /// <summary>Максимальное время, нс.</summary>
    public double MaxNs { get; set; }

    /// <summary>Стандартное отклонение, нс.</summary>
    public double StdDevNs { get; set; }

    /// <summary>Количество элементарных операций (суммарно).</summary>
    public double OperationsTotal { get; set; }

    /// <summary>Теоретическое время, нс.</summary>
    public double TheoreticalNs { get; set; }

    /// <summary>Абсолютная ошибка, нс.</summary>
    public double AbsDiffNs { get; set; }

    /// <summary>Относительная ошибка, %.</summary>
    public double RelDiffPercent { get; set; }
}

/// <summary>
/// Полный отчёт об эксперименте: сохраняется в CSV и JSON.
/// </summary>
public sealed class ExperimentReport
{
    /// <summary>Название алгоритма.</summary>
    public string AlgorithmName { get; set; } = string.Empty;

    /// <summary>Теоретическая сложность алгоритма.</summary>
    public string Complexity { get; set; } = string.Empty;

    /// <summary>Дата и время проведения эксперимента (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Конфигурация эксперимента.</summary>
    public BenchmarkConfiguration Configuration { get; set; } = new();

    /// <summary>Использованные константы стоимости операций, нс.</summary>
    public OperationCostModel CostModel { get; set; } = new();

    /// <summary>Пояснение о режимах измерения, встречающихся в отчёте.</summary>
    public string MeasurementModeNote { get; set; } = string.Empty;

    /// <summary>Автоматический вывод по результатам эксперимента.</summary>
    public string Conclusion { get; set; } = string.Empty;

    /// <summary>Строки результатов по каждой точке n.</summary>
    public List<ExperimentRow> Rows { get; set; } = new();
}
