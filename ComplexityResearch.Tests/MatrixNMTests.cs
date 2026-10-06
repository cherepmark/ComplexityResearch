using System.IO;
using ComplexityResearch.Algorithms;
using ComplexityResearch.Models;
using ComplexityResearch.Services;
using Xunit;

namespace ComplexityResearch.Tests;

/// <summary>
/// Тесты ДВУМЕРНОГО матричного эксперимента A(n×m)·B(m×n):
/// ядро MultiplyNM, оценка операций, сервис сетки n×m и БД/кэш.
/// </summary>
public class MatrixNMTests : IDisposable
{
    // ---------- Ядро MultiplyNM ----------

    [Fact]
    public void MultiplyNM_Known_Case()
    {
        // A(2×3)·B(3×2) = C(2×2)
        var a = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
        var b = new int[3, 2] { { 7, 8 }, { 9, 10 }, { 11, 12 } };
        var c = new int[2, 2];
        MatrixMultiplicationAlgorithm.MultiplyNM(a, b, c);
        // [1·7+2·9+3·11, 1·8+2·10+3·12] = [58, 64]
        // [4·7+5·9+6·11, 4·8+5·10+6·12] = [139, 154]
        Assert.Equal(58, c[0, 0]);
        Assert.Equal(64, c[0, 1]);
        Assert.Equal(139, c[1, 0]);
        Assert.Equal(154, c[1, 1]);
    }

    [Fact]
    public void MultiplyNM_Matches_Reference_On_Random_Sizes()
    {
        var rng = new Random(20260914);
        foreach (var (n, m) in new[] { (1, 1), (2, 5), (5, 2), (7, 7), (10, 13) })
        {
            var a = new int[n, m];
            var b = new int[m, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    a[i, j] = rng.Next(-10, 10);
                    b[j, i] = rng.Next(-10, 10);
                }
            }

            var expected = ReferenceMultiplyNM(a, b);
            var actual = new int[n, n];
            MatrixMultiplicationAlgorithm.MultiplyNM(a, b, actual);

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    Assert.Equal(expected[i, j], actual[i, j]);
                }
            }
        }
    }

    [Fact]
    public void MultiplyNM_Estimate_Scales_As_N2M()
    {
        // f = n²·m: удвоение m → ×2; удвоение n → ×4.
        double f1 = MatrixMultiplicationAlgorithm.EstimateOperationCountsNM(100, 200).Total;
        double f2 = MatrixMultiplicationAlgorithm.EstimateOperationCountsNM(100, 400).Total;
        double f3 = MatrixMultiplicationAlgorithm.EstimateOperationCountsNM(200, 200).Total;
        Assert.InRange(f2 / f1, 1.9, 2.1);
        Assert.InRange(f3 / f1, 3.8, 4.2);
    }

    private static int[,] ReferenceMultiplyNM(int[,] a, int[,] b)
    {
        int n = a.GetLength(0), m = a.GetLength(1), p = b.GetLength(1);
        var c = new int[n, p];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < p; j++)
            {
                int sum = 0;
                for (int k = 0; k < m; k++)
                {
                    sum += a[i, k] * b[k, j];
                }
                c[i, j] = sum;
            }
        }
        return c;
    }

    // ---------- Сервис сетки n×m + БД ----------

    private readonly string _dbPath;

    public MatrixNMTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"matrix_nm_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static BenchmarkConfiguration SmallRange() => new()
    {
        StartN = 20,
        EndN = 60,
        StepN = 20,
        RunsPerPoint = 2,
        MaxSecondsPerPoint = 10
    };

    [Fact]
    public async Task Service_Produces_Grid_Of_Triples_And_Persists_With_M()
    {
        using var db = new BenchmarkDatabase(_dbPath);
        var service = new MatrixNMBenchmarkService();
        var nRange = SmallRange();
        var mRange = SmallRange();
        var cost = OperationCostModel.CreateDefault();

        var outcome = await service.RunAsync(nRange, mRange, cost, progress: null, CancellationToken.None, database: db);

        // 3 значения n × 3 значения m = 9 троек (n, m, время).
        Assert.Equal(9, outcome.Results.Count);
        Assert.All(outcome.Results, r =>
        {
            Assert.True(r.Statistics.MeanNs > 0);
            Assert.Equal(nRange.RunsPerPoint, r.RunTimesNs.Count);
            Assert.False(r.IsFromCache);
        });
        Assert.Contains(outcome.Results, r => r.N == 20 && r.M == 60);
        Assert.Contains(outcome.Results, r => r.N == 60 && r.M == 20);

        // В БД: 9 ячеек × 2 запуска = 18 строк, у всех M = m ячейки.
        Assert.Equal(18, db.CountMeasurements());
        Assert.Equal(1, db.CountExperiments());

        // Кэш по (n, m): повторный запуск берётся из БД.
        var outcome2 = await service.RunAsync(nRange, mRange, cost, progress: null, CancellationToken.None, database: db, useCache: true);
        Assert.All(outcome2.Results, r => Assert.True(r.IsFromCache));
        Assert.Equal(18, db.CountMeasurements()); // новых записей нет

        // Force recalculation: старые данные ключа удалены целиком.
        var outcome3 = await service.RunAsync(nRange, mRange, cost, progress: null, CancellationToken.None, database: db, useCache: true, forceRecalculation: true);
        Assert.All(outcome3.Results, r => Assert.False(r.IsFromCache));
        Assert.Equal(1, db.CountExperiments());
        Assert.Equal(18, db.CountMeasurements());
    }

    [Fact]
    public async Task MatrixNM_Hash_Differs_From_2D_And_From_Other_MRanges()
    {
        var cost = OperationCostModel.CreateDefault();
        var nRange = SmallRange();
        var mRange = SmallRange();
        string hashNM = BenchmarkDatabase.ComputeMatrixNMHash(nRange, mRange, cost);

        // Другой диапазон m → другой ключ.
        var mRange2 = SmallRange();
        mRange2.EndN = 80;
        Assert.NotEqual(hashNM, BenchmarkDatabase.ComputeMatrixNMHash(nRange, mRange2, cost));

        // 2D-замер матриц (TIME) и NM (MATNM) — разные виды экспериментов.
        var algo = new MatrixMultiplicationAlgorithm();
        Assert.NotEqual(hashNM, BenchmarkDatabase.ComputeConfigHash(algo, nRange, cost, "TIME"));
    }

    [Fact]
    public void FitPowerNM_Recovers_Coefficient_On_Synthetic_Data()
    {
        double c = 2.5e-6;
        var results = new List<MatrixNMResult>();
        foreach (long n in new[] { 10, 20, 40 })
        {
            foreach (long m in new[] { 10, 20, 40 })
            {
                double t = c * n * n * m;
                results.Add(new MatrixNMResult
                {
                    N = n,
                    M = m,
                    Statistics = new StatisticsResult { MeanNs = t }
                });
            }
        }
        var fit = MatrixNMBenchmarkService.FitPowerNM(results);
        Assert.Equal(c, fit.C, 12);
        Assert.Equal(0, fit.MSE, 8);
        Assert.Contains("n²·m", fit.Formula);
    }

    [Fact]
    public void Validate_Rejects_Too_Large_Grids()
    {
        var ok = new BenchmarkConfiguration { StartN = 10, EndN = 100, StepN = 10 };
        var tooManyM = new BenchmarkConfiguration { StartN = 10, EndN = 1000, StepN = 5 };
        Assert.Throws<ArgumentException>(() => MatrixNMBenchmarkService.Validate(ok, tooManyM));
    }
}
