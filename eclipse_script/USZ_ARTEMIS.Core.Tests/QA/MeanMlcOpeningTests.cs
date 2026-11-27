using System;
using USZ_ARTEMIS.Core.QA;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.QA;

public sealed class MeanMlcOpeningTests
{
    [Fact]
    public void Includes_the_first_pair_even_when_it_is_the_only_open_pair()
    {
        var positions = new float[,] { { -2, 1 }, { 4, 1 } };

        Assert.Equal(6.0, MeanMlcOpening.CalculateSingleLayer(new[] { positions }));
    }

    [Fact]
    public void Excludes_crossed_leaves_and_gaps_within_the_existing_closure_tolerance()
    {
        var positions = new float[,] { { 0, 5, 0, 0, 0 }, { 6, -5, 0, 0.01f, 10 } };

        Assert.Equal(8.0, MeanMlcOpening.CalculateSingleLayer(new[] { positions }));
    }

    [Fact]
    public void Averages_open_pairs_across_all_control_points()
    {
        var first = new float[,] { { 0, 0, 0 }, { 4, 0, 0 } };
        var second = new float[,] { { 0, 0, 0 }, { 8, 12, 16 } };

        Assert.Equal(10.0, MeanMlcOpening.CalculateSingleLayer(new[] { first, second }));
    }

    [Fact]
    public void Returns_unavailable_when_there_are_no_open_pairs()
    {
        var closed = new float[,] { { 0, 2 }, { 0, 1 } };

        Assert.True(double.IsNaN(MeanMlcOpening.CalculateSingleLayer(new[] { closed })));
        Assert.True(double.IsNaN(MeanMlcOpening.CalculateSingleLayer(new[] { new float[0, 0] })));
        Assert.True(double.IsNaN(MeanMlcOpening.CalculateSingleLayer(Array.Empty<float[,]>())));
    }

    [Fact]
    public void Returns_unavailable_for_invalid_data_after_a_valid_control_point()
    {
        var valid = new float[,] { { 0 }, { 4 } };
        var nonFinite = new float[,] { { 0 }, { float.PositiveInfinity } };

        Assert.True(double.IsNaN(MeanMlcOpening.CalculateSingleLayer(new[] { valid, new float[2, 2] })));
        Assert.True(double.IsNaN(MeanMlcOpening.CalculateSingleLayer(new[] { valid, nonFinite })));
    }
}
