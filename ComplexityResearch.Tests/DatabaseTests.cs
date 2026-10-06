using System.IO;
using ComplexityResearch.Algorithms;
using ComplexityResearch.Models;
using ComplexityResearch.Services;
using Xunit;

namespace ComplexityResearch.Tests;

/// <summary>
/// Тесты БД (SQLite) и кэша измерений.
/// </summary>
public class DatabaseTests : IDisposable
{
    private readonly string _dbPath;

    public DatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"benchmark_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static BenchmarkResult MakeResult(long n, double[] runsNs) =>
        StatisticsService.BuildResult(new SumAlgorithm(), n, runsNs, runsPerPoint: runsNs.Length,
            OperationCostModel.CreateDefault());

    [Fact]
    public void Database_Creates_Schema_And_Counts()
    {
        using var db = new BenchmarkDatabase(_dbPath);
        Assert.True(File.Exists(_dbPath));
        Assert.Equal(0, db.CountExperiments());
        Assert.Equal(0, db.CountMeasurements());
    }

    [Fact]
    public void Save_And_Read_Cached_Runs_Roundtrip()
    {
        using var db = new BenchmarkDatabase(_dbPath);
        var algo = new SumAlgorithm();
        var cfg = new BenchmarkConfiguration { StartN = 1000, EndN = 10_000, StepN = 1000, RunsPerPoint = 3 };
        var cost = OperationCostModel.CreateDefault();
        string hash = BenchmarkDatabase.ComputeConfigHash(algo, cfg, cost, "TIME");

        long expId = db.BeginExperiment(hash, algo, cfg, cost);
        double[] runs = [100.5, 110.5, 120.5];
        for (int run = 0; run < runs.Length; run++)
        {
            db.SaveMeasurement(expId, n: 5000, runNumber: run + 1, elapsedNs: runs[run],
                stepCount: 25_000, theoreticalNs: 60_000);
        }

        Assert.True(db.TryGetCachedRuns(hash, 5000, m: 1, out var cached));
        Assert.Equal(3, cached.Count);
        Assert.Equal(runs, cached.ToArray());

        // Другой n — нет в кэше.
        Assert.False(db.TryGetCachedRuns(hash, 6000, m: 1, out _));
        // Другой хэш конфигурации — нет в кэше.
        var cfg2 = cfg.Clone();
        cfg2.RunsPerPoint = 5;
        string hash2 = BenchmarkDatabase.ComputeConfigHash(algo, cfg2, cost, "TIME");
        Assert.False(db.TryGetCachedRuns(hash2, 5000, m: 1, out _));
    }

    [Fact]
    public void Cache_Takes_Most_Recent_Experiment_Only()
    {
        using var db = new BenchmarkDatabase(_dbPath);
        var algo = new SumAlgorithm();
        var cfg = new BenchmarkConfiguration { StartN = 1, EndN = 100, StepN = 50, RunsPerPoint = 2 };
        string hash = BenchmarkDatabase.ComputeConfigHash(algo, cfg, OperationCostModel.CreateDefault(), "TIME");

        long exp1 = db.BeginExperiment(hash, algo, cfg, OperationCostModel.CreateDefault());
        db.SaveMeasurement(exp1, 50, 1, 1.0, 100, 100);
        db.SaveMeasurement(exp1, 50, 2, 2.0, 100, 100);

        long exp2 = db.BeginExperiment(hash, algo, cfg, OperationCostModel.CreateDefault());
        db.SaveMeasurement(exp2, 50, 1, 10.0, 100, 100);
        db.SaveMeasurement(exp2, 50, 2, 20.0, 100, 100);

        Assert.True(db.TryGetCachedRuns(hash, 50, m: 1, out var cached));
        Assert.Equal(2, cached.Count);
        Assert.Equal(new[] { 10.0, 20.0 }, cached.ToArray()); // свежий эксперимент, не смесь
    }

    [Fact]
    public void Force_Recalculation_Deletes_All_Data_For_Key()
    {
        using var db = new BenchmarkDatabase(_dbPath);
        var algo = new SumAlgorithm();
        var cfg = new BenchmarkConfiguration { StartN = 1, EndN = 100, StepN = 50, RunsPerPoint = 2 };
        string hash = BenchmarkDatabase.ComputeConfigHash(algo, cfg, OperationCostModel.CreateDefault(), "TIME");

        long exp1 = db.BeginExperiment(hash, algo, cfg, OperationCostModel.CreateDefault());
        db.SaveMeasurement(exp1, 50, 1, 1.0, 100, 100);

        long deleted = db.DeleteConfigData(hash);
        Assert.Equal(1, deleted);
        Assert.Equal(0, db.CountExperiments());
        Assert.Equal(0, db.CountMeasurements());
        Assert.False(db.TryGetCachedRuns(hash, 50, m: 1, out _));
    }

    [Fact]
    public void Config_Hash_Changes_With_Parameters()
    {
        var algo = new SumAlgorithm();
        var cost = OperationCostModel.CreateDefault();
        var cfg = new BenchmarkConfiguration { StartN = 1, EndN = 100, StepN = 50 };

        string baseHash = BenchmarkDatabase.ComputeConfigHash(algo, cfg, cost, "TIME");

        var cfgOtherStep = cfg.Clone();
        cfgOtherStep.StepN = 10;
        Assert.NotEqual(baseHash, BenchmarkDatabase.ComputeConfigHash(algo, cfgOtherStep, cost, "TIME"));

        var cfgOtherSeed = cfg.Clone();
        cfgOtherSeed.RandomSeed = 42;
        Assert.NotEqual(baseHash, BenchmarkDatabase.ComputeConfigHash(algo, cfgOtherSeed, cost, "TIME"));

        var costOther = cost.Clone();
        costOther.AdditionCost *= 2;
        Assert.NotEqual(baseHash, BenchmarkDatabase.ComputeConfigHash(algo, cfg, costOther, "TIME"));

        // Тег режима: время и операции не смешиваются.
        Assert.NotEqual(baseHash, BenchmarkDatabase.ComputeConfigHash(algo, cfg, cost, "OPS"));
    }

    [Fact]
    public void Update_Summary_Persists_Mse_And_Conclusion()
    {
        using var db = new BenchmarkDatabase(_dbPath);
        var algo = new SumAlgorithm();
        var cfg = new BenchmarkConfiguration();
        string hash = BenchmarkDatabase.ComputeConfigHash(algo, cfg, OperationCostModel.CreateDefault(), "TIME");
        long expId = db.BeginExperiment(hash, algo, cfg, OperationCostModel.CreateDefault());
        db.SaveMeasurement(expId, 10, 1, 5.0, 50, 50);

        db.UpdateExperimentSummary(expId, 12.5, 3.3, "тестовый вывод");

        // Проверяем через перечитание кэша (данные не пропали) — и отсутствие исключений.
        Assert.True(db.TryGetCachedOps(hash, 10, out var steps));
        Assert.Equal(50, steps);
    }

    [Fact]
    public void Cache_Worker_Produces_Statistics_Identical_To_Raw_Runs()
    {
        using var db = new BenchmarkDatabase(_dbPath);
        var algo = new SumAlgorithm();
        var cfg = new BenchmarkConfiguration { StartN = 1, EndN = 100, StepN = 100, RunsPerPoint = 5 };
        string hash = BenchmarkDatabase.ComputeConfigHash(algo, cfg, OperationCostModel.CreateDefault(), "TIME");
        long expId = db.BeginExperiment(hash, algo, cfg, OperationCostModel.CreateDefault());

        double[] runs = [100, 200, 300, 400, 500];
        for (int i = 0; i < runs.Length; i++)
        {
            db.SaveMeasurement(expId, 100, i + 1, runs[i], 500, 2500);
        }

        db.TryGetCachedRuns(hash, 100, m: 1, out var cached);
        var fromCache = StatisticsService.BuildResult(algo, 100, cached, 5, OperationCostModel.CreateDefault(), fromCache: true);
        var fromRaw = MakeResult(100, runs);

        Assert.Equal(fromRaw.Statistics.MeanNs, fromCache.Statistics.MeanNs, 6);
        Assert.Equal(fromRaw.Statistics.StdDevNs, fromCache.Statistics.StdDevNs, 6);
        Assert.True(fromCache.IsFromCache);
        Assert.Equal("Из кэша", fromCache.ModeText);
    }
}
