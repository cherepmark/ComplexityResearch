using ComplexityResearch.Algorithms;
using ComplexityResearch.Models;

namespace ComplexityResearch.Services;

/// <summary>
/// Сервис выполнения серии измерений алгоритма.
/// </summary>
/// <remarks>
/// <para>
/// Методика эксперимента:
/// 1) JIT-прогрев всего алгоритма на малом n (вне результатов);
/// 2) точки n генерируются линейно: StartN, StartN+StepN, …, Nmax (всегда включён);
/// 3) для каждой точки данные генерируются ВНЕ таймера;
/// 4) первый запуск точки — прогревочный, не учитывается;
/// 5) измеряется только ExecuteCore (Stopwatch);
/// 6) каждый учитываемый запуск сохраняется в БД отдельной строкой
///    (RunNumber, ElapsedTime, StepCount, ExperimentDate);
/// 7) кэш: при включённом «Use cache» точка, уже измеренная с той же
///    конфигурацией (хэш алгоритм+n+параметры), берётся из БД;
/// 8) «Force recalculation» удаляет старые данные ключа — старые и новые
///    результаты не смешиваются;
/// 9) лимит времени точки (MaxSecondsPerPoint): превышение останавливает
///    эксперимент с понятным сообщением (защита от зависания).
/// </para>
/// </remarks>
public sealed class BenchmarkService
{
    /// <summary>Максимальное число точек в серии (защита от слишком малого шага).</summary>
    public const int MaxPoints = 60;

    /// <summary>Итог серии измерений: результаты точек + Id записи эксперимента в БД.</summary>
    public sealed record BenchmarkRunOutcome(IReadOnlyList<BenchmarkResult> Results, long ExperimentId);

    /// <summary>
    /// Выполняет серию измерений.
    /// </summary>
    /// <param name="algorithm">Исследуемый алгоритм.</param>
    /// <param name="config">Конфигурация (Nmax, шаг, запуски, seed, лимит времени).</param>
    /// <param name="costModel">Модель стоимости операций.</param>
    /// <param name="progress">Прогресс для UI.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <param name="onPointCompleted">Колбэк после завершения точки (частичные результаты).</param>
    /// <param name="database">БД (null — работа без БД и кэша).</param>
    /// <param name="useCache">Использовать кэш БД.</param>
    /// <param name="forceRecalculation">Принудительный пересчёт (удалить старые данные ключа).</param>
    /// <returns>Список результатов по точкам.</returns>
    public async Task<BenchmarkRunOutcome> RunAsync(
        AlgorithmBase algorithm,
        BenchmarkConfiguration config,
        OperationCostModel costModel,
        IProgress<BenchmarkProgress>? progress,
        CancellationToken ct,
        Action<BenchmarkResult>? onPointCompleted = null,
        BenchmarkDatabase? database = null,
        bool useCache = true,
        bool forceRecalculation = false)
    {
        ValidateConfiguration(config);
        return await Task.Run(() =>
        {
            var sizes = GenerateSizes(config);
            long totalWork = (long)sizes.Length * (config.RunsPerPoint + 1);
            long doneWork = 0;

            string configHash = string.Empty;
            long experimentId = 0;
            if (database != null)
            {
                configHash = BenchmarkDatabase.ComputeConfigHash(algorithm, config, costModel, modeTag: "TIME");
                // Принудительный пересчёт: старые данные ключа удаляются целиком,
                // чтобы старые и новые результаты не смешивались.
                if (forceRecalculation)
                {
                    database.DeleteConfigData(configHash);
                }
                experimentId = database.BeginExperiment(configHash, algorithm, config, costModel);
            }

            // --- JIT-прогрев: компиляция и первый прогон алгоритма на малом n ---
            long warmupN = Math.Min(10_000, Math.Max(1, sizes[0]));
            var warmupInput = algorithm.PrepareRunInput(algorithm.PrepareInput(warmupN));
            algorithm.Run(warmupInput);
            doneWork++;
            ReportProgress(progress, algorithm, sizes[0], 0, sizes.Length, 0, config.RunsPerPoint, doneWork, totalWork);

            var results = new List<BenchmarkResult>(sizes.Length);
            for (int point = 0; point < sizes.Length; point++)
            {
                ct.ThrowIfCancellationRequested();
                long n = sizes[point];

                IReadOnlyList<double> times;
                bool fromCache = false;

                // --- Кэш: точка уже измерена с этой конфигурацией? ---
                if (database != null && useCache && !forceRecalculation &&
                    database.TryGetCachedRuns(configHash, n, m: 1, out var cachedRuns) &&
                    cachedRuns.Count == config.RunsPerPoint)
                {
                    times = cachedRuns;
                    fromCache = true;
                    doneWork += config.RunsPerPoint + 1;
                    ReportProgress(progress, algorithm, n, point, sizes.Length, config.RunsPerPoint, config.RunsPerPoint, doneWork, totalWork);
                }
                else
                {
                    // Подготовка данных — ВНЕ измеряемого участка.
                    object sharedInput = algorithm.PrepareInput(n);
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();

                    var measured = new List<double>(config.RunsPerPoint);
                    // r = 0 — прогревочный запуск точки, не учитывается.
                    for (int run = 0; run <= config.RunsPerPoint; run++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var runInput = algorithm.PrepareRunInput(sharedInput);
                        double ns = BenchmarkTimer.MeasureNs(() => algorithm.Run(runInput));
                        doneWork++;

                        // Лимит времени точки: защита от зависания при большом Nmax.
                        if (run > 0 && config.MaxSecondsPerPoint > 0 && ns > config.MaxSecondsPerPoint * 1e9)
                        {
                            throw new PointTimeoutException(n, ns, config.MaxSecondsPerPoint);
                        }

                        if (run > 0)
                        {
                            measured.Add(ns);
                            database?.SaveMeasurement(
                                experimentId, n, run, ns,
                                stepCount: algorithm.EstimateOperationCounts(n).Total,
                                theoreticalNs: algorithm.EstimateTheoreticalTimeNs(n, costModel));
                        }
                        ReportProgress(progress, algorithm, n, point, sizes.Length, run, config.RunsPerPoint, doneWork, totalWork);
                    }
                    times = measured;
                    sharedInput = null!; // освобождаем большую ссылку до GC-паузы следующей точки
                }

                var result = StatisticsService.BuildResult(algorithm, n, times, config.RunsPerPoint, costModel, fromCache);
                results.Add(result);
                onPointCompleted?.Invoke(result);
            }

            return new BenchmarkRunOutcome(results, experimentId);
        }, ct);
    }

    /// <summary>
    /// Готовит список размеров n: StartN, StartN+StepN, …, Nmax (включён всегда).
    /// Без запуска измерений — используется кнопкой «Подготовить данные».
    /// </summary>
    public static long[] GenerateSizes(BenchmarkConfiguration config)
    {
        ValidateConfiguration(config);
        var sizes = new List<long>(MaxPoints);
        for (long n = config.StartN; n <= config.EndN && sizes.Count < MaxPoints; n += config.StepN)
        {
            sizes.Add(n);
        }
        // Nmax всегда последняя точка (если ещё не добавлена).
        if ((sizes.Count == 0 || sizes[^1] != config.EndN) && sizes.Count < MaxPoints)
        {
            sizes.Add(config.EndN);
        }
        return sizes.ToArray();
    }

    /// <summary>
    /// Публичная проверка корректности конфигурации (используется ViewModel
    /// для валидации полей интерфейса до запуска эксперимента).
    /// </summary>
    public static void ValidateConfigurationPublic(BenchmarkConfiguration config) => ValidateConfiguration(config);

    private static void ValidateConfiguration(BenchmarkConfiguration config)
    {
        if (config.StartN <= 0)
            throw new ArgumentException("Начальное значение n должно быть положительным.");
        if (config.EndN <= config.StartN)
            throw new ArgumentException("Nmax должен быть больше начального n.");
        if (config.StepN <= 0)
            throw new ArgumentException("Шаг (step) должен быть положительным.");
        if ((config.EndN - config.StartN) / config.StepN + 1 > MaxPoints)
            throw new ArgumentException($"Слишком много точек: максимум {MaxPoints}. Увеличьте шаг (step).");
        if (config.RunsPerPoint < 1 || config.RunsPerPoint > 100)
            throw new ArgumentException("Количество запусков должно быть от 1 до 100.");
        if (config.MaxSecondsPerPoint < 0)
            throw new ArgumentException("Лимит времени точки не может быть отрицательным.");
    }

    private static void ReportProgress(
        IProgress<BenchmarkProgress>? progress,
        AlgorithmBase algorithm,
        long currentN,
        int pointIndex,
        int pointCount,
        int runIndex,
        int runCount,
        long doneWork,
        long totalWork)
    {
        progress?.Report(new BenchmarkProgress(
            algorithm.Name,
            currentN,
            pointIndex,
            pointCount,
            runIndex,
            runCount,
            (int)(doneWork * 100 / Math.Max(1, totalWork))));
    }
}
