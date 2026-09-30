namespace ComplexityResearch.Services;

/// <summary>
/// Результат аппроксимации экспериментальных точек кривой Tapprox(n) = C·f(n).
/// </summary>
public sealed class ApproximationResult
{
    /// <summary>Коэффициент C, подобранный методом наименьших квадратов (кривая через начало координат).</summary>
    public double C { get; init; }

    /// <summary>Коэффициент детерминации R² (1 — идеальное совпадение).</summary>
    public double R2 { get; init; }

    /// <summary>
    /// Среднеквадратичная ошибка:
    /// MSE = (1/k) · Σ (Tempirical(nᵢ) − Tapprox(nᵢ))²,
    /// в единицах² точек (для времени — нс²).
    /// </summary>
    public double MSE { get; init; }

    /// <summary>Текст формулы, например «Tapprox(n) = 3.12E-09·n²».</summary>
    public string Formula { get; init; } = string.Empty;

    /// <summary>Текст с MSE в читаемом виде.</summary>
    public string MseText { get; init; } = string.Empty;

    public string ComplexityClass { get; init; } = "O(n)";

    /// <summary>Вычисляет аппроксимированное время для размера n.</summary>
    public double Evaluate(double n) =>
     C * ApproximationService.GetComplexityFunction(ComplexityClass)(n);
}

/// <summary>
/// Аппроксимация экспериментальных данных теоретической функцией сложности.
/// </summary>
/// <remarks>
/// <para>
/// Для серии точек подбирается кривая Tapprox(n) = C·f(n), где f(n) —
/// функция сложности алгоритма (1, log₂n, n, n·log₂n, n², n³).
/// Коэффициент C подбирается методом наименьших квадратов для кривой,
/// проходящей через начало координат (свободный член не используется,
/// так как при n → 0 время → 0):
/// C = Σ f(nᵢ)·tᵢ / Σ f(nᵢ)².
/// </para>
/// <para>
/// MSE = (1/k) · Σ (Tempirical(nᵢ) − Tapprox(nᵢ))² — качество аппроксимации.
/// Низкий MSE подтверждает лишь соответствие данных выбранной ФОРМЕ f(n),
/// но не является абсолютным доказательством правильности теоретической модели
/// (например, f(n) = n² хорошо ложится и на n²·1.01, и на близкие кривые).
/// </para>
/// </remarks>
public static class ApproximationService
{
    /// <summary>
    /// Строит аппроксимацию Tapprox(n) = C·f(n) и считает MSE по точкам (n, t).
    /// </summary>
    /// <param name="points">Экспериментальные точки: (n, время/операции).</param>
    /// <param name="complexityClass">Класс сложности ("O(1)", "O(log n)", "O(n)", "O(n log n)", "O(n^2)", "O(n^3)").</param>
    public static ApproximationResult Fit(IReadOnlyList<(double N, double T)> points, string complexityClass)
    {
        var f = GetComplexityFunction(complexityClass);
        int count = points.Count;
        if (count < 2)
        {
            return new ApproximationResult
            {
                ComplexityClass = complexityClass,
                Formula = "недостаточно точек",
                MseText = "MSE: недостаточно точек"
            };
        }

        // МНК для кривой через начало координат: C = Σ f·t / Σ f².
        double sumFT = 0, sumFF = 0;
        for (int i = 0; i < count; i++)
        {
            double fi = f(points[i].N);
            sumFT += fi * points[i].T;
            sumFF += fi * fi;
        }
        double c = sumFF > 0 ? sumFT / sumFF : 0;

        // MSE и R².
        double mse = 0, ssRes = 0, ssTot = 0;
        double meanT = 0;
        for (int i = 0; i < count; i++)
        {
            meanT += points[i].T;
        }
        meanT /= count;
        for (int i = 0; i < count; i++)
        {
            double predicted = c * f(points[i].N);
            double residual = points[i].T - predicted;
            mse += residual * residual;
            ssRes += residual * residual;
            double dt = points[i].T - meanT;
            ssTot += dt * dt;
        }
        mse /= count;
        double r2 = ssTot > 0 ? Math.Max(0, 1 - ssRes / ssTot) : 0;

        return new ApproximationResult
        {
            C = c,
            R2 = r2,
            MSE = mse,
            ComplexityClass = complexityClass,
            Formula = $"Tapprox(n) = {Format(c)}·{GetComplexitySymbol(complexityClass)}  (R² = {r2:F4})",
            MseText = $"MSE = {Format(mse)} (единицы² измерения)"
        };
    }

    /// <summary>Функция сложности f(n) по классу сложности.</summary>
    public static Func<double, double> GetComplexityFunction(string complexityClass) => complexityClass switch
    {
        "O(1)" => _ => 1.0,
        "O(log n)" => n => Math.Log2(Math.Max(2, n)),
        "O(n)" => n => n,
        "O(n log n)" => n => n * Math.Log2(Math.Max(2, n)),
        "O(n^2)" => n => n * n,
        "O(n^3)" => n => n * n * n,
        _ => n => n
    };

    /// <summary>Символ функции сложности для формулы.</summary>
    public static string GetComplexitySymbol(string complexityClass) => complexityClass switch
    {
        "O(1)" => "1",
        "O(log n)" => "log₂(n)",
        "O(n)" => "n",
        "O(n log n)" => "n·log₂(n)",
        "O(n^2)" => "n²",
        "O(n^3)" => "n³",
        _ => "n"
    };

    /// <summary>Теоретический показатель степени роста (наклон в log-log координатах).</summary>
    public static double GetTheoreticalSlope(string complexityClass) => complexityClass switch
    {
        "O(1)" => 0.0,
        "O(log n)" => 0.15,
        "O(n)" => 1.0,
        "O(n log n)" => 1.5,
        "O(n^2)" => 2.0,
        "O(n^3)" => 3.0,
        _ => 1.0
    };

    private static string Format(double value) =>
        value >= 0.001 && value < 10000
            ? value.ToString("0.000")
            : value.ToString("E3");
}
