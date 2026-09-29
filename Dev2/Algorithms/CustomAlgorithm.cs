using ComplexityResearch.Models;

namespace ComplexityResearch.Algorithms;

/// <summary>
/// №9. Индивидуальный алгоритм: быстрое возведение в степень по модулю — O(log n).
/// </summary>
/// <remarks>
/// <para>
/// Образец для замены на собственный алгоритм студента. Требования к
/// индивидуальному алгоритму: сложность не хуже O(n) (интереснее линейной),
/// собственная теоретическая формула, поддержка общей системой измерений.
/// </para>
/// <para>
/// Алгоритм: двоичное возведение в степень (exponentiation by squaring).
/// aⁿ mod m вычисляется за O(log n) умножений: показатель разлагается
/// в двоичную запись, на каждом шаге основание возводится в квадрат,
/// а при установленном бите результат домножается.
/// </para>
/// <para>
/// Особенность измерения: один вызов для n = 10¹⁵ — это ~50 умножений (~100 нс),
/// что на пределе разрешения Stopwatch. Поэтому выполняется фиксированная серия
/// из <see cref="RepeatsPerRun"/> вычислений за один замер; время растёт
/// пропорционально log₂(n) — рост сложности виден на графике.
/// Алгоритм не требует больших массивов — всегда прямой режим.
/// </para>
/// </remarks>
public sealed class CustomAlgorithm : AlgorithmBase
{
    /// <summary>Количество вычислений aⁿ в одном замере (технический параметр для измеримого времени).</summary>
    public const int RepeatsPerRun = 200_000;

    /// <summary>Модуль арифметики (простое число, исключает переполнение: до 10¹⁵² &lt; 2⁶³).</summary>
    public const long Modulus = 1_000_000_007L;

    /// <summary>Основание степени.</summary>
    public const long Base = 2L;

    /// <summary>Вход: логический показатель степени n.</summary>
    public sealed record PowerInput(long Exponent);

    /// <inheritdoc />
    public override string Name => "№10. Индивидуальный: быстрое возведение в степень — O(log n)";

    /// <inheritdoc />
    public override string Description =>
        "Двоичное возведение в степень aⁿ mod m: показатель разлагается в двоичную запись, " +
        "каждый шаг — возведение основания в квадрат, при установленном бите — домножение результата. " +
        "Всего ≈ log₂(n) итераций. Образец для замены на собственный алгоритм студента.";

    /// <inheritdoc />
    public override string Complexity => "O(log n)";

    /// <inheritdoc />
    public override string ComplexityClass => "O(log n)";

    /// <inheritdoc />
    public override string Application =>
        "Криптография (RSA, Diffie–Hellman), модульная арифметика больших степеней, " +
        "быстрое вычисление биномиальных коэффициентов по модулю.";

    /// <inheritdoc />
    public override string TheoreticalFormula =>
        "T(n) ≈ K·log₂(n)·(3·C_умн + C_слож + C_срав + 2·C_присв), где K = 200 000 повторений " +
        "в одном замере; на итерацию: умножение, остаток (≈ умножение), квадрат основания (2 умножения), " +
        "сдвиг и сравнение";

    /// <inheritdoc />
    public override BenchmarkConfiguration DefaultConfiguration => new()
    {
        // Рост log₂(n): от 7 до 17 итераций на диапазоне — рост времени ~2.4x.
        StartN = 100L,
        EndN = 100_000L,
        StepN = 10_000L,
        RunsPerPoint = 5,
        MaxSecondsPerPoint = 15
    };

    /// <inheritdoc />
    public override MeasurementMode ModeFor(long n) => MeasurementMode.Direct;

    /// <inheritdoc />
    public override object PrepareInput(long n) => new PowerInput(n);

    /// <inheritdoc />
    protected override void ExecuteCore(object input)
    {
        long exponent = ((PowerInput)input).Exponent;
        long acc = 0;
        for (int rep = 0; rep < RepeatsPerRun; rep++)
        {
            // Классическое двоичное возведение в степень.
            long result = 1;
            long basis = Base % Modulus;
            long e = exponent;
            while (e > 0)
            {
                if ((e & 1) == 1)
                {
                    result = result * basis % Modulus;
                }
                basis = basis * basis % Modulus;
                e >>= 1;
            }
            acc ^= result; // накапливаем результат, чтобы JIT не удалил вычисления
        }
        PreventOptimization(acc);
    }

    /// <inheritdoc />
    public override OperationCounts EstimateOperationCounts(long n)
    {
        double iterations = Math.Log2(Math.Max(2, n)) * RepeatsPerRun;
        // В среднем половина битов показателя установлена → половина итераций
        // содержит дополнительное умножение с остатком.
        return new OperationCounts(
            Addition: iterations,            // сдвиг/декремент показателя
            Multiplication: 3 * iterations,  // квадрат + остаток; +умножение и остаток при установленном бите (в среднем 0.5 → ≈ 3 всего)
            Comparison: iterations,
            Assignment: 2 * iterations,
            Swap: 0,
            ArrayAccess: 0);
    }

    /// <inheritdoc />
    public override Func<double, double> ComplexityFunction => n => Math.Log2(Math.Max(2, n));
}
