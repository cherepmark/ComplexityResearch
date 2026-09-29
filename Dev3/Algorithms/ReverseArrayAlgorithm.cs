using ComplexityResearch.Models;
using ComplexityResearch.Services;

namespace ComplexityResearch.Algorithms;

/// <summary>
/// №12. Индивидуальный алгоритм: реверс массива — O(n).
/// </summary>
/// <remarks>
/// <para>
/// Оборот массива на месте двумя указателями: обмен a[i] и a[len−1−i],
/// указатели движутся к центру. Всего n/2 обменов — линейное время,
/// константная дополнительная память.
/// </para>
/// <para>
/// Для n &gt; 50 млн — масштабированный режим: блок 50 млн элементов
/// реверсируется многократно. После первого прохода массив обращён,
/// второй проход возвращает исходный порядок — объём работы на каждый
/// проход одинаков (n/2 обменов), поэтому суммарная работа пропорциональна
/// логическому n; это честно отражено в комментарии и режиме точки.
/// </para>
/// </remarks>
public sealed class ReverseArrayAlgorithm : AlgorithmBase
{
    /// <summary>Максимальный физический размер массива, элементы.</summary>
    public const int BlockLimit = 50_000_000;

    /// <inheritdoc />
    public override string Name => "№12. Индивидуальный: реверс массива — O(n)";

    /// <inheritdoc />
    public override string Description =>
        "Оборот массива на месте двумя указателями: n/2 обменов, указатели идут от краёв к центру. " +
        "Для больших n — масштабированный режим (проходы ∝ n; повторные реверсы чередуют порядок, " +
        "объём работы на проход одинаков).";

    /// <inheritdoc />
    public override string Complexity => "O(n)";

    /// <inheritdoc />
    public override string ComplexityClass => "O(n)";

    /// <inheritdoc />
    public override string Application =>
        "Обработка строк и буферов, разворот истории операций, подготовка данных для стека, " +
        "текстовые редакторы.";

    /// <inheritdoc />
    public override string TheoreticalFormula =>
        "T(n) = (n/2)·C_обмен + n·C_слож + (n/2)·C_срав + 2n·C_дост — n/2 обменов пар и движение двух указателей";

    /// <inheritdoc />
    public override BenchmarkConfiguration DefaultConfiguration => new()
    {
        // Nmax = 2·10⁹ ≈ 3–5 с на максимальной точке (16 ГБ трафика память↔кэш).
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
        int[] data = DataPreparationService.RandomInt32Array((int)physical, seedOffset: 12);
        return new StreamInput(data, passes);
    }

    /// <inheritdoc />
    protected override void ExecuteCore(object input)
    {
        var s = (StreamInput)input;
        int[] data = s.Data;
        long acc = 0;
        for (long p = 0; p < s.Passes; p++)
        {
            ReverseInPlace(data);
            acc ^= data[0] + data[data.Length - 1];
        }
        PreventOptimization(acc);
    }

    /// <summary>
    /// Ядро алгоритма: оборот массива на месте двумя указателями
    /// (публично — для unit-тестов).
    /// </summary>
    public static void ReverseInPlace(int[] a)
    {
        for (int i = 0, j = a.Length - 1; i < j; i++, j--)
        {
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    /// <inheritdoc />
    public override OperationCounts EstimateOperationCounts(long n)
    {
        long physical = Math.Min(n, BlockLimit);
        long passes = Math.Max(1, (n + physical - 1) / physical);
        double elements = physical * (double)passes;
        return new OperationCounts(
            Addition: elements,        // движение двух указателей (i++, j--)
            Multiplication: 0,
            Comparison: elements / 2,  // i < j
            Assignment: elements / 2,  // служебные присваивания указателей
            Swap: elements / 2,        // обмен пары = 1 полный обмен
            ArrayAccess: 2 * elements); // чтение и запись обоих элементов пары
    }

    /// <inheritdoc />
    public override Func<double, double> ComplexityFunction => n => n;
}
