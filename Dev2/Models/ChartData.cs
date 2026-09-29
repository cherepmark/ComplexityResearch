namespace ComplexityResearch.Models;

/// <summary>Точка серии для построения графика: (n, время в секундах).</summary>
public readonly record struct ChartPointData(double X, double Y);

/// <summary>
/// Серия данных для графика. DTO, независимый от элемента отрисовки:
/// ViewModel формирует список серий, а код окна преобразует его
/// в серии <c>Controls.ChartControl</c>.
/// </summary>
/// <param name="Title">Название серии (для легенды).</param>
/// <param name="ColorHex">Цвет линии в формате #RRGGBB.</param>
/// <param name="Points">Точки серии.</param>
/// <param name="Dashed">Рисовать линию пунктиром.</param>
/// <param name="ShowPoints">Отображать точки.</param>
public sealed record ChartSeriesData(
    string Title,
    string ColorHex,
    IReadOnlyList<ChartPointData> Points,
    bool Dashed = false,
    bool ShowPoints = true);

/// <summary>
/// Данные графика, передаваемые ViewModel в окно после завершения эксперимента.
/// </summary>
public sealed record ChartData(
    string Title,
    IReadOnlyList<ChartSeriesData> Series);
