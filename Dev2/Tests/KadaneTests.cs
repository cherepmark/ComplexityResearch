using ComplexityResearch.Algorithms;
using ComplexityResearch.Services;
using Xunit;

namespace ComplexityResearch.Tests;

/// <summary>
/// Тесты алгоритма Кадане (№11) — вклад участника Dev2.
/// </summary>
public class KadaneTests
{
    [Fact]
    public void Kadane_Known_Case()
    {
        // Классический пример: максимальный подмассив [4, -1, 2, 1] → 6.
        int[] a = [-2, 1, -3, 4, -1, 2, 1, -5, 4];
        Assert.Equal(6, KadaneAlgorithm.MaxSubarraySum(a));
    }

    [Fact]
    public void Kadane_Handles_All_Negative_Array()
    {
        // Все отрицательные: максимум — один наибольший элемент.
        int[] a = [-5, -2, -9, -1, -7];
        Assert.Equal(-1, KadaneAlgorithm.MaxSubarraySum(a));
    }

    [Fact]
    public void Kadane_Handles_Single_Element_And_Positive()
    {
        Assert.Equal(42, KadaneAlgorithm.MaxSubarraySum([42]));
        Assert.Equal(15, KadaneAlgorithm.MaxSubarraySum([1, 2, 3, 4, 5]));
    }

    [Fact]
    public void Kadane_Matches_Brute_Force_On_Random_Arrays()
    {
        var rng = new Random(20260914);
        for (int trial = 0; trial < 30; trial++)
        {
            int n = rng.Next(1, 40);
            int[] a = Enumerable.Range(0, n).Select(_ => rng.Next(-20, 21)).ToArray();

            // Эталон: перебор всех подмассивов O(n²).
            long expected = long.MinValue;
            for (int i = 0; i < n; i++)
            {
                long sum = 0;
                for (int j = i; j < n; j++)
                {
                    sum += a[j];
                    expected = Math.Max(expected, sum);
                }
            }

            Assert.Equal(expected, KadaneAlgorithm.MaxSubarraySum(a));
        }
    }

    [Fact]
    public void Kadane_Estimate_Grows_Linearly()
    {
        var algo = new KadaneAlgorithm();
        double ops1 = algo.EstimateOperationCounts(100_000).Total;
        double ops2 = algo.EstimateOperationCounts(200_000).Total;
        Assert.InRange(ops2 / ops1, 1.9, 2.1);
        Assert.Equal("O(n)", algo.ComplexityClass);
    }
}
