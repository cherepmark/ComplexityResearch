namespace ComplexityResearch.Models;

/// <summary>
/// Метаданные алгоритма для информационной панели интерфейса.
/// </summary>
/// <param name="Name">Название алгоритма.</param>
/// <param name="Description">Краткое описание идеи.</param>
/// <param name="Complexity">Теоретическая сложность (например, O(n log n)).</param>
/// <param name="Application">Практическое применение.</param>
/// <param name="TheoreticalFormula">Формула теоретического времени.</param>
public sealed record AlgorithmMetadata(
    string Name,
    string Description,
    string Complexity,
    string Application,
    string TheoreticalFormula);
