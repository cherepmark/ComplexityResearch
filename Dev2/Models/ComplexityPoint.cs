namespace ComplexityResearch.Models;

/// <summary>
/// Одна точка серии измерений: пара (n, время) для построения графика
/// и аппроксимации.
/// </summary>
/// <param name="N">Размер входных данных.</param>
/// <param name="TimeSeconds">Время в секундах.</param>
public readonly record struct ComplexityPoint(double N, double TimeSeconds);
