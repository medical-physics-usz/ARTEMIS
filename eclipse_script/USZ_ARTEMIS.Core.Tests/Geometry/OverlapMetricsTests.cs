using USZ_ARTEMIS.Core.Geometry;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Geometry;

public sealed class OverlapMetricsTests
{
    [Theory]
    [InlineData(10.0, 10.0, 10.0, 1.0)]
    [InlineData(10.0, 10.0, 0.0, 0.0)]
    [InlineData(10.0, 20.0, 5.0, 1.0 / 3.0)]
    [InlineData(0.0, 0.0, 0.0, 1.0)]
    public void CalculateDiceSorensen_ReturnsExpectedValue(
        double first,
        double second,
        double intersection,
        double expected)
    {
        double result = OverlapMetrics.CalculateDiceSorensen(first, second, intersection);

        Assert.Equal(expected, result, 10);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CalculateDiceSorensen_RejectsInvalidMeasures(double invalidMeasure)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            OverlapMetrics.CalculateDiceSorensen(invalidMeasure, 1.0, 0.0));
    }
}
