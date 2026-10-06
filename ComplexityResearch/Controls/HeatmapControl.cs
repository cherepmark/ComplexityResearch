using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComplexityResearch.Models;

namespace ComplexityResearch.Controls;

/// <summary>
/// Элемент тепловой карты для двумерного эксперимента «A(n×m)·B(m×n)».
/// </summary>
/// <remarks>
/// <para>
/// Ось X = n, ось Y = m, цвет ячейки = время (чем «горячее» цвет — тем дольше).
/// Все тройки (n, m, время) показываются ОДНИМ связным представлением
/// вместо набора отдельных 2D-кривых по m. Справа — цветовая шкала
/// (colorbar) с минимумом, максимумом и подписью величины.
/// </para>
/// <para>
/// Поддерживает зум колесом (по осям значений n/m через изменение видимого
/// диапазона не требуется — сетка дискретная), поэтому из интеракций есть
/// только подсказки в ячейках и экспорт PNG.
/// </para>
/// </remarks>
public class HeatmapControl : FrameworkElement
{
    private const double LeftMargin = 90;
    private const double RightMargin = 150; // место под colorbar
    private const double TopMargin = 45;
    private const double BottomMargin = 60;

    private HeatmapData? _data;
    private double _maxValue;      // для нормировки цвета
    private double _minValue;

    /// <summary>Задаёт данные тепловой карты.</summary>
    public void SetData(HeatmapData data)
    {
        _data = data;
        _minValue = double.MaxValue;
        _maxValue = double.MinValue;
        foreach (var c in data.Cells)
        {
            _minValue = Math.Min(_minValue, c.ValueSeconds);
            _maxValue = Math.Max(_maxValue, c.ValueSeconds);
        }
        if (_minValue > _maxValue)
        {
            _minValue = 0;
            _maxValue = 0;
        }
        InvalidateVisual();
    }

    /// <summary>Очищает элемент.</summary>
    public void Clear()
    {
        _data = null;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Render(dc, ActualWidth, ActualHeight, dpi);
    }

    /// <summary>Экспорт в PNG.</summary>
    public void ExportPng(string path, int width = 1400, int height = 900)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Render(dc, width, height, 1.0);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void Render(DrawingContext dc, double width, double height, double pixelsPerDip)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var typefaceBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var textBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        textBrush.Freeze();

        FormattedText MakeText(string s, double size, bool bold = false, Brush? brush = null) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? typefaceBold : typeface,
                size, brush ?? textBrush, pixelsPerDip);

        var data = _data;
        if (data == null || data.Cells.Count == 0)
        {
            var empty = MakeText("Нет данных — запустите эксперимент A(n×m)·B(m×n)", 14);
            dc.DrawText(empty, new Point(width / 2 - empty.Width / 2, height / 2));
            return;
        }

        // Уникальные отсортированные значения осей.
        var xs = data.Cells.Select(c => c.N).Distinct().OrderBy(v => v).ToList();
        var ys = data.Cells.Select(c => c.M).Distinct().OrderBy(v => v).ToList();
        var lookup = data.Cells.ToDictionary(c => (c.N, c.M), c => c.ValueSeconds);

        var plot = new Rect(LeftMargin, TopMargin,
            Math.Max(10, width - LeftMargin - RightMargin),
            Math.Max(10, height - TopMargin - BottomMargin));
        double cellW = plot.Width / xs.Count;
        double cellH = plot.Height / ys.Count;

        // Ячейки.
        foreach (var cell in data.Cells)
        {
            int xi = xs.IndexOf(cell.N);
            int yi = ys.IndexOf(cell.M);
            double t = _maxValue > _minValue ? (cell.ValueSeconds - _minValue) / (_maxValue - _minValue) : 0;
            var rect = new Rect(plot.X + xi * cellW, plot.Y + yi * cellH, cellW, cellH);
            dc.DrawRectangle(ColormapBrush(t), null, rect);

            // Подпись времени в ячейке, если ячеек немного.
            if (xs.Count * ys.Count <= 64)
            {
                var label = MakeText(FormatValue(cell.ValueSeconds), Math.Max(9, Math.Min(12, cellH * 0.4)), brush: Brushes.Black);
                if (label.Width < cellW - 4)
                {
                    dc.DrawText(label, new Point(rect.X + (cellW - label.Width) / 2, rect.Y + (cellH - label.Height) / 2));
                }
            }
        }

        // Разделительные линии сетки.
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 0.5);
        gridPen.Freeze();
        for (int i = 0; i <= xs.Count; i++)
        {
            double x = plot.X + i * cellW;
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
        for (int j = 0; j <= ys.Count; j++)
        {
            double y = plot.Y + j * cellH;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
        var framePen = new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 1);
        framePen.Freeze();
        dc.DrawRectangle(null, framePen, plot);

        // Подписи осей.
        foreach (double v in xs)
        {
            int xi = xs.IndexOf(v);
            var label = MakeText(FormatValue(v), 11);
            dc.DrawText(label, new Point(plot.X + xi * cellW + (cellW - label.Width) / 2, plot.Bottom + 5));
        }
        foreach (double v in ys)
        {
            int yi = ys.IndexOf(v);
            var label = MakeText(FormatValue(v), 11);
            dc.DrawText(label, new Point(plot.Left - label.Width - 6, plot.Y + yi * cellH + (cellH - label.Height) / 2));
        }

        var xTitle = MakeText(data.XTitle, 12);
        dc.DrawText(xTitle, new Point(width / 2 - xTitle.Width / 2, height - xTitle.Height - 6));
        var yTitle = MakeText(data.YTitle, 12);
        double ylx = 14, yly = height / 2 + yTitle.Width / 2;
        dc.PushTransform(new RotateTransform(-90, ylx, yly));
        dc.DrawText(yTitle, new Point(ylx, yly));
        dc.Pop();

        if (!string.IsNullOrEmpty(data.Title))
        {
            var title = MakeText(data.Title, 15, bold: true);
            dc.DrawText(title, new Point(width / 2 - title.Width / 2, 12));
        }

        // --- Colorbar ---
        double barW = 22, barH = plot.Height * 0.85;
        var barRect = new Rect(plot.Right + 45, plot.Top + (plot.Height - barH) / 2, barW, barH);
        int steps = 60;
        for (int i = 0; i < steps; i++)
        {
            double t = 1.0 - (double)i / steps; // сверху максимум
            var strip = new Rect(barRect.X, barRect.Y + i * (barRect.Height / steps), barRect.Width, barRect.Height / steps + 0.5);
            dc.DrawRectangle(ColormapBrush(t), null, strip);
        }
        dc.DrawRectangle(null, framePen, barRect);
        var maxLabel = MakeText(FormatValue(_maxValue), 11);
        dc.DrawText(maxLabel, new Point(barRect.Right + 6, barRect.Top - maxLabel.Height / 2));
        var minLabel = MakeText(FormatValue(_minValue), 11);
        dc.DrawText(minLabel, new Point(barRect.Right + 6, barRect.Bottom - minLabel.Height / 2));
        var valueTitle = MakeText(data.ValueTitle, 11);
        dc.PushTransform(new RotateTransform(90, barRect.Right + 44, barRect.Bottom));
        dc.DrawText(valueTitle, new Point(barRect.Right + 44, barRect.Bottom));
        dc.Pop();
    }

    /// <summary>
    /// Цветовая шкала «холодный → горячий»: синий → голубой → зелёный →
    /// жёлтый → оранжевый → красный. t ∈ [0, 1].
    /// </summary>
    public static Brush ColormapBrush(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new SolidColorBrush(Colormap(t));
    }

    /// <summary>Цвет шкалы для значения t ∈ [0, 1].</summary>
    public static Color Colormap(double t)
    {
        t = Math.Clamp(t, 0, 1);
        (double stop, Color color)[] stops =
        [
            (0.00, Color.FromRgb(30, 58, 138)),   // тёмно-синий
            (0.25, Color.FromRgb(46, 134, 222)),  // голубой
            (0.50, Color.FromRgb(46, 158, 68)),   // зелёный
            (0.70, Color.FromRgb(242, 193, 46)),  // жёлтый
            (0.85, Color.FromRgb(240, 124, 30)),  // оранжевый
            (1.00, Color.FromRgb(215, 38, 61))    // красный
        ];
        for (int i = 0; i < stops.Length - 1; i++)
        {
            if (t <= stops[i + 1].stop)
            {
                double k = (t - stops[i].stop) / (stops[i + 1].stop - stops[i].stop);
                var a = stops[i].color;
                var b = stops[i + 1].color;
                return Color.FromRgb(
                    (byte)(a.R + (b.R - a.R) * k),
                    (byte)(a.G + (b.G - a.G) * k),
                    (byte)(a.B + (b.B - a.B) * k));
            }
        }
        return stops[^1].color;
    }

    private static string FormatValue(double v)
    {
        if (v == 0) return "0";
        double abs = Math.Abs(v);
        if (abs >= 1e6 || abs < 1e-4) return v.ToString("G2", CultureInfo.CurrentCulture);
        return v.ToString("0.###", CultureInfo.CurrentCulture);
    }

    /// <summary>Контекстная подсказка: значения ячейки под курсором.</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_data == null || _data.Cells.Count == 0) return;
        var p = e.GetPosition(this);
        var plot = new Rect(LeftMargin, TopMargin,
            Math.Max(10, ActualWidth - LeftMargin - RightMargin),
            Math.Max(10, ActualHeight - TopMargin - BottomMargin));
        if (!plot.Contains(p))
        {
            ToolTip = null;
            return;
        }
        var xs = _data.Cells.Select(c => c.N).Distinct().OrderBy(v => v).ToList();
        var ys = _data.Cells.Select(c => c.M).Distinct().OrderBy(v => v).ToList();
        double cellW = plot.Width / xs.Count;
        double cellH = plot.Height / ys.Count;
        int xi = (int)((p.X - plot.X) / cellW);
        int yi = (int)((p.Y - plot.Y) / cellH);
        if (xi < 0 || xi >= xs.Count || yi < 0 || yi >= ys.Count)
        {
            ToolTip = null;
            return;
        }
        double n = xs[xi], m = ys[yi];
        var cell = _data.Cells.FirstOrDefault(c => c.N == n && c.M == m);
        ToolTip = $"n = {n:N0}, m = {m:N0}\nВремя: {cell.ValueSeconds / 1e9 * 1000:F3} мс";
    }
}
