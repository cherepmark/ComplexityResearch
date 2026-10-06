using ComplexityResearch.Algorithms;
using ComplexityResearch.Models;

namespace ComplexityResearch.Services;

/// <summary>
/// Вход одного запуска двумерного матричного эксперимента:
/// A(n×m), B(m×n) и матрица результата C(n×n) — все выделены ВНЕ таймера.
/// </summary>
/// <param name="A">Матрица A размером n×m.</param>
/// <param name="B">Матрица B размером m×n.</param>
/// <param name="C">Матрица результата n×n.</param>
public sealed record MatrixNMInput(int[,] A, int[,] B, int[,] C);

/// <summary>
/// ДВУМЕРНЫЙ матричный бенчмарк «A(n×m)·B(m×n)».
/// </summary>
/// <remarks>
/// <para>
/// Для НАБОРА значений n и НАБОРА значений m (два независимых диапазона
/// со своими StartN/Nmax/step) перемножаются матрицы A(n×m)·B(m×n) и
/// собираются ВСЕ тройки (n, m, время) — без разбиения по m на отдельные
/// кривые. Результат визуализируется одной тепловой картой
/// (ось X = n, ось Y = m, цвет = время).
/// </para>
/// <para>
/// Это ОТДЕЛЬНЫЙ вид эксперимента (в БД — свой тег режима «MATNM» в ключе
/// кэша и колонка M в измерениях), не смешиваемый с обычным 2D-замером
/// квадратных матриц.
/// </para>
/// <para>
/// Методика та же, что в <see cref="BenchmarkService"/>: JIT-прогрев,
/// прогревочный запуск ячейки, данные/клоны матриц вне таймера,
/// отдельные запуски в БД, лимит времени ячейки, отмена.
/// </para>
/// </remarks>
public sealed class MatrixNMBenchmarkService
{
    /// <summary>Максимальное число значений по каждой оси (защита от слишком мелкого шага).</summary>
    public const int MaxPointsPerAxis = 40;

    /// <summary>Имя эксперимента для БД и интерфейса.</summary>
    public const string ExperimentName = "№9-3D. Умножение матриц A(n×m)·B(m×n)";

    /// <summary>Теоретическая сложность для записи в БД.</summary>
    public const string ExperimentComplexity = "O(n²·m)";

    /// <summary>Итог серии: тройки (n, m, время) + Id записи эксперимента в БД.</summary>
    public sealed record MatrixNMRunOutcome(IReadOnlyList<MatrixNMResult> Results, long ExperimentId);

    /// <summary>Результат подбора модели t = C·n²·m по всем тройкам.</summary>
    public sealed record MatrixNMFitResult(double C, double MSE, string Formula);

    /// <summary>
    /// МНК-подбор коэффициента C для модели t(n, m) = C·n²·m и расчёт MSE:
    /// C = Σ f·t / Σ f², MSE = (1/k)·Σ (t − C·f)².
    /// </summary>
    public static MatrixNMFitResult FitPowerNM(IReadOnlyList<MatrixNMResult> results)
    {
        double sumFT = 0, sumFF = 0;
        foreach (var r in results)
        {
            double f = r.N * (double)r.N * r.M;
            sumFT += f * r.Statistics.MeanNs;
            sumFF += f * f;
        }
        double c = sumFF > 0 ? sumFT / sumFF : 0;
        double mse = 0;
        foreach (var r in results)
        {
            double f = r.N * (double)r.N * r.M;
            double d = r.Statistics.MeanNs - c * f;
            mse += d * d;
        }
        mse /= Math.Max(1, results.Count);
        return new MatrixNMFitResult(c, mse, $"Tapprox(n, m) = {Format(c)}·n²·m");
    }

    private static string Format(double value) =>
        value >= 0.001 && value < 10000 ? value.ToString("0.000") : value.ToString("E3");

    /// <summary>
    /// Выполняет двумерную серию измерений и возвращает все тройки (n, m, время).
    /// </summary>
    public async Task<MatrixNMRunOutcome> RunAsync(
        BenchmarkConfiguration nRange,
        BenchmarkConfiguration mRange,
        OperationCostModel costModel,
        IProgress<BenchmarkProgress>? progress,
        CancellationToken ct,
        BenchmarkDatabase? database = null,
        bool useCache = true,
        bool forceRecalculation = false)
    {
        Validate(nRange, mRange);
        return await Task.Run(() =>
        {
            long[] nSizes = BenchmarkService.GenerateSizes(nRange);
            long[] mSizes = BenchmarkService.GenerateSizes(mRange);
            int cells = nSizes.Length * mSizes.Length;
            long totalWork = (long)cells * (nRange.RunsPerPoint + 1);
            long doneWork = 0;

            string configHash = string.Empty;
            long experimentId = 0;
            if (database != null)
            {
                configHash = BenchmarkDatabase.ComputeMatrixNMHash(nRange, mRange, costModel);
                if (forceRecalculation)
                {
                    database.DeleteConfigData(configHash);
                }
                experimentId = database.BeginExperiment(
                    configHash, ExperimentName, ExperimentComplexity, nRange, costModel);
            }

            // JIT-прогрев на минимальной ячейке (вне результатов).
            int warmN = (int)Math.Clamp(nSizes[0], 1, 200);
            int warmM = (int)Math.Clamp(mSizes[0], 1, 200);
            RunCell(warmN, warmM);
            doneWork++;

            var results = new List<MatrixNMResult>(cells);
            int cellIndex = 0;
            foreach (long n in nSizes)
            {
                foreach (long m in mSizes)
                {
                    ct.ThrowIfCancellationRequested();
                    cellIndex++;

                    IReadOnlyList<double> times;
                    bool fromCache = false;

                    // Кэш: ячейка (n, m) уже измерялась с этой конфигурацией?
                    if (database != null && useCache && !forceRecalculation &&
                        database.TryGetCachedRuns(configHash, n, m, out var cachedRuns) &&
                        cachedRuns.Count == nRange.RunsPerPoint)
                    {
                        times = cachedRuns;
                        fromCache = true;
                        doneWork += nRange.RunsPerPoint + 1;
                    }
                    else
                    {
                        var measured = new List<double>(nRange.RunsPerPoint);
                        var shared = PrepareInput(n, m);
                        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                        GC.WaitForPendingFinalizers();

                        // r = 0 — прогревочный запуск ячейки.
                        for (int run = 0; run <= nRange.RunsPerPoint; run++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var input = PrepareRunInput(shared);
                            double ns = BenchmarkTimer.MeasureNs(() => MultiplyCell(input));
                            doneWork++;

                            if (run > 0 && nRange.MaxSecondsPerPoint > 0 && ns > nRange.MaxSecondsPerPoint * 1e9)
                            {
                                throw new PointTimeoutException(n * m, ns, nRange.MaxSecondsPerPoint);
                            }

                            if (run > 0)
                            {
                                measured.Add(ns);
                                database?.SaveMeasurement(
                                    experimentId, n, run, ns,
                                    stepCount: MatrixMultiplicationAlgorithm.EstimateOperationCountsNM(n, m).Total,
                                    theoreticalNs: costModel.TotalTimeNs(
                                        MatrixMultiplicationAlgorithm.EstimateOperationCountsNM(n, m)),
                                    m: m);
                            }

                            progress?.Report(new BenchmarkProgress(
                                ExperimentName, n, cellIndex - 1, cells, run, nRange.RunsPerPoint,
                                (int)(doneWork * 100 / Math.Max(1, totalWork)), m));
                        }

                        times = measured;
                        shared = null!; // освобождаем матрицы до GC-паузы следующей ячейки
                    }

                    results.Add(new MatrixNMResult
                    {
                        N = n,
                        M = m,
                        RunsCount = nRange.RunsPerPoint,
                        RunTimesNs = times.ToArray(),
                        Statistics = StatisticsService.Compute(times, MatrixMultiplicationAlgorithm.EstimateOperationCountsNM(n, m), costModel),
                        IsFromCache = fromCache
                    });
                }
            }

            return new MatrixNMRunOutcome(results, experimentId);
        }, ct);
    }

    private static void RunCell(int n, int m)
    {
        var a = new int[n, m];
        var b = new int[m, n];
        var c = new int[n, n];
        var rng = DataPreparationService.CreateRandom(20260914);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                a[i, j] = rng.Next(-100, 101);
                b[j, i] = rng.Next(-100, 101);
            }
        }
        MatrixMultiplicationAlgorithm.MultiplyNM(a, b, c);
    }

    private static MatrixNMInput PrepareInput(long n, long m)
    {
        int rows = (int)n, inner = (int)m;
        var rng = DataPreparationService.CreateRandom(20260914 + 13);
        var a = new int[rows, inner];
        var b = new int[inner, rows];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < inner; j++)
            {
                a[i, j] = rng.Next(-100, 101);
                b[j, i] = rng.Next(-100, 101);
            }
        }
        return new MatrixNMInput(a, b, new int[rows, rows]);
    }

    private static MatrixNMInput PrepareRunInput(MatrixNMInput shared) =>
        new((int[,])shared.A.Clone(), (int[,])shared.B.Clone(), new int[shared.A.GetLength(0), shared.A.GetLength(0)]);

    private static void MultiplyCell(MatrixNMInput input)
    {
        MatrixMultiplicationAlgorithm.MultiplyNM(input.A, input.B, input.C);
        PreventOptimization(input.C[0, 0]);
    }

    /// <summary>Защита от удаления результата JIT-ом (копия приёма AlgorithmBase).</summary>
    private static long _sink;

    private static void PreventOptimization(long value) => _sink = value;

    /// <summary>
    /// Проверка конфигурации двух осей и защита от слишком большой сетки
    /// (число ячеек ≤ MaxPointsPerAxis²).
    /// </summary>
    public static void Validate(BenchmarkConfiguration nRange, BenchmarkConfiguration mRange)
    {
        BenchmarkService.ValidateConfigurationPublic(nRange);
        BenchmarkService.ValidateConfigurationPublic(mRange);
        long nCount = (nRange.EndN - nRange.StartN) / nRange.StepN + 2; // + Nmax
        long mCount = (mRange.EndN - mRange.StartN) / mRange.StepN + 2;
        if (nCount > MaxPointsPerAxis || mCount > MaxPointsPerAxis)
        {
            throw new ArgumentException(
                $"Слишком много значений по оси: максимум {MaxPointsPerAxis} по n и по m. Увеличьте шаг.");
        }
    }
}
