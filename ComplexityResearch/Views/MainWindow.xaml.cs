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

    /// <summary>Показывает тепловую карту A(n×m)·B(m×n) вместо линейного графика.</summary>
    private void OnHeatmapCompleted(object? sender, HeatmapData data)
    {
        ChartHost.Visibility = Visibility.Collapsed;
        HeatmapHost.Visibility = Visibility.Visible;
        Heatmap.SetData(data);
    }

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
            if (HeatmapHost.Visibility == Visibility.Visible)
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
