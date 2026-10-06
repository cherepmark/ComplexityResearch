namespace ComplexityResearch.Models;

/// <summary>
/// Ячейка тепловой карты: (n, m, время). n и m — размеры матриц
/// эксперимента A(n×m)·B(m×n), значение — время в секундах.
/// </summary>
/// <param name="N">Строки матрицы A.</param>
/// <param name="M">Внутреннее измерение (столбцы A = строки B).</param>
/// <param name="ValueSeconds">Среднее время умножения, секунды.</param>
public readonly record struct HeatmapCell(double N, double M, double ValueSeconds);

/// <summary>
/// Данные тепловой карты для эксперимента A(n×m)·B(m×n): все тройки
/// (n, m, время) целиком, без разбиения на отдельные кривые по m.
/// </summary>
public sealed record HeatmapData(
    string Title,
    string XTitle,
    string YTitle,
    string ValueTitle,
    IReadOnlyList<HeatmapCell> Cells);
