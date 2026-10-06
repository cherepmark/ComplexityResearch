using System.IO;
using System.Security.Cryptography;
using System.Text;
using ComplexityResearch.Algorithms;
using ComplexityResearch.Models;
using Microsoft.Data.Sqlite;

namespace ComplexityResearch.Services;

/// <summary>
/// Хранилище результатов экспериментов (SQLite) и кэш измерений.
/// </summary>
/// <remarks>
/// <para>
/// Схема:
/// <code>
/// Experiments(Id, ConfigHash, AlgorithmName, Complexity, StartN, NMax, StepN,
///             RunsPerPoint, Seed, Cost*константы, MSE, ApproximationC, Conclusion, CreatedUtc)
/// Measurements(Id, ExperimentId, N, RunNumber, ElapsedTimeNs, StepCount,
///              TheoreticalNs, ExperimentDate)
/// </code>
/// Минимально требуемые поля: Algorithm, n, RunNumber, ElapsedTime, StepCount,
/// ExperimentDate — присутствуют; дополнительно хранятся параметры эксперимента
/// и теоретическое значение.
/// </para>
/// <para>
/// КЭШ. Ключ кэша — SHA-256 хэш от: алгоритм + n + параметры эксперимента
/// (StartN, Nmax, step, запусков, seed) + константы стоимости операций.
/// Если для ключа уже есть измерения — они берутся из БД вместо повторного
/// запуска (режим «Use cache»). «Принудительный пересчёт» удаляет ВСЕ данные
/// с данным ключом перед запуском, поэтому старые и новые результаты
/// не смешиваются.
/// </para>
/// </remarks>
public sealed class BenchmarkDatabase : IDisposable
{
    /// <summary>Освобождает ресурсы (соединения открываются на каждую операцию и закрываются сразу).</summary>
    public void Dispose()
    {
        // Соединения SQLite создаются и закрываются на каждую операцию,
        // поэтому освобождать нечего; интерфейс реализован для использования в using.
    }

    private const int SchemaVersion = 1;
    private readonly string _connectionString;

    /// <summary>Путь к БД по умолчанию: %LOCALAPPDATA%\ComplexityResearch\benchmark.db.</summary>
    public static string DefaultDbPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ComplexityResearch", "benchmark.db");

    /// <summary>Путь к файлу БД (для отображения в интерфейсе).</summary>
    public string DbPath { get; }

    /// <summary>Создаёт (при необходимости) БД и открывает соединения по требованию.</summary>
    public BenchmarkDatabase(string dbPath)
    {
        DbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Пул отключён: соединения короткоживущие (одна операция — одно соединение),
            // а без пула файл БД можно удалять/копировать сразу после работы.
            Pooling = false
        }.ToString();
        EnsureCreated();
    }

    /// <summary>Создаёт таблицы, если их ещё нет.</summary>
    public void EnsureCreated()
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS Experiments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConfigHash TEXT NOT NULL,
                SchemaVersion INTEGER NOT NULL DEFAULT {SchemaVersion},
                AlgorithmName TEXT NOT NULL,
                Complexity TEXT NOT NULL,
                StartN INTEGER NOT NULL,
                NMax INTEGER NOT NULL,
                StepN INTEGER NOT NULL,
                RunsPerPoint INTEGER NOT NULL,
                Seed INTEGER NOT NULL,
                CostAddition REAL NOT NULL,
                CostMultiplication REAL NOT NULL,
                CostComparison REAL NOT NULL,
                CostAssignment REAL NOT NULL,
                CostSwap REAL NOT NULL,
                CostArrayAccess REAL NOT NULL,
                MSE REAL,
                ApproximationC REAL,
                Conclusion TEXT,
                CreatedUtc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_Experiments_Hash ON Experiments(ConfigHash);
            CREATE TABLE IF NOT EXISTS Measurements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ExperimentId INTEGER NOT NULL,
                N INTEGER NOT NULL,
                M INTEGER NOT NULL DEFAULT 1,
                RunNumber INTEGER NOT NULL,
                ElapsedTimeNs REAL NOT NULL,
                StepCount REAL NOT NULL,
                TheoreticalNs REAL NOT NULL,
                ExperimentDate TEXT NOT NULL,
                FOREIGN KEY(ExperimentId) REFERENCES Experiments(Id) ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS IX_Measurements_HashN ON Measurements(ExperimentId, N, M, RunNumber);
            """;
        cmd.ExecuteNonQuery();
        Migrate();
    }

    /// <summary>
    /// Миграция старых БД: добавляет колонку M (внутреннее измерение
    /// матричного эксперимента A(n×m)·B(m×n)); для обычных одномерных
    /// замеров M = 1. Старые данные не теряются.
    /// </summary>
    private void Migrate()
    {
        using var connection = OpenConnection();
        bool hasM = false;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Measurements);";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1) == "M")
                {
                    hasM = true;
                }
            }
        }
        if (!hasM)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE Measurements ADD COLUMN M INTEGER NOT NULL DEFAULT 1;";
            cmd.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        // Каскадное удаление для FK.
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
        return connection;
    }

    /// <summary>
    /// Вычисляет ключ кэша: SHA-256 от алгоритма + параметров эксперимента +
    /// констант стоимости (+ тег режима, чтобы не смешивать время и операции).
    /// </summary>
    public static string ComputeConfigHash(
        AlgorithmBase algorithm, BenchmarkConfiguration cfg, OperationCostModel cost, string modeTag)
    {
        string payload = string.Join('|',
            modeTag,
            algorithm.Name,
            algorithm.ComplexityClass,
            cfg.StartN, cfg.EndN, cfg.StepN, cfg.RunsPerPoint, cfg.RandomSeed,
            cfg.PhysicalBlockLimit,
            cost.AdditionCost.ToString("R"),
            cost.MultiplicationCost.ToString("R"),
            cost.ComparisonCost.ToString("R"),
            cost.AssignmentCost.ToString("R"),
            cost.SwapCost.ToString("R"),
            cost.ArrayAccessCost.ToString("R"));
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Ключ кэша ДВУМЕРНОГО матричного эксперимента A(n×m)·B(m×n):
    /// отдельный тег режима «MATNM» (не смешивается с обычным 2D-замером
    /// матриц «TIME» и операционным «OPS») + ОБА диапазона (n и m) +
    /// параметры запусков + константы стоимости.
    /// </summary>
    public static string ComputeMatrixNMHash(
        BenchmarkConfiguration nRange, BenchmarkConfiguration mRange, OperationCostModel cost)
    {
        string payload = string.Join('|',
            "MATNM",
            "MatrixNM",
            nRange.StartN, nRange.EndN, nRange.StepN,
            mRange.StartN, mRange.EndN, mRange.StepN,
            nRange.RunsPerPoint, nRange.RandomSeed,
            nRange.PhysicalBlockLimit,
            cost.AdditionCost.ToString("R"),
            cost.MultiplicationCost.ToString("R"),
            cost.ComparisonCost.ToString("R"),
            cost.AssignmentCost.ToString("R"),
            cost.SwapCost.ToString("R"),
            cost.ArrayAccessCost.ToString("R"));
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Создаёт запись эксперимента и возвращает её Id. Измерения добавляются
    /// через <see cref="SaveMeasurement"/> по мере выполнения точек (частичные
    /// результаты тоже сохраняются, например при отмене или таймауте).
    /// </summary>
    public long BeginExperiment(string configHash, AlgorithmBase algorithm, BenchmarkConfiguration cfg, OperationCostModel cost) =>
        BeginExperiment(configHash, algorithm.Name, algorithm.Complexity, cfg, cost);

    /// <summary>
    /// Перегрузка для экспериментов без объекта алгоритма
    /// (например, двумерный матричный A(n×m)·B(m×n)).
    /// </summary>
    public long BeginExperiment(string configHash, string algorithmName, string complexity, BenchmarkConfiguration cfg, OperationCostModel cost)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO Experiments (ConfigHash, AlgorithmName, Complexity, StartN, NMax, StepN,
                                     RunsPerPoint, Seed, CostAddition, CostMultiplication, CostComparison,
                                     CostAssignment, CostSwap, CostArrayAccess, CreatedUtc)
            VALUES (@hash, @name, @complexity, @startN, @nMax, @stepN, @runs, @seed,
                    @cAdd, @cMul, @cCmp, @cAssign, @cSwap, @cArr, @created);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@hash", configHash);
        cmd.Parameters.AddWithValue("@name", algorithmName);
        cmd.Parameters.AddWithValue("@complexity", complexity);
        cmd.Parameters.AddWithValue("@startN", cfg.StartN);
        cmd.Parameters.AddWithValue("@nMax", cfg.EndN);
        cmd.Parameters.AddWithValue("@stepN", cfg.StepN);
        cmd.Parameters.AddWithValue("@runs", cfg.RunsPerPoint);
        cmd.Parameters.AddWithValue("@seed", cfg.RandomSeed);
        cmd.Parameters.AddWithValue("@cAdd", cost.AdditionCost);
        cmd.Parameters.AddWithValue("@cMul", cost.MultiplicationCost);
        cmd.Parameters.AddWithValue("@cCmp", cost.ComparisonCost);
        cmd.Parameters.AddWithValue("@cAssign", cost.AssignmentCost);
        cmd.Parameters.AddWithValue("@cSwap", cost.SwapCost);
        cmd.Parameters.AddWithValue("@cArr", cost.ArrayAccessCost);
        cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("O"));
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Сохраняет один запуск точки: n, номер запуска, время, число операций, теория.</summary>
    public void SaveMeasurement(long experimentId, long n, int runNumber, double elapsedNs, double stepCount, double theoreticalNs, long m = 1)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO Measurements (ExperimentId, N, M, RunNumber, ElapsedTimeNs, StepCount, TheoreticalNs, ExperimentDate)
            VALUES (@expId, @n, @m, @run, @elapsed, @steps, @theoretical, @date);
            """;
        cmd.Parameters.AddWithValue("@expId", experimentId);
        cmd.Parameters.AddWithValue("@n", n);
        cmd.Parameters.AddWithValue("@m", m);
        cmd.Parameters.AddWithValue("@run", runNumber);
        cmd.Parameters.AddWithValue("@elapsed", elapsedNs);
        cmd.Parameters.AddWithValue("@steps", stepCount);
        cmd.Parameters.AddWithValue("@theoretical", theoreticalNs);
        cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Обновляет итоги эксперимента (MSE, коэффициент C, вывод) после аппроксимации.</summary>
    public void UpdateExperimentSummary(long experimentId, double mse, double approximationC, string conclusion)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            UPDATE Experiments SET MSE = @mse, ApproximationC = @c, Conclusion = @conclusion
            WHERE Id = @id;
            """;
        cmd.Parameters.AddWithValue("@mse", mse);
        cmd.Parameters.AddWithValue("@c", approximationC);
        cmd.Parameters.AddWithValue("@conclusion", conclusion);
        cmd.Parameters.AddWithValue("@id", experimentId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// КЭШ: возвращает отдельные запуски точки из последнего эксперимента
    /// с данным ключом конфигурации. <paramref name="m"/> — внутреннее
    /// измерение матричного эксперимента A(n×m)·B(m×n); для одномерных
    /// замеров передаётся 1. false — если данных нет.
    /// </summary>
    public bool TryGetCachedRuns(string configHash, long n, long m, out IReadOnlyList<double> runTimesNs)
    {
        runTimesNs = Array.Empty<double>();
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT mm.ExperimentId, mm.ElapsedTimeNs
            FROM Measurements mm
            JOIN Experiments e ON e.Id = mm.ExperimentId
            WHERE e.ConfigHash = @hash AND mm.N = @n AND mm.M = @m
            ORDER BY mm.ExperimentId DESC, mm.RunNumber ASC;
            """;
        cmd.Parameters.AddWithValue("@hash", configHash);
        cmd.Parameters.AddWithValue("@n", n);
        cmd.Parameters.AddWithValue("@m", m);

        var runs = new List<double>();
        using (var reader = cmd.ExecuteReader())
        {
            long currentExperiment = -1;
            while (reader.Read())
            {
                long expId = reader.GetInt64(0);
                // Берём только запуски самого свежего эксперимента с этим n.
                if (currentExperiment == -1)
                {
                    currentExperiment = expId;
                }
                else if (expId != currentExperiment)
                {
                    break;
                }
                runs.Add(reader.GetDouble(1));
            }
        }
        if (runs.Count == 0)
        {
            return false;
        }
        runTimesNs = runs;
        return true;
    }

    /// <summary>
    /// КЭШ для операционного бенчмарка: возвращает подсчитанное число операций
    /// (StepCount) точки n из последнего эксперимента с данным ключом.
    /// </summary>
    public bool TryGetCachedOps(string configHash, long n, out double stepCount)
    {
        stepCount = 0;
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT m.StepCount
            FROM Measurements m
            JOIN Experiments e ON e.Id = m.ExperimentId
            WHERE e.ConfigHash = @hash AND m.N = @n
            ORDER BY m.ExperimentId DESC, m.RunNumber ASC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@hash", configHash);
        cmd.Parameters.AddWithValue("@n", n);
        var result = cmd.ExecuteScalar();
        if (result is null || result is DBNull)
        {
            return false;
        }
        stepCount = Convert.ToDouble(result);
        return true;
    }

    /// <summary>
    /// Принудительный пересчёт: удаляет ВСЕ эксперименты и измерения с данным
    /// ключом конфигурации. Старые и новые данные не смешиваются.
    /// </summary>
    /// <returns>Количество удалённых экспериментов.</returns>
    public long DeleteConfigData(string configHash)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Experiments WHERE ConfigHash = @hash; SELECT changes();";
        cmd.Parameters.AddWithValue("@hash", configHash);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Число сохранённых экспериментов (для строки статуса БД).</summary>
    public long CountExperiments()
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Experiments;";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Число сохранённых отдельных запусков.</summary>
    public long CountMeasurements()
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Measurements;";
        return (long)cmd.ExecuteScalar()!;
    }
}
