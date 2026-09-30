using ComplexityResearch.Algorithms;
using ComplexityResearch.Services;
using Xunit;

namespace ComplexityResearch.Tests;

/// <summary>
/// Тесты реверса массива (№12) — вклад участника Dev3.
/// </summary>
public class ReverseTests
{
    [Fact]
    public void Reverse_Matches_Array_Reverse()
    {
        var rng = new Random(20260914);
        foreach (int n in new[] { 1, 2, 3, 8, 101, 1000 })
        {
            int[] a = Enumerable.Range(0, n).Select(_ => rng.Next(-100, 100)).ToArray();
            int[] expected = (int[])a.Clone();
            Array.Reverse(expected);

            ReverseArrayAlgorithm.ReverseInPlace(a);
            Assert.Equal(expected, a);
        }
    }

    [Fact]
    public void Reverse_Is_Involutive()
    {
        int[] a = [1, 2, 3, 4, 5, 6, 7];
        int[] original = (int[])a.Clone();
        ReverseArrayAlgorithm.ReverseInPlace(a);
        ReverseArrayAlgorithm.ReverseInPlace(a);
        Assert.Equal(original, a);
    }

    [Fact]
    public void Reverse_Estimate_Grows_Linearly()
    {
        var algo = new ReverseArrayAlgorithm();
        double ops1 = algo.EstimateOperationCounts(100_000).Total;
        double ops2 = algo.EstimateOperationCounts(200_000).Total;
        Assert.InRange(ops2 / ops1, 1.9, 2.1);
        Assert.Equal("O(n)", algo.ComplexityClass);
    }
}
