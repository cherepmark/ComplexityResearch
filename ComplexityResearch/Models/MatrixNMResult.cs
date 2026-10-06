using ComplexityResearch.Algorithms;

namespace ComplexityResearch.Models;

/// <summary>
/// Результат одной ячейки эксперимента «A(n×m)·B(m×n)»: тройка
/// (n, m, время) со статистикой по запускам.
/// </summary>
public sealed class MatrixNMResult
{
    /// <summary>Число строк матрицы A.</summary>
    public long N { get; init; }

    /// <summary>Внутреннее измерение (столбцы A = строки B).</summary>
    public long M { get; init; }

    /// <summary>Количество учитываемых запусков (без прогревочного).</summary>
    public int RunsCount { get; init; }

    /// <summary>Отдельные запуски, нс.</summary>
    public IReadOnlyList<double> RunTimesNs { get; init; } = Array.Empty<double>();

    /// <summary>Статистика времени.</summary>
    public StatisticsResult Statistics { get; init; } = new();

    /// <summary>Результат взят из кэша БД.</summary>
    public bool IsFromCache { get; init; }

    /// <summary>Количество элементарных операций n²·m (суммарно).</summary>
    public long OperationsTotal => (long)Math.Round(
        MatrixMultiplicationAlgorithm.EstimateOperationCountsNM(N, M).Total);

    /// <summary>Среднее время, мс.</summary>
    public double AverageMs => Statistics.MeanNs / 1e6;
}
