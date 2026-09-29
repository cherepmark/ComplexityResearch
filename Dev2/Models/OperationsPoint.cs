namespace ComplexityResearch.Models;

/// <summary>
/// Точка «операционного» бенчмарка возведения в степень:
/// измеряется НЕ время, а количество элементарных операций.
/// </summary>
/// <param name="N">Показатель степени n.</param>
/// <param name="MeasuredOperations">Фактически подсчитанное число операций.</param>
/// <param name="TheoreticalOperations">Теоретическая оценка числа операций.</param>
public readonly record struct OperationsPoint(
    double N,
    double MeasuredOperations,
    double TheoreticalOperations);
