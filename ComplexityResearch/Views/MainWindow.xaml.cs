using System.Windows;
using System.Windows.Media;
using ComplexityResearch.Controls;
using ComplexityResearch.Models;
using ComplexityResearch.ViewModels;

namespace ComplexityResearch.Views;

/// <summary>
/// Главное окно приложения: настройки эксперимента слева, график и таблица
/// результатов справа. Код-файл отвечает только за передачу данных ViewModel
/// в ChartControl и экспорт PNG (график — чисто UI-объект).
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>Создаёт окно и подписывается на события ViewModel.</summary>
    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.ExperimentCompleted += OnExperimentCompleted;
        _viewModel.HeatmapCompleted += OnHeatmapCompleted;
        _viewModel.PngExportRequested += OnPngExportRequested;
    }

    /// <summary>Обновляет график после одномерного эксперимента (карта скрывается).</summary>
    private void OnExperimentCompleted(object? sender, ChartData data)
    {
        HeatmapHost.Visibility = Visibility.Collapsed;
        Surface3DHost.Visibility = Visibility.Collapsed;
        ChartHost.Visibility = Visibility.Visible;

        var series = data.Series.Select(s => new ChartSeries
        {
            Title = s.Title,
            Color = (Color)ColorConverter.ConvertFromString(s.ColorHex),
            Points = s.Points.Select(p => new ChartPoint(p.X, p.Y)).ToList(),
            ShowPoints = s.ShowPoints,
            DashPattern = s.Dashed ? new DoubleCollection { 4, 3 } : null
        }).ToList();

        Chart.SetData(data.Title, "Размер входных данных n", "Время, с", series);
    }

    private HeatmapData? _lastHeatmapData;

    /// <summary>Показывает 3D-поверхность (или тепловую карту) A(n×m)·B(m×n) вместо линейного графика.</summary>
    private void OnHeatmapCompleted(object? sender, HeatmapData data)
    {
        ChartHost.Visibility = Visibility.Collapsed;
        _lastHeatmapData = data;

        Surface3D.SetData(data);
        Heatmap.SetData(data);

        if (Surface3D.LastRange is { } r)
        {
            SurfaceRangeText.Text =
                $"n: {r.minN:N0}–{r.maxN:N0}   m: {r.minM:N0}–{r.maxM:N0}   " +
                $"время: {r.minValue * 1000:F3}–{r.maxValue * 1000:F3} мс";
        }

        ApplyMatrixViewMode();
    }

    /// <summary>Переключает между 3D-поверхностью и тепловой картой для уже построенных данных.</summary>
    private void ApplyMatrixViewMode()
    {
        if (_lastHeatmapData == null) return;
        bool showSurface = ViewModeSurface.IsChecked == true;
        Surface3DHost.Visibility = showSurface ? Visibility.Visible : Visibility.Collapsed;
        HeatmapHost.Visibility = showSurface ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MatrixViewMode_Changed(object sender, RoutedEventArgs e) => ApplyMatrixViewMode();

    private void ResetSurfaceView_Click(object sender, RoutedEventArgs e) => Surface3D.ResetView();

    /// <summary>Сохраняет видимую визуализацию (график или карту) в PNG.</summary>
    private void OnPngExportRequested(object? sender, string fileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            FileName = fileName
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (Surface3DHost.Visibility == Visibility.Visible)
            {
                MessageBox.Show("Для 3D-поверхности используйте снимок окна (Win+Shift+S) — "
                    + "экспорт в PNG пока реализован только для графика и тепловой карты.",
                    "Экспорт графика", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (HeatmapHost.Visibility == Visibility.Visible)
            {
                Heatmap.ExportPng(dialog.FileName);
            }
            else
            {
                Chart.ExportPng(dialog.FileName);
            }
            MessageBox.Show($"Файл сохранён: {dialog.FileName}", "Экспорт графика",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось сохранить график: {ex.Message}", "Экспорт графика",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Переключение обычной/логарифмической шкалы осей.
    /// В каждой группе второй переключатель (не помеченный именем) — логарифм.
    /// </summary>
    private void Scale_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (Chart == null) return;
        bool logX = !(ScaleXLinear?.IsChecked ?? true);
        bool logY = !(ScaleYLinear?.IsChecked ?? true);
        Chart.SetScale(logX, logY);
    }
}
