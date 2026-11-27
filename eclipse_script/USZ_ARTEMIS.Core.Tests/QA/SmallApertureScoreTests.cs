using System;
using USZ_ARTEMIS.Core.QA;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.QA;

public sealed class SmallApertureScoreTests
{
    [Fact]
    public void Counts_the_first_leaf_pair_and_uses_a_strict_ten_mm_threshold()
    {
        var positions = new float[,]
        {
            { -5, -5, 0, 0, 2 },
            { 4.99f, 5, 10.01f, 0, 1 }
        };

        SmallApertureScoreResult result = SmallApertureScore.Calculate(new[] { positions });

        Assert.Equal(SmallApertureScoreStatus.Available, result.Status);
        Assert.Equal(100.0 / 3, result.Percentage);
    }

    [Fact]
    public void Pools_leaf_pairs_across_control_points_with_different_open_counts()
    {
        var first = new float[,] { { 0, 0, 0 }, { 9, 0, 0 } };
        var second = new float[,] { { 0, 0, 0 }, { 11, 12, 13 } };

        SmallApertureScoreResult result = SmallApertureScore.Calculate(new[] { first, second });

        Assert.Equal(SmallApertureScoreStatus.Available, result.Status);
        Assert.Equal(25.0, result.Percentage);
    }

    [Theory]
    [InlineData(9f, 100.0)]
    [InlineData(10f, 0.0)]
    public void Reports_a_valid_score_at_either_end_of_the_range(float gap, double percentage)
    {
        var positions = new float[,] { { 0 }, { gap } };

        SmallApertureScoreResult result = SmallApertureScore.Calculate(new[] { positions });

        Assert.Equal(SmallApertureScoreStatus.Available, result.Status);
        Assert.Equal(percentage, result.Percentage);
    }

    [Fact]
    public void Distinguishes_no_control_points_no_mlc_and_no_open_pairs()
    {
        var closed = new float[,] { { 0, 2 }, { 0, 1 } };

        AssertUnavailable(Array.Empty<float[,]>(), SmallApertureScoreStatus.NoControlPoints);
        AssertUnavailable(new[] { new float[0, 0], new float[0, 0] }, SmallApertureScoreStatus.NoMlc);
        AssertUnavailable(new[] { closed }, SmallApertureScoreStatus.NoOpenLeafPairs);
    }

    [Fact]
    public void Rejects_missing_or_inconsistent_leaf_arrays_after_valid_observations()
    {
        var valid = new float[,] { { 0 }, { 9 } };

        AssertUnavailable(new[] { valid, null! }, SmallApertureScoreStatus.InvalidLeafPositions);
        AssertUnavailable(new[] { valid, new float[0, 0] }, SmallApertureScoreStatus.InvalidLeafPositions);
        AssertUnavailable(new[] { valid, new float[1, 1] }, SmallApertureScoreStatus.InvalidLeafPositions);
        AssertUnavailable(new[] { valid, new float[2, 2] }, SmallApertureScoreStatus.InvalidLeafPositions);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Rejects_non_finite_leaf_positions(float position)
    {
        var positions = new float[,] { { 0, 0 }, { 9, position } };

        AssertUnavailable(new[] { positions }, SmallApertureScoreStatus.InvalidLeafPositions);
    }

    private static void AssertUnavailable(float[][,] positions, SmallApertureScoreStatus expectedStatus)
    {
        SmallApertureScoreResult result = SmallApertureScore.Calculate(positions);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Percentage);
    }
}
