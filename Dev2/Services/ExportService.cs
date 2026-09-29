using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ComplexityResearch.Algorithms;
using ComplexityResearch.Models;

namespace ComplexityResearch.Services;

/// <summary>
/// Экспорт результатов эксперимента в CSV и JSON.
/// </summary>
public static class ExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // кириллица читается в файле «как есть»
    };

    /// <summary>Одна строка экспортируемого отчёта.</summary>
    public sealed class ExperimentRow
    {
        public long N { get; set; }
        public string Mode { get; set; } = string.Empty;
        public int Runs { get; set; }
        public double MeanNs { get; set; }
        public double MinNs { get; set; }
        public double MaxNs { get; set; }
        public double StdDevNs { get; set; }
        public double OperationsTotal { get; set; }
        public double TheoreticalNs { get; set; }
        public double ApproxNs { get; set; }
        public double AbsDiffNs { get; set; }
        public double RelDiffPercent { get; set; }
        public List<double> RunTimesNs { get; set; } = new();
    }

    /// <summary>Полный отчёт об эксперименте.</summary>
    public sealed class ExperimentReport
    {
        public string AlgorithmName { get; set; } = string.Empty;
        public string Complexity { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public BenchmarkConfiguration Configuration { get; set; } = new();
        public OperationCostModel CostModel { get; set; } = new();
        public string MeasurementModeNote { get; set; } = string.Empty;
        public string Conclusion { get; set; } = string.Empty;

        /// <summary>Аппроксимация: формула, коэффициент C, MSE.</summary>
        public ApproximationResult? Approximation { get; set; }

        public List<ExperimentRow> Rows { get; set; } = new();
    }

    /// <summary>
    /// Собирает отчёт из результатов эксперимента.
    /// </summary>
    public static ExperimentReport CreateReport(
        AlgorithmBase algorithm,
        BenchmarkConfiguration config,
        OperationCostModel costModel,
        IReadOnlyList<BenchmarkResult> results,
        string conclusion,
        ApproximationResult? approximation)
    {
        var report = new ExperimentReport
        {
            AlgorithmName = algorithm.Name,
            Complexity = algorithm.Complexity,
            CreatedUtc = DateTime.UtcNow,
            Configuration = config.Clone(),
            CostModel = costModel.Clone(),
            MeasurementModeNote = BuildModeNote(results),
            Conclusion = conclusion,
            Approximation = approximation,
            Rows = new List<ExperimentRow>(results.Count)
        };

        foreach (var r in results)
        {
            report.Rows.Add(new ExperimentRow
            {
                N = r.LogicalN,
                Mode = r.ModeText,
                Runs = r.RunsCount,
                MeanNs = r.Statistics.MeanNs,
                MinNs = r.Statistics.MinNs,
                MaxNs = r.Statistics.MaxNs,
                StdDevNs = r.Statistics.StdDevNs,
                OperationsTotal = r.Operations.Total,
                TheoreticalNs = r.Statistics.TheoreticalNs,
                ApproxNs = r.ApproxNs,
                AbsDiffNs = r.Statistics.AbsDiffNs,
                RelDiffPercent = r.Statistics.RelDiffPercent,
                RunTimesNs = r.RunTimesNs.ToList()
            });
        }
        return report;
    }

    /// <summary>Сохраняет отчёт в CSV.</summary>
    public static void ExportCsv(ExperimentReport report, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Экспорт результатов исследования временной сложности");
        sb.AppendLine($"# Алгоритм: {report.AlgorithmName}");
        sb.AppendLine($"# Теоретическая сложность: {report.Complexity}");
        sb.AppendLine($"# Дата (UTC): {report.CreatedUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# Конфигурация: StartN={report.Configuration.StartN}; Nmax={report.Configuration.EndN}; " +
                      $"step={report.Configuration.StepN}; запусков={report.Configuration.RunsPerPoint}; " +
                      $"seed={report.Configuration.RandomSeed}; лимит точки={report.Configuration.MaxSecondsPerPoint} с");
        sb.AppendLine($"# Константы (нс): сложение={report.CostModel.AdditionCost:F3}; умножение={report.CostModel.MultiplicationCost:F3}; " +
                      $"сравнение={report.CostModel.ComparisonCost:F3}; присваивание={report.CostModel.AssignmentCost:F3}; " +
                      $"обмен={report.CostModel.SwapCost:F3}; доступ к массиву={report.CostModel.ArrayAccessCost:F3}");
        if (report.Approximation != null)
        {
            sb.AppendLine($"# Аппроксимация: {report.Approximation.Formula}");
            sb.AppendLine($"# {report.Approximation.MseText}");
        }
        sb.AppendLine($"# {report.MeasurementModeNote}");
        sb.AppendLine();
        sb.AppendLine("n;Режим;Запусков;Среднее_нс;Мин_нс;Макс_нс;СтОткл_нс;Операций;Теоретическое_нс;Аппроксимация_нс;Абс_ошибка_нс;Ошибка_%");
        foreach (var row in report.Rows)
        {
            sb.AppendLine(string.Join(';',
                row.N.ToString(CultureInfo.InvariantCulture),
                row.Mode,
                row.Runs.ToString(CultureInfo.InvariantCulture),
                row.MeanNs.ToString("F1", CultureInfo.InvariantCulture),
                row.MinNs.ToString("F1", CultureInfo.InvariantCulture),
                row.MaxNs.ToString("F1", CultureInfo.InvariantCulture),
                row.StdDevNs.ToString("F1", CultureInfo.InvariantCulture),
                row.OperationsTotal.ToString("F0", CultureInfo.InvariantCulture),
                row.TheoreticalNs.ToString("F1", CultureInfo.InvariantCulture),
                row.ApproxNs.ToString("F1", CultureInfo.InvariantCulture),
                row.AbsDiffNs.ToString("F1", CultureInfo.InvariantCulture),
                row.RelDiffPercent.ToString("F1", CultureInfo.InvariantCulture)));
        }
        sb.AppendLine();
        sb.AppendLine("# Вывод:");
        foreach (var line in report.Conclusion.Split(Environment.NewLine))
        {
            sb.AppendLine($"# {line}");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Сохраняет отчёт в JSON (включая отдельные запуски каждой точки).</summary>
    public static void ExportJson(ExperimentReport report, string path)
    {
        string json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Безопасное имя файла из названия алгоритма.</summary>
    public static string MakeFileName(string algorithmName, string extension) =>
        $"{MakeFileStem(algorithmName)}.{extension}";

    /// <summary>База имени файла: «QuickSort_2026-09-14».</summary>
    public static string MakeFileStem(string algorithmName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in algorithmName)
        {
            if (Array.IndexOf(invalid, c) >= 0) continue;
            if (c is '№' or '—' or ' ' or '.') continue;
            sb.Append(c);
        }
        string stem = sb.ToString().Trim();
        if (stem.Length == 0) stem = "Experiment";
        return $"{stem}_{DateTime.Now:yyyy-MM-dd}";
    }

    private static string BuildModeNote(IReadOnlyList<BenchmarkResult> results)
    {
        if (results.All(r => r.IsFromCache))
        {
            return "Все результаты взяты из кэша БД (конфигурация уже измерялась).";
        }
        bool hasDirect = results.Any(r => r.Mode == MeasurementMode.Direct && !r.IsFromCache);
        bool hasScaled = results.Any(r => r.Mode == MeasurementMode.Scaled && !r.IsFromCache);
        if (hasScaled && hasDirect)
        {
            return "Режимы измерения: прямой (n ≤ 50 млн) и масштабированный (блок 50 млн элементов, " +
                   "число проходов пропорционально n). Масштабированные точки НЕ являются полным " +
                   "физическим измерением обработки n элементов.";
        }
        if (hasScaled)
        {
            return "Режим измерения: масштабированный — алгоритм обрабатывает блок 50 млн элементов, " +
                   "число проходов пропорционально логическому n. Результат не является полным физическим " +
                   "измерением обработки n элементов в одном массиве.";
        }
        return "Режим измерения: прямой — алгоритм физически обрабатывает массив размера n.";
    }
}
