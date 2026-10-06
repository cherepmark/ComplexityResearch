namespace ComplexityResearch.Models;

/// <summary>
/// Прогресс выполнения бенчмарка (для ProgressBar и статусной строки).
/// </summary>
/// <param name="AlgorithmName">Название алгоритма/эксперимента.</param>
/// <param name="CurrentN">Текущее n.</param>
/// <param name="PointIndex">Индекс точки (с нуля).</param>
/// <param name="PointCount">Всего точек.</param>
/// <param name="RunIndex">Номер запуска (0 — прогревочный).</param>
/// <param name="RunCount">Всего учитываемых запусков.</param>
/// <param name="PercentComplete">Процент выполнения.</param>
/// <param name="CurrentM">
/// Текущее m — только для двумерного эксперимента A(n×m)·B(m×n);
/// для обычных одномерных замеров null.
/// </param>
public sealed record BenchmarkProgress(
    string AlgorithmName,
    long CurrentN,
    int PointIndex,
    int PointCount,
    int RunIndex,
    int RunCount,
    int PercentComplete,
    long? CurrentM = null)
{
    /// <summary>Готовый текст статуса.</summary>
    public string DetailsText
    {
        get
        {
            string point = CurrentM.HasValue
                ? $"n = {CurrentN:N0}, m = {CurrentM:N0} | ячейка {PointIndex + 1}/{PointCount}"
                : $"n = {CurrentN:N0} | точка {PointIndex + 1}/{PointCount}";
            string run = RunIndex == 0 ? "прогрев" : $"запуск {RunIndex}/{RunCount}";
            return $"{AlgorithmName} | {point} | {run}";
        }
    }
}
