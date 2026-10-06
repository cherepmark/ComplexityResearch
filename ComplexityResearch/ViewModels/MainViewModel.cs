using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using ComplexityResearch.Algorithms;
using ComplexityResearch.Models;
using ComplexityResearch.Services;

namespace ComplexityResearch.ViewModels;

/// <summary>
/// Главная ViewModel приложения: выбор алгоритма, конфигурация эксперимента
/// (Nmax, шаг, запуски, seed, лимит времени), кэш БД, результаты
/// (таблица, график, MSE, вывод) и «операционный» бенчмарк возведения в степень.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly BenchmarkService _benchmarkService = new();
    private readonly OperationBenchmarkService _operationBenchmarkService = new();
    private readonly MatrixNMBenchmarkService _matrixNMService = new();
    private BenchmarkDatabase? _database;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isBusy;
    private ApproximationResult? _lastApproximation;

    /// <summary>Список зарегистрированных алгоритмов.</summary>
    public IReadOnlyList<AlgorithmBase> Algorithms { get; }

    private AlgorithmBase _selectedAlgorithm;

    /// <summary>Выбранный алгоритм. При смене подставляются его настройки по умолчанию.</summary>
    public AlgorithmBase SelectedAlgorithm
    {
        get => _selectedAlgorithm;
        set
        {
            if (SetProperty(ref _selectedAlgorithm, value) && value != null)
            {
                ApplyDefaultConfiguration(value.DefaultConfiguration);
            }
        }
    }

    private void ApplyDefaultConfiguration(BenchmarkConfiguration cfg)
    {
        StartNText = cfg.StartN.ToString();
        NMaxText = cfg.EndN.ToString();
        StepText = cfg.StepN.ToString();
        RandomSeedText = cfg.RandomSeed.ToString();
        MaxTimeText = cfg.MaxSecondsPerPoint.ToString("0.0");
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(SelectedComplexity));
        OnPropertyChanged(nameof(SelectedApplication));
        OnPropertyChanged(nameof(SelectedFormula));
    }

    /// <summary>Описание выбранного алгоритма.</summary>
    public string SelectedDescription => _selectedAlgorithm.Description;

    /// <summary>Сложность выбранного алгоритма.</summary>
    public string SelectedComplexity => _selectedAlgorithm.Complexity;

    /// <summary>Применение выбранного алгоритма.</summary>
    public string SelectedApplication => _selectedAlgorithm.Application;

    /// <summary>Формула теоретического времени выбранного алгоритма.</summary>
    public string SelectedFormula => _selectedAlgorithm.TheoreticalFormula;

    // --- Поля конфигурации (текстовые, с проверкой при запуске) ---

    private string _startNText = "1";
    private string _nMaxText = "1000";
    private string _stepText = "100";
    private string _runsText = "5";
    private string _randomSeedText = "20260914";
    private string _maxTimeText = "15.0";

    /// <summary>Начальное значение n.</summary>
    public string StartNText { get => _startNText; set => SetProperty(ref _startNText, value); }

    /// <summary>Nmax — максимальный размер входных данных.</summary>
    public string NMaxText { get => _nMaxText; set => SetProperty(ref _nMaxText, value); }

    /// <summary>Шаг между точками n.</summary>
    public string StepText { get => _stepText; set => SetProperty(ref _stepText, value); }

    /// <summary>Количество запусков на точку (по умолчанию 5; первый — прогревочный).</summary>
    public string RunsText { get => _runsText; set => SetProperty(ref _runsText, value); }

    /// <summary>Seed генератора случайных данных.</summary>
    public string RandomSeedText { get => _randomSeedText; set => SetProperty(ref _randomSeedText, value); }

    /// <summary>Максимально допустимое время одной точки, с.</summary>
    public string MaxTimeText { get => _maxTimeText; set => SetProperty(ref _maxTimeText, value); }

    private bool _useCache = true;

    /// <summary>Использовать кэш БД (результат уже измерялся — взять из БД).</summary>
    public bool UseCache { get => _useCache; set => SetProperty(ref _useCache, value); }

    private bool _forceRecalculation;

    /// <summary>Принудительный пересчёт: старые данные ключа удаляются, не смешиваются с новыми.</summary>
    public bool ForceRecalculation { get => _forceRecalculation; set => SetProperty(ref _forceRecalculation, value); }

    /// <summary>Модель стоимости элементарных операций.</summary>
    public OperationCostModel CostModel { get; } = OperationCostModel.CreateDefault();

    private string _costSummary;

    /// <summary>Текстовое описание констант (для панели интерфейса).</summary>
    public string CostSummary
    {
        get => _costSummary;
        private set => SetProperty(ref _costSummary, value);
    }

    private string _dbInfoText = "БД: не инициализирована";

    /// <summary>Информация о базе данных.</summary>
    public string DbInfoText
    {
        get => _dbInfoText;
        private set => SetProperty(ref _dbInfoText, value);
    }

    // --- Состояние выполнения ---

    /// <summary>Идёт ли сейчас эксперимент.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private string _statusText = "Готово";

    /// <summary>Текст статуса.</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private int _progressPercent;

    /// <summary>Процент выполнения (ProgressBar).</summary>
    public int ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    private string _currentDetailsText = string.Empty;

    /// <summary>Подробности текущего шага: алгоритм, n, точка, запуск.</summary>
    public string CurrentDetailsText
    {
        get => _currentDetailsText;
        set => SetProperty(ref _currentDetailsText, value);
    }

    /// <summary>Результаты по точкам n (DataGrid).</summary>
    public ObservableCollection<BenchmarkResult> Results { get; } = new();

    private string _conclusionText = string.Empty;

    /// <summary>Автоматический вывод по результатам эксперимента.</summary>
    public string ConclusionText
    {
        get => _conclusionText;
        set => SetProperty(ref _conclusionText, value);
    }

    private string _approximationText = string.Empty;

    /// <summary>Формула аппроксимации Tapprox(n) = C·f(n).</summary>
    public string ApproximationText
    {
        get => _approximationText;
        set => SetProperty(ref _approximationText, value);
    }

    private string _mseText = "MSE: —";

    /// <summary>Среднеквадратичная ошибка аппроксимации (рядом с графиком).</summary>
    public string MseText
    {
        get => _mseText;
        set => SetProperty(ref _mseText, value);
    }

    private string _powerResultsText = string.Empty;

    /// <summary>Результаты операционного бенчмарка возведения в степень.</summary>
    public string PowerResultsText
    {
        get => _powerResultsText;
        set => SetProperty(ref _powerResultsText, value);
    }

    // --- События для окна (график и экспорт PNG — чисто UI-функции) ---

    /// <summary>Данные для построения графика после эксперимента.</summary>
    public event EventHandler<ChartData>? ExperimentCompleted;

    /// <summary>Данные тепловой карты после двумерного эксперимента A(n×m)·B(m×n).</summary>
    public event EventHandler<HeatmapData>? HeatmapCompleted;

    /// <summary>Запрос на сохранение графика в PNG.</summary>
    public event EventHandler<string>? PngExportRequested;

    // --- Двумерный матричный эксперимент A(n×m)·B(m×n) ---

    private string _matrixNStartText = "50";
    private string _matrixNMaxText = "250";
    private string _matrixNStepText = "50";
    private string _matrixMStartText = "50";
    private string _matrixMMaxText = "250";
    private string _matrixMStepText = "50";
    private List<MatrixNMResult> _lastMatrixNMResults = new();
    private BenchmarkConfiguration _lastMatrixNRange = new();
    private BenchmarkConfiguration _lastMatrixMRange = new();
    private ChartData? _previousChartData;
    private bool _lastExperimentWasMatrixNM;

    /// <summary>Начальное n (строки матрицы A).</summary>
    public string MatrixNStartText { get => _matrixNStartText; set => SetProperty(ref _matrixNStartText, value); }

    /// <summary>Nmax по n.</summary>
    public string MatrixNMaxText { get => _matrixNMaxText; set => SetProperty(ref _matrixNMaxText, value); }

    /// <summary>Шаг по n.</summary>
    public string MatrixNStepText { get => _matrixNStepText; set => SetProperty(ref _matrixNStepText, value); }

    /// <summary>Начальное m (внутреннее измерение).</summary>
    public string MatrixMStartText { get => _matrixMStartText; set => SetProperty(ref _matrixMStartText, value); }

    /// <summary>Mmax по m.</summary>
    public string MatrixMMaxText { get => _matrixMMaxText; set => SetProperty(ref _matrixMMaxText, value); }

    /// <summary>Шаг по m.</summary>
    public string MatrixMStepText { get => _matrixMStepText; set => SetProperty(ref _matrixMStepText, value); }

    private bool _overlayPrevious;

    /// <summary>
    /// Наложить предыдущий эксперимент на одни координаты: на графике будут
    /// видны серии текущего И предыдущего запуска одновременно.
    /// </summary>
    public bool OverlayPrevious { get => _overlayPrevious; set => SetProperty(ref _overlayPrevious, value); }

    private string _matrixResultsText = string.Empty;

    /// <summary>Сводка по двумерному эксперименту.</summary>
    public string MatrixResultsText
    {
        get => _matrixResultsText;
        set => SetProperty(ref _matrixResultsText, value);
    }

    /// <summary>Команда запуска двумерного матричного эксперимента.</summary>
    public RelayCommand RunMatrixNMCommand { get; }

    /// <summary>Создаёт ViewModel; регистрирует команды, алгоритмы и БД.</summary>
    public MainViewModel()
    {
        Algorithms = AlgorithmRegistry.CreateDefaultAlgorithms();
        _selectedAlgorithm = Algorithms[0];
        ApplyDefaultConfiguration(_selectedAlgorithm.DefaultConfiguration);
        _costSummary = CostModel.ToDisplayString();

        InitDatabase();

        PrepareDataCommand = new RelayCommand(_ => PrepareData(), _ => !IsBusy);
        RunExperimentCommand = new RelayCommand(_ => RunExperimentAsync(), _ => !IsBusy);
        RunPowerExperimentCommand = new RelayCommand(_ => RunPowerExperimentAsync(), _ => !IsBusy);
        RunMatrixNMCommand = new RelayCommand(_ => RunMatrixNMAsync(), _ => !IsBusy);
        CancelCommand = new RelayCommand(_ => _cancellationTokenSource?.Cancel(), _ => IsBusy);
        CalibrateCommand = new RelayCommand(_ => CalibrateAsync(), _ => !IsBusy);
        ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => !IsBusy && (Results.Count > 0 || _lastMatrixNMResults.Count > 0));
        ExportJsonCommand = new RelayCommand(_ => ExportJson(), _ => !IsBusy && (Results.Count > 0 || _lastMatrixNMResults.Count > 0));
        ExportPngCommand = new RelayCommand(_ => ExportPng(), _ => !IsBusy && (Results.Count > 0 || _lastMatrixNMResults.Count > 0));
    }

    /// <summary>Команда «Подготовить данные».</summary>
    public RelayCommand PrepareDataCommand { get; }

    /// <summary>Команда «Запустить эксперимент».</summary>
    public RelayCommand RunExperimentCommand { get; }

    /// <summary>Команда «Возведение в степень (операции)».</summary>
    public RelayCommand RunPowerExperimentCommand { get; }

    /// <summary>Команда «Остановить».</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Команда «Калибровка констант».</summary>
    public RelayCommand CalibrateCommand { get; }

    /// <summary>Команда экспорта в CSV.</summary>
    public RelayCommand ExportCsvCommand { get; }

    /// <summary>Команда экспорта в JSON.</summary>
    public RelayCommand ExportJsonCommand { get; }

    /// <summary>Команда сохранения графика в PNG.</summary>
    public RelayCommand ExportPngCommand { get; }

    private void InitDatabase()
    {
        try
        {
            _database = new BenchmarkDatabase(BenchmarkDatabase.DefaultDbPath);
            DbInfoText = $"БД: {_database.DbPath} (экспериментов: {_database.CountExperiments()}, " +
                         $"запусков: {_database.CountMeasurements()})";
        }
        catch (Exception ex)
        {
            _database = null;
            DbInfoText = $"БД недоступна ({ex.Message}). Эксперименты работают без кэша.";
        }
    }

    private BenchmarkConfiguration ReadConfiguration()
    {
        return new BenchmarkConfiguration
        {
            StartN = ParseLong(StartNText, "Начальное n"),
            EndN = ParseLong(NMaxText, "Nmax"),
            StepN = ParseLong(StepText, "Шаг (step)"),
            RunsPerPoint = (int)ParseLong(RunsText, "Количество запусков"),
            RandomSeed = (int)ParseLong(RandomSeedText, "Seed"),
            MaxSecondsPerPoint = ParseDouble(MaxTimeText, "Лимит времени точки"),
            PhysicalBlockLimit = SumAlgorithm.BlockLimit
        };
    }

    private static long ParseLong(string text, string fieldName)
    {
        string cleaned = text.Replace(" ", "").Replace("\u00A0", "");
        if (!long.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
        {
            throw new ArgumentException($"Некорректное значение поля «{fieldName}»: «{text}».");
        }
        return value;
    }

    private static double ParseDouble(string text, string fieldName)
    {
        if (!double.TryParse(text.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new ArgumentException($"Некорректное значение поля «{fieldName}»: «{text}».");
        }
        return value;
    }

    private void PrepareData()
    {
        try
        {
            var config = ReadConfiguration();
            BenchmarkService.ValidateConfigurationPublic(config);
            var sizes = BenchmarkService.GenerateSizes(config);
            var algo = SelectedAlgorithm;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Алгоритм: {algo.Name}");
            sb.AppendLine($"Точки n ({sizes.Length}): {string.Join(", ", sizes.Take(10))}{(sizes.Length > 10 ? ", …" : string.Empty)}");
            long maxMemoryMb = 0;
            foreach (long n in sizes)
            {
                var mode = algo.ModeFor(n);
                long memoryMb = mode == MeasurementMode.Direct ? n * 4 / (1024 * 1024) : SumAlgorithm.BlockLimit * 4 / (1024 * 1024);
                maxMemoryMb = Math.Max(maxMemoryMb, memoryMb);
            }
            sb.AppendLine($"Память на максимальной точке: ≈ {maxMemoryMb:N0} МБ");
            sb.AppendLine($"Запусков на точку: {config.RunsPerPoint} (первый — прогревочный)");
            sb.AppendLine($"Лимит времени точки: {(config.MaxSecondsPerPoint > 0 ? config.MaxSecondsPerPoint + " с" : "без ограничения")}");
            sb.AppendLine($"Оценка суммарного времени: ≈ {EstimateTotalTime(sizes, config):F0} с");
            sb.AppendLine($"Кэш: {(UseCache ? "включён" : "выключен")}{(ForceRecalculation ? " + принудительный пересчёт" : string.Empty)}");
            StatusText = "Данные подготовлены";
            ConclusionText = sb.ToString();
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
        }
    }

    /// <summary>Грубая оценка суммарного времени эксперимента по теоретической модели.</summary>
    private double EstimateTotalTime(long[] sizes, BenchmarkConfiguration config)
    {
        double total = 0;
        foreach (long n in sizes)
        {
            total += _selectedAlgorithm.EstimateTheoreticalTimeNs(n, CostModel) * (config.RunsPerPoint + 1) / 1e9;
        }
        return total;
    }

    private async Task RunExperimentAsync()
    {
        BenchmarkConfiguration config;
        try
        {
            config = ReadConfiguration();
            BenchmarkService.ValidateConfigurationPublic(config);
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
            return;
        }

        IsBusy = true;
        Results.Clear();
        ConclusionText = string.Empty;
        MseText = "MSE: —";
        StatusText = "Выполняется…";
        _cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<BenchmarkProgress>(p =>
        {
            ProgressPercent = p.PercentComplete;
            CurrentDetailsText = p.DetailsText;
            StatusText = $"Выполняется {p.PercentComplete}%";
        });

        try
        {
            var outcome = await _benchmarkService.RunAsync(
                SelectedAlgorithm, config, CostModel, progress, _cancellationTokenSource.Token,
                onPointCompleted: r => Application.Current?.Dispatcher.BeginInvoke(() => Results.Add(r)),
                database: _database,
                useCache: UseCache,
                forceRecalculation: ForceRecalculation);

            ProgressPercent = 100;
            StatusText = $"Готово: {outcome.Results.Count} точек, «{_selectedAlgorithm.Name}»";
            OnExperimentCompleted(outcome.Results, outcome.ExperimentId);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Эксперимент остановлен пользователем. Частичные результаты сохранены.";
            OnExperimentCompleted(Results.ToList(), experimentId: -1);
        }
        catch (PointTimeoutException ex)
        {
            StatusText = "Остановлено: превышен лимит времени точки.";
            CurrentDetailsText = string.Empty;
            MessageBox.Show(ex.Message, "Лимит времени", MessageBoxButton.OK, MessageBoxImage.Warning);
            OnExperimentCompleted(Results.ToList(), experimentId: -1);
        }
        catch (OutOfMemoryException)
        {
            StatusText = "Ошибка";
            ShowError("Недостаточно памяти для массива такого размера. Уменьшите Nmax " +
                      "или используйте алгоритмы с масштабированным режимом (сумма, произведение, Горнер).");
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка";
            ShowError($"Ошибка выполнения эксперимента: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            RefreshDbInfo();
        }
    }

    /// <summary>Обновляет информацию о БД в статусной строке.</summary>
    private void RefreshDbInfo()
    {
        if (_database == null) return;
        try
        {
            DbInfoText = $"БД: {_database.DbPath} (экспериментов: {_database.CountExperiments()}, " +
                         $"запусков: {_database.CountMeasurements()})";
        }
        catch
        {
            // БД могла быть занята — не критично для статуса.
        }
    }

    private void OnExperimentCompleted(IReadOnlyList<BenchmarkResult> results, long experimentId)
    {
        if (results.Count == 0) return;

        // Аппроксимация Tapprox(n) = C·f(n) и MSE.
        var fit = ApproximationService.Fit(
            results.Select(r => ((double)r.LogicalN, r.Statistics.MeanNs)).ToList(),
            _selectedAlgorithm.ComplexityClass);
        _lastApproximation = fit;
        ApproximationText = fit.Formula;
        MseText = $"MSE = {FormatMse(fit.MSE)} нс²  — R² = {fit.R2:F4}";

        foreach (var r in Results)
        {
            r.ApproxNs = fit.Evaluate(r.LogicalN);
        }

        // График: эксперимент + теория + аппроксимация (в заголовке серии — MSE).
        var series = BuildChartSeries(results, fit);
        _lastExperimentWasMatrixNM = false;

        // Задача «два графика на одних координатах»: если включён режим
        // наложения и есть предыдущий эксперимент — добавляем его серии
        // (с пометкой «пред.») на те же оси.
        var allSeries = new List<ChartSeriesData>(series);
        string chartTitle = $"{_selectedAlgorithm.Name} — MSE = {FormatMse(fit.MSE)} нс²";
        if (OverlayPrevious && _previousChartData != null)
        {
            foreach (var s in _previousChartData.Series)
            {
                allSeries.Add(new ChartSeriesData($"пред. {s.Title}", s.ColorHex, s.Points, Dashed: true, ShowPoints: s.ShowPoints));
            }
            chartTitle += " (наложен предыдущий эксперимент)";
        }
        ExperimentCompleted?.Invoke(this, new ChartData(chartTitle, allSeries));
        // Запоминаем ТЕКУЩИЙ эксперимент (без наложенных серий) как предыдущий.
        _previousChartData = new ChartData(_selectedAlgorithm.Name, series);

        ConclusionText = BuildConclusion(_selectedAlgorithm, results, fit);

        // Итоги эксперимента (MSE, коэффициент C, вывод) — в БД.
        if (_database != null && experimentId > 0)
        {
            try
            {
                _database.UpdateExperimentSummary(experimentId, fit.MSE, fit.C, ConclusionText);
            }
            catch
            {
                // Итоги в БД не критичны для работы приложения.
            }
        }
    }

    /// <summary>Строит серии графика: эксперимент (синяя), теория (оранжевая), аппроксимация (зелёная, пунктир).</summary>
    private static IReadOnlyList<ChartSeriesData> BuildChartSeries(IReadOnlyList<BenchmarkResult> results, ApproximationResult fit)
    {
        var experimental = new List<ChartPointData>(results.Count);
        var theoretical = new List<ChartPointData>(results.Count);
        foreach (var r in results)
        {
            experimental.Add(new ChartPointData(r.LogicalN, r.Statistics.MeanNs / 1e9));
            theoretical.Add(new ChartPointData(r.LogicalN, r.Statistics.TheoreticalNs / 1e9));
        }

        var approximation = results
            .Select(r => new ChartPointData(r.LogicalN, fit.Evaluate(r.LogicalN) / 1e9))
            .ToList();

        return
        [
            new ChartSeriesData("Эксперимент (среднее)", "#1F6FEB", experimental, Dashed: false, ShowPoints: true),
            new ChartSeriesData("Теоретическая модель", "#F0821E", theoretical, Dashed: false, ShowPoints: true),
            new ChartSeriesData($"Аппроксимация C·f(n), MSE = {FormatMse(fit.MSE)} нс²", "#2E9E44", approximation, Dashed: true, ShowPoints: false)
        ];
    }

    /// <summary>
    /// Формирует вывод по реальным данным эксперимента: наклон log t / log n,
    /// MSE, средняя относительная ошибка, вердикт о соответствии теории и практики.
    /// </summary>
    private string BuildConclusion(AlgorithmBase algorithm, IReadOnlyList<BenchmarkResult> results, ApproximationResult fit)
    {
        var usable = results
            .Where(r => r.LogicalN > 1 && r.Statistics.MeanNs > 0)
            .Select(r => ((double)r.LogicalN, r.Statistics.MeanNs))
            .ToList();

        double slope = EstimateLogLogSlope(usable);
        double theoreticalSlope = ApproximationService.GetTheoreticalSlope(algorithm.ComplexityClass);
        double avgError = results.Average(r => r.Statistics.RelDiffPercent);
        int cached = results.Count(r => r.IsFromCache);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Теоретическая сложность: {algorithm.Complexity}");
        sb.AppendLine();
        sb.AppendLine("Эксперимент показал:");
        if (algorithm.ComplexityClass is "O(1)" or "O(log n)")
        {
            sb.AppendLine(usable.Count >= 2
                ? $"при увеличении n время изменяется слабо (наклон log t / log n ≈ {slope:F2}), что соответствует {algorithm.ComplexityClass}."
                : "данных недостаточно для оценки роста.");
        }
        else
        {
            sb.AppendLine($"при увеличении n время возрастает приблизительно как n^{slope:F2} " +
                          $"(теоретический показатель: {theoreticalSlope:F1}).");
        }
        sb.AppendLine();
        sb.AppendLine($"Аппроксимация: {fit.Formula}");
        sb.AppendLine($"MSE = {FormatMse(fit.MSE)} нс² (средний квадрат отклонения эксперимента от кривой C·f(n); " +
                      "низкий MSE подтверждает форму роста, но не является абсолютным доказательством модели).");
        sb.AppendLine($"Средняя относительная ошибка (эксперимент vs теоретическая модель): {avgError:F1}%");
        if (cached > 0)
        {
            sb.AppendLine($"Из кэша БД взято точек: {cached} из {results.Count}.");
        }
        sb.AppendLine();
        sb.AppendLine("Вывод:");
        string verdict = avgError switch
        {
            < 15 => "Экспериментальные данные хорошо соответствуют теоретической оценке.",
            < 40 => "Экспериментальные данные в целом соответствуют теоретической оценке.",
            _ => "Экспериментальные данные заметно отличаются от теоретической оценки."
        };
        sb.AppendLine(verdict + " Абсолютное время отличается из-за иерархии кэша процессора, " +
                      "оптимизаций JIT (SIMD), работы GC и планировщика ОС. Для точного совпадения " +
                      "выполните калибровку констант.");

        bool hasScaled = results.Any(r => r.Mode == MeasurementMode.Scaled && !r.IsFromCache);
        if (hasScaled)
        {
            sb.AppendLine();
            sb.AppendLine("Примечание: часть точек измерена в масштабированном режиме " +
                          "(блок 50 млн элементов, число проходов пропорционально n).");
        }
        return sb.ToString();
    }

    private static double EstimateLogLogSlope(List<(double N, double T)> points)
    {
        if (points.Count < 2) return 0;
        double meanX = 0, meanY = 0;
        foreach (var (n, t) in points)
        {
            meanX += Math.Log10(n);
            meanY += Math.Log10(t);
        }
        meanX /= points.Count;
        meanY /= points.Count;
        double cov = 0, variance = 0;
        foreach (var (n, t) in points)
        {
            double dx = Math.Log10(n) - meanX;
            double dy = Math.Log10(t) - meanY;
            cov += dx * dy;
            variance += dx * dx;
        }
        return variance > 0 ? cov / variance : 0;
    }

    /// <summary>
    /// «Операционный» бенчмарк возведения в степень: итеративный/рекурсивный/бинарный,
    /// n = 1..1000, измеряется количество элементарных операций (не время).
    /// </summary>
    private async Task RunPowerExperimentAsync()
    {
        var config = new BenchmarkConfiguration
        {
            StartN = 1,
            EndN = 1000,
            StepN = 50,
            RunsPerPoint = 1,
            RandomSeed = 20260914,
            MaxSecondsPerPoint = 0
        };

        IsBusy = true;
        StatusText = "Возведение в степень: подсчёт операций…";
        _cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<BenchmarkProgress>(p =>
        {
            ProgressPercent = p.PercentComplete;
            CurrentDetailsText = p.DetailsText;
        });

        try
        {
            var results = await _operationBenchmarkService.RunAsync(
                config, progress, _cancellationTokenSource.Token,
                database: _database, useCache: UseCache, forceRecalculation: ForceRecalculation);

            // Один график: три серии эксперимента + три теоретические кривые.
            var series = new List<ChartSeriesData>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Возведение в степень, n = 1..1000. Измеряется количество операций (не время):");
            sb.AppendLine();

            string[] colors = ["#1F6FEB", "#D62728", "#9467BD"];
            string[] theoColors = ["#7FA9E8", "#E88B8B", "#C3A8DF"];
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                series.Add(new ChartSeriesData(
                    $"{r.Algorithm.Name} (эксперимент)",
                    colors[i % colors.Length],
                    r.Points.Select(p => new ChartPointData(p.N, p.MeasuredOperations)).ToList(),
                    ShowPoints: true));
                series.Add(new ChartSeriesData(
                    $"{r.Algorithm.Complexity} — теория",
                    theoColors[i % theoColors.Length],
                    r.Points.Select(p => new ChartPointData(p.N, p.TheoreticalOperations)).ToList(),
                    Dashed: true,
                    ShowPoints: false));

                sb.AppendLine($"{r.Algorithm.Name}:");
                sb.AppendLine($"  {r.Approximation.Formula}");
                sb.AppendLine($"  MSE = {FormatMse(r.Approximation.MSE)} (операций²)");
            }

            PowerResultsText = sb.ToString();
            MseText = "MSE (по степеням): " + string.Join(" | ",
                results.Select(r => $"{r.Algorithm.Complexity}: {FormatMse(r.Approximation.MSE)}"));

            ExperimentCompleted?.Invoke(this, new ChartData(
                "Возведение в степень: количество операций vs n",
                series));
            StatusText = "Возведение в степень: готово";
            ProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Остановлено пользователем.";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка";
            ShowError($"Ошибка операционного бенчмарка: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            RefreshDbInfo();
        }
    }

    /// <summary>
    /// Двумерный матричный эксперимент A(n×m)·B(m×n): сетка значений n и m,
    /// все тройки (n, m, время) одной тепловой картой.
    /// </summary>
    private async Task RunMatrixNMAsync()
    {
        BenchmarkConfiguration nCfg, mCfg;
        try
        {
            nCfg = new BenchmarkConfiguration
            {
                StartN = ParseLong(MatrixNStartText, "Начальное n"),
                EndN = ParseLong(MatrixNMaxText, "Nmax по n"),
                StepN = ParseLong(MatrixNStepText, "Шаг по n"),
                RunsPerPoint = (int)ParseLong(RunsText, "Количество запусков"),
                RandomSeed = (int)ParseLong(RandomSeedText, "Seed"),
                MaxSecondsPerPoint = ParseDouble(MaxTimeText, "Лимит времени ячейки")
            };
            mCfg = new BenchmarkConfiguration
            {
                StartN = ParseLong(MatrixMStartText, "Начальное m"),
                EndN = ParseLong(MatrixMMaxText, "Mmax по m"),
                StepN = ParseLong(MatrixMStepText, "Шаг по m"),
                RunsPerPoint = (int)ParseLong(RunsText, "Количество запусков"),
                RandomSeed = (int)ParseLong(RandomSeedText, "Seed"),
                MaxSecondsPerPoint = ParseDouble(MaxTimeText, "Лимит времени ячейки")
            };
            MatrixNMBenchmarkService.Validate(nCfg, mCfg);
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
            return;
        }

        // Предупреждение: общее число замеров = (кол-во n) × (кол-во m) × запуски.
        var nSizes = BenchmarkService.GenerateSizes(nCfg);
        var mSizes = BenchmarkService.GenerateSizes(mCfg);
        long totalMeasurements = (long)nSizes.Length * mSizes.Length * (nCfg.RunsPerPoint + 1);
        bool bigMaxSmallStep =
            (nCfg.EndN >= 500 && nCfg.StepN <= 25) || (mCfg.EndN >= 500 && mCfg.StepN <= 25);
        if (totalMeasurements > 500 || bigMaxSmallStep)
        {
            var answer = MessageBox.Show(
                "Диапазоны n и m заданы так, что эксперимент может выполняться очень долго:\n\n" +
                $"значений n: {nSizes.Length}, значений m: {mSizes.Length}, запусков на ячейку: {nCfg.RunsPerPoint + 1} (включая прогрев).\n" +
                $"Общее число замеров = {nSizes.Length} × {mSizes.Length} × {nCfg.RunsPerPoint + 1} = {totalMeasurements:N0}." +
                (bigMaxSmallStep ? "\nМаксимум по оси ≥ 500 при маленьком шаге." : string.Empty) +
                "\n\nПродолжить?",
                "Большая сетка замеров", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        IsBusy = true;
        _lastMatrixNMResults.Clear();
        Results.Clear();
        MatrixResultsText = string.Empty;
        MseText = "MSE: —";
        StatusText = "Выполняется…";
        _cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<BenchmarkProgress>(p =>
        {
            ProgressPercent = p.PercentComplete;
            CurrentDetailsText = p.DetailsText;
            StatusText = $"Выполняется {p.PercentComplete}%";
        });

        try
        {
            var outcome = await _matrixNMService.RunAsync(
                nCfg, mCfg, CostModel, progress, _cancellationTokenSource.Token,
                database: _database, useCache: UseCache, forceRecalculation: ForceRecalculation);

            _lastMatrixNMResults = outcome.Results.ToList();
            _lastMatrixNRange = nCfg;
            _lastMatrixMRange = mCfg;
            _lastExperimentWasMatrixNM = true;
            ProgressPercent = 100;
            StatusText = $"Готово: {_lastMatrixNMResults.Count} ячеек (n×m)";

            // ОДНО связное представление: все тройки (n, m, время) на тепловой карте.
            var cells = _lastMatrixNMResults
                .Select(r => new HeatmapCell(r.N, r.M, r.Statistics.MeanNs / 1e9))
                .ToList();
            HeatmapCompleted?.Invoke(this, new HeatmapData(
                "Умножение матриц A(n×m)·B(m×n) — время, с",
                "n (строки A)",
                "m (столбцы A = строки B)",
                "Время, с",
                cells));

            // Модель t = C·n²·m: коэффициент C и MSE по всем тройкам.
            var fit = MatrixNMBenchmarkService.FitPowerNM(_lastMatrixNMResults);
            MseText = $"MSE = {FormatMse(fit.MSE)} нс² (модель t = C·n²·m), C = {FormatMse(fit.C)}";

            var cached = _lastMatrixNMResults.Count(r => r.IsFromCache);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Двумерный эксперимент: {nSizes.Length} значений n × {mSizes.Length} значений m = {_lastMatrixNMResults.Count} троек (n, m, время).");
            sb.AppendLine($"Аппроксимация: {fit.Formula} (теория: t ∝ n²·m — n² ячеек результата по m умножений каждая).");
            sb.AppendLine($"MSE = {FormatMse(fit.MSE)} нс². Из кэша БД: {cached} ячеек.");
            sb.AppendLine("Тёплый цвет ячейки — большее время; рост виден одновременно по обеим осям.");
            MatrixResultsText = sb.ToString();
            ConclusionText = sb.ToString();

            if (_database != null && outcome.ExperimentId > 0)
            {
                try
                {
                    _database.UpdateExperimentSummary(outcome.ExperimentId, fit.MSE, fit.C, sb.ToString());
                }
                catch
                {
                    // Итоги в БД не критичны.
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Остановлено пользователем. Частичные результаты в БД сохранены.";
        }
        catch (PointTimeoutException ex)
        {
            StatusText = "Остановлено: превышен лимит времени ячейки.";
            MessageBox.Show(ex.Message, "Лимит времени", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (OutOfMemoryException)
        {
            StatusText = "Ошибка";
            ShowError("Недостаточно памяти для матриц такого размера. Уменьшите Nmax/Mmax.");
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка";
            ShowError($"Ошибка двумерного эксперимента: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            RefreshDbInfo();
        }
    }

    private async Task CalibrateAsync()
    {
        StatusText = "Калибровка констант (несколько секунд)…";
        try
        {
            var model = await Task.Run(() => CalibrationService.Calibrate());
            CostModel.AdditionCost = model.AdditionCost;
            CostModel.MultiplicationCost = model.MultiplicationCost;
            CostModel.ComparisonCost = model.ComparisonCost;
            CostModel.AssignmentCost = model.AssignmentCost;
            CostModel.SwapCost = model.SwapCost;
            CostModel.ArrayAccessCost = model.ArrayAccessCost;
            CostModel.CalibratedAtUtc = model.CalibratedAtUtc;
            CostSummary = CostModel.ToDisplayString();
            OnPropertyChanged(nameof(CostSummary));
            StatusText = "Калибровка завершена: константы обновлены (кэш ключей изменится — результаты будут пересчитаны).";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка калибровки";
            ShowError($"Не удалось выполнить калибровку: {ex.Message}");
        }
    }

    private void ExportCsv() => Export("csv", path =>
    {
        if (_lastExperimentWasMatrixNM)
        {
            ExportService.ExportMatrixNmCsv(
                MatrixNMBenchmarkService.ExperimentName, _lastMatrixNRange, _lastMatrixMRange,
                _lastMatrixNMResults, MatrixResultsText, path);
            return;
        }
        var report = ExportService.CreateReport(SelectedAlgorithm, ReadConfiguration(), CostModel, Results, ConclusionText, _lastApproximation);
        ExportService.ExportCsv(report, path);
    });

    private void ExportJson() => Export("json", path =>
    {
        if (_lastExperimentWasMatrixNM)
        {
            ExportService.ExportMatrixNmJson(
                MatrixNMBenchmarkService.ExperimentName, _lastMatrixNRange, _lastMatrixMRange,
                _lastMatrixNMResults, MatrixResultsText, path);
            return;
        }
        var report = ExportService.CreateReport(SelectedAlgorithm, ReadConfiguration(), CostModel, Results, ConclusionText, _lastApproximation);
        ExportService.ExportJson(report, path);
    });

    private void ExportPng() =>
        PngExportRequested?.Invoke(this,
            ExportService.MakeFileStem(_lastExperimentWasMatrixNM ? "MatrixNM" : SelectedAlgorithm.Name) + ".png");

    private static void Export(string extension, Action<string> save)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = extension == "csv" ? "CSV (*.csv)|*.csv" : "JSON (*.json)|*.json",
            FileName = "results." + extension
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                save(dialog.FileName);
                MessageBox.Show($"Файл сохранён: {dialog.FileName}", "Экспорт",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить файл: {ex.Message}", "Экспорт",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private static string FormatMse(double mse) =>
        mse >= 0.001 && mse < 10000 ? mse.ToString("0.000") : mse.ToString("E3");

    private static void ShowError(string message) =>
        MessageBox.Show(message, "Проверьте параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
}
