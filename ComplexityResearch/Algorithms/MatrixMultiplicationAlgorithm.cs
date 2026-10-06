using ComplexityResearch.Models;
using ComplexityResearch.Services;

namespace ComplexityResearch.Algorithms;

/// <summary>
/// Входные данные умножения матриц.
/// </summary>
/// <param name="A">Матрица A (n×n).</param>
/// <param name="B">Матрица B (n×n).</param>
/// <param name="Result">Матрица результата C = A·B (n×n), выделена ВНЕ таймера.</param>
public sealed record MatrixInput(int[,] A, int[,] B, int[,] Result);

/// <summary>
/// №10. Классическое умножение матриц — O(n³).
/// </summary>
/// <remarks>
/// <para>
/// C[i,j] = Σ A[i,k]·B[k,j] по всем k. Тройной вложенный цикл даёт ровно n³
/// умножений и n³ сложений — эталон кубической сложности.
/// </para>
/// <para>
/// Порядок циклов i-k-j (а не «лобовой» i-j-k): при фиксированном i обращение
/// к B идёт по строке k (последовательно по памяти), что в разы быстрее на
/// больших матрицах за счёт кэша. Сложность при этом не меняется — O(n³);
/// порядок циклов упоминается в Documentation/BenchmarkMethodology.md как
/// пример влияния кэша на константу C.
/// </para>
/// <para>
/// Матрицы A, B генерируются и клонируются ВНЕ таймера; матрица результата
/// выделяется вне таймера — в измерение попадает только само умножение.
/// Nmax = 600 подобран эмпирически: 2·600³ ≈ 4.3·10⁸ операций ≈ 2–5 с.
/// </para>
/// </remarks>
public sealed class MatrixMultiplicationAlgorithm : AlgorithmBase
{
    /// <inheritdoc />
    public override string Name => "№9. Умножение матриц (классическое) — O(n³)";

    /// <inheritdoc />
    public override string Description =>
        "C = A·B тройным вложенным циклом: n³ умножений и n³ сложений. " +
        "Порядок циклов i-k-j для последовательного доступа к памяти (влияние кэша на константу). " +
        "Матрицы генерируются вне таймера; измеряется только умножение.";

    /// <inheritdoc />
    public override string Complexity => "O(n³)";

    /// <inheritdoc />
    public override string ComplexityClass => "O(n^3)";

    /// <inheritdoc />
    public override string Application =>
        "Графика (трансформации), решение СЛАУ, графы (транзитивное замыкание), " +
        "нейросети (до появления BLAS-оптимизаций), учебные задачи.";

    /// <inheritdoc />
    public override string TheoreticalFormula =>
        "T(n) = n³·(C_умн + C_слож) + 3n³·(C_срав + C_присв) + n²·C_дост — " +
        "n³ умножений и сложений во внутреннем цикле плюс накладные расходы двух внешних циклов";

    /// <inheritdoc />
    public override BenchmarkConfiguration DefaultConfiguration => new()
    {
        StartN = 100,
        EndN = 600,   // Nmax: 2·600³ ≈ 4.3·10⁸ операций ≈ 2–5 с (подобрано эмпирически)
        StepN = 100,
        RunsPerPoint = 5,
        MaxSecondsPerPoint = 15
    };

    /// <inheritdoc />
    public override MeasurementMode ModeFor(long n) => MeasurementMode.Direct;

    /// <inheritdoc />
    public override object PrepareInput(long n)
    {
        int size = (int)n;
        var rng = DataPreparationService.CreateRandom(20260914 + 10);
        var a = new int[size, size];
        var b = new int[size, size];
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                a[i, j] = rng.Next(-100, 101);
                b[i, j] = rng.Next(-100, 101);
            }
        }
        return new MatrixInput(a, b, new int[size, size]);
    }

    /// <summary>
    /// Свежие копии матриц и результат — вне таймера: алгоритм каждый раз
    /// работает с одинаковыми данными, аллокации в измерение не попадают.
    /// </summary>
    public override object PrepareRunInput(object sharedInput)
    {
        var m = (MatrixInput)sharedInput;
        return new MatrixInput((int[,])m.A.Clone(), (int[,])m.B.Clone(), new int[m.A.GetLength(0), m.A.GetLength(1)]);
    }

    /// <inheritdoc />
    protected override void ExecuteCore(object input)
    {
        var m = (MatrixInput)input;
        Multiply(m.A, m.B, m.Result);
        PreventOptimization(m.Result[0, 0]);
    }

    /// <summary>
    /// Ядро алгоритма: классическое умножение матриц (публично — для unit-тестов),
    /// порядок циклов i-k-j (дружелюбен к кэшу).
    /// </summary>
    public static void Multiply(int[,] a, int[,] b, int[,] c)
    {
        int n = a.GetLength(0);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                c[i, j] = 0;
            }
        }
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < n; k++)
            {
                int aik = a[i, k];
                if (aik == 0) continue; // разреженные нули пропускаем (редко на случайных данных)
                for (int j = 0; j < n; j++)
                {
                    c[i, j] += aik * b[k, j];
                }
            }
        }
    }

    /// <inheritdoc />
    public override OperationCounts EstimateOperationCounts(long n)
    {
        double n3 = n * (double)n * n;
        return new OperationCounts(
            Addition: 2 * n3,          // c[i,j] += ... и счётчик j
            Multiplication: n3,        // aik * b[k,j]
            Comparison: 3 * n3,
            Assignment: 2 * n3,
            Swap: 0,
            ArrayAccess: 4 * n3);      // чтения a, b, запись c, чтение c
    }

    /// <inheritdoc />
    public override Func<double, double> ComplexityFunction => n => n * n * n;

    /// <summary>
    /// Ядро эксперимента «A(n×m)·B(m×n)»: умножение ПРЯМОУГОЛЬНЫХ матриц
    /// (публично — для unit-тестов). A имеет размер n×m, B — m×n,
    /// результат C — n×n; внутренних операций n²·m.
    /// </summary>
    public static void MultiplyNM(int[,] a, int[,] b, int[,] c)
    {
        int n = a.GetLength(0); // строки A
        int m = a.GetLength(1); // столбцы A = строки B
        int p = b.GetLength(1); // столбцы B (в эксперименте p = n)

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < p; j++)
            {
                c[i, j] = 0;
            }
        }
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < m; k++)
            {
                int aik = a[i, k];
                if (aik == 0) continue;
                for (int j = 0; j < p; j++)
                {
                    c[i, j] += aik * b[k, j];
                }
            }
        }
    }

    /// <summary>
    /// Оценка количества элементарных операций для A(n×m)·B(m×n):
    /// n²·m умножений и n²·m сложений во внутреннем цикле плюс накладные
    /// расходы циклов (приближённо).
    /// </summary>
    public static OperationCounts EstimateOperationCountsNM(long n, long m)
    {
        double inner = n * (double)n * m; // n²·m
        return new OperationCounts(
            Addition: 2 * inner,
            Multiplication: inner,
            Comparison: 2 * inner + n * (double)n,
            Assignment: 2 * inner + n * (double)n,
            Swap: 0,
            ArrayAccess: 4 * inner);
    }
}
