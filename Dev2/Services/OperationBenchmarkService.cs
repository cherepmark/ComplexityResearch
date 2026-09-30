using ComplexityResearch.Algorithms;
using ComplexityResearch.Models;

namespace ComplexityResearch.Services;

/// <summary>
/// Результат операционного бенчмарка одного алгоритма возведения в степень.
/// </summary>
/// <param name="Algorithm">Алгоритм.</param>
/// <param name="Points">Точки (n, измерено операций, теория).</param>
/// <param name="Approximation">Аппроксимация C·f(n) по измеренным операциям.</param>
public sealed record OperationBenchmarkResult(
    IOperationCountedAlgorithm Algorithm,
    IReadOnlyList<OperationsPoint> Points,
    ApproximationResult Approximation);

/// <summary>
/// «Операционный» бенчмарк возведения в степень: для n = 1..1000 измеряется
/// НЕ время, а количество элементарных операций, подсчитанное счётчиком
/// прямо во время выполнения алгоритма.
/// </summary>
/// <remarks>
/// Три алгоритма: итеративный O(n), рекурсивный O(n), бинарный O(log n).
/// Выполняется очень быстро (миллисекунды), поэтому операция выполняется
/// синхронно в фоновом потоке; результаты сохраняются в БД
/// (ElapsedTime = 0, StepCount = измеренное число операций).
/// </remarks>
public sealed class OperationBenchmarkService
{
    private const string ModeTag = "OPS";

    /// <summary>
    /// Выполняет операционный бенчмарк для всех алгоритмов возведения в степень.
    /// </summary>
    /// <param name="config">Конфигурация точек n (обычно StartN=1, Nmax=1000, шаг 50).</param>
    /// <param name="progress">Прогресс (может быть null).</param>
    /// <param name="ct">Токен отмены.</param>
    /// <param name="database">БД (null — без сохранения).</param>
    /// <param name="useCache">Использовать кэш.</param>
    /// <param name="forceRecalculation">Принудительный пересчёт.</param>
    public async Task<List<OperationBenchmarkResult>> RunAsync(
        BenchmarkConfiguration config,
        IProgress<BenchmarkProgress>? progress,
        CancellationToken ct,
        BenchmarkDatabase? database = null,
        bool useCache = true,
        bool forceRecalculation = false)
    {
        BenchmarkService.ValidateConfigurationPublic(config);
        return await Task.Run(() =>
        {
            var sizes = BenchmarkService.GenerateSizes(config);
            var algorithms = AlgorithmRegistry.CreatePowerAlgorithms();
            var results = new List<OperationBenchmarkResult>(algorithms.Count);
            int done = 0, total = algorithms.Count * sizes.Length;

            foreach (var algorithm in algorithms)
            {
                ct.ThrowIfCancellationRequested();

                string hash = string.Empty;
                long experimentId = 0;
                if (database != null)
                {
                    hash = BenchmarkDatabase.ComputeConfigHash(
                        new OperationAlgorithmAdapter(algorithm), config, OperationCostModel.CreateDefault(), ModeTag);
                    if (forceRecalculation)
                    {
                        database.DeleteConfigData(hash);
                    }
                    // Операционный бенчмарк не зависит от констант стоимости —
                    // используем нейтральные значения в хэше (см. выше) и записи.
                    experimentId = database.BeginExperiment(hash,
                        new OperationAlgorithmAdapter(algorithm), config, OperationCostModel.CreateDefault());
                }

                var points = new List<OperationsPoint>(sizes.Length);
                foreach (long n in sizes)
                {
                    ct.ThrowIfCancellationRequested();
                    int exponent = (int)Math.Clamp(n, 1, 1_000_000);

                    double measured;
                    if (database != null && useCache && !forceRecalculation &&
                        database.TryGetCachedOps(hash, exponent, out var cachedOps))
                    {
                        measured = cachedOps;
                    }
                    else
                    {
                        measured = algorithm.ComputeOperationCount(exponent);
                        database?.SaveMeasurement(experimentId, exponent, runNumber: 1,
                            elapsedNs: 0, stepCount: measured,
                            theoreticalNs: algorithm.TheoreticalOperationCount(exponent));
                    }

                    points.Add(new OperationsPoint(exponent, measured, algorithm.TheoreticalOperationCount(exponent)));
                    done++;
                    progress?.Report(new BenchmarkProgress(
                        algorithm.Name, exponent, done / Math.Max(1, sizes.Length), algorithms.Count,
                        done % Math.Max(1, sizes.Length), sizes.Length,
                        done * 100 / total));
                }

                var fit = ApproximationService.Fit(
                    points.Select(p => (p.N, p.MeasuredOperations)).ToList(),
                    algorithm.ComplexityClass);

                database?.UpdateExperimentSummary(experimentId, fit.MSE, fit.C, fit.Formula);
                results.Add(new OperationBenchmarkResult(algorithm, points, fit));
            }

            return results;
        }, ct);
    }

    /// <summary>
    /// Адаптер: позволяет использовать алгоритмы возведения в степень
    /// в методах, ожидающих AlgorithmBase (вычисление хэша, запись шапки).
    /// </summary>
    private sealed class OperationAlgorithmAdapter : AlgorithmBase
    {
        private readonly IOperationCountedAlgorithm _inner;

        public OperationAlgorithmAdapter(IOperationCountedAlgorithm inner) => _inner = inner;

        public override string Name => _inner.Name;
        public override string Description => _inner.Description;
        public override string Complexity => _inner.Complexity;
        public override string ComplexityClass => _inner.ComplexityClass;
        public override string Application => "Возведение в степень";
        public override string TheoreticalFormula => _inner.TheoreticalFormula;
        public override BenchmarkConfiguration DefaultConfiguration => new();
        public override MeasurementMode ModeFor(long n) => MeasurementMode.Direct;
        public override object PrepareInput(long n) => n;
        protected override void ExecuteCore(object input) { }
        public override OperationCounts EstimateOperationCounts(long n) =>
            new(0, 0, 0, 0, 0, 0);
        public override Func<double, double> ComplexityFunction =>
            ApproximationService.GetComplexityFunction(_inner.ComplexityClass);
    }
}
