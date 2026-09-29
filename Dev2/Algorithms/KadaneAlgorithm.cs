using ComplexityResearch.Models;
using ComplexityResearch.Services;

namespace ComplexityResearch.Algorithms;

/// <summary>
/// №11. Индивидуальный алгоритм: алгоритм Кадане — O(n).
/// </summary>
/// <remarks>
/// <para>
/// Поиск максимальной суммы непрерывного подмассива за один проход:
/// maxEnding — максимальная сумма подмассива, заканчивающегося в текущей
/// позиции (либо текущий элемент, либо продолжение предыдущего), maxSoFar —
/// глобальный максимум. Один проход, константная память.
/// </para>
/// <para>
/// Для n &gt; 50 млн используется масштабированный режим (как у суммы):
/// блок 50 млн элементов обрабатывается многократно, число проходов
/// пропорционально логическому n. Значения массива — с отрицательными
/// числами, чтобы алгоритм решал нетривиальную задачу.
/// </para>
/// </remarks>
public sealed class KadaneAlgorithm : AlgorithmBase
{
    /// <summary>Максимальный физический размер массива, элементы.</summary>
    public const int BlockLimit = 50_000_000;

    /// <inheritdoc />
    public override string Name => "№11. Индивидуальный: алгоритм Кадане — O(n)";

    /// <inheritdoc />
    public override string Description =>
        "Максимальная сумма непрерывного подмассива за один проход: maxEnding = max(a[i], maxEnding + a[i]), " +
        "maxSoFar = max(maxSoFar, maxEnding). Один проход, константная память. " +
        "Для больших n — масштабированный режим (проходы ∝ n).";

    /// <inheritdoc />
    public override string Complexity => "O(n)";

    /// <inheritdoc />
    public override string ComplexityClass => "O(n)";

    /// <inheritdoc />
    public override string Application =>
        "Максимальная прибыль по дням, анализ сигналов и временных рядов, " +
        "задачи о наибольшем сегменте в биоинформатике.";

    /// <inheritdoc />
    public override string TheoreticalFormula =>
        "T(n) = n·(3·C_срав + 3·C_слож + 2·C_присв + C_дост) — на элемент: сложение, " +
        "два сравнения (max), присваивания и доступ к массиву плюс накладные расходы цикла";

    /// <inheritdoc />
    public override BenchmarkConfiguration DefaultConfiguration => new()
    {
        // Nmax = 2·10⁹ ≈ 4–6 с на максимальной точке (алгоритм ветвящийся,
        // на элемент дороже суммы; подобрано эмпирически).
        StartN = 100_000_000L,
        EndN = 2_000_000_000L,
        StepN = 200_000_000L,
        RunsPerPoint = 5,
        MaxSecondsPerPoint = 15
    };

    /// <inheritdoc />
    public override MeasurementMode ModeFor(long n) => n <= BlockLimit ? MeasurementMode.Direct : MeasurementMode.Scaled;

    /// <inheritdoc />
    public override object PrepareInput(long n)
    {
        long physical = Math.Min(n, BlockLimit);
        long passes = Math.Max(1, (n + physical - 1) / physical);
        // Значения от −100 до 100: без отрицательных чисел Кадане вырождается
        // (максимальный подмассив — весь массив), ветвления теряют смысл.
        int[] data = DataPreparationService.RandomInt32Array((int)physical, seedOffset: 11, minValue: -100, maxValue: 100);
        return new StreamInput(data, passes);
    }

    /// <inheritdoc />
    protected override void ExecuteCore(object input)
    {
        var s = (StreamInput)input;
        long acc = 0;
        for (long p = 0; p < s.Passes; p++)
        {
            acc ^= MaxSubarraySum(s.Data);
        }
        PreventOptimization(acc);
    }

    /// <summary>
    /// Ядро алгоритма: максимальная сумма подмассива
    /// (публично — для unit-тестов). Работает и для всех отрицательных массивов.
    /// </summary>
    public static long MaxSubarraySum(int[] a)
    {
        long maxEnding = 0;
        long maxSoFar = long.MinValue;
        for (int i = 0; i < a.Length; i++)
        {
            long cand = maxEnding + a[i];
            maxEnding = cand > a[i] ? cand : a[i];
            if (maxEnding > maxSoFar)
            {
                maxSoFar = maxEnding;
            }
        }
        return maxSoFar;
    }

    /// <inheritdoc />
    public override OperationCounts EstimateOperationCounts(long n)
    {
        long physical = Math.Min(n, BlockLimit);
        long passes = Math.Max(1, (n + physical - 1) / physical);
        double elements = physical * (double)passes;
        return new OperationCounts(
            Addition: 3 * elements,      // cand, счётчик i, счётчик проходов
            Multiplication: 0,
            Comparison: 3 * elements,    // cand > a[i], maxEnding > maxSoFar, i < len
            Assignment: 2 * elements,
            Swap: 0,
            ArrayAccess: elements);
    }

    /// <inheritdoc />
    public override Func<double, double> ComplexityFunction => n => n;
}
