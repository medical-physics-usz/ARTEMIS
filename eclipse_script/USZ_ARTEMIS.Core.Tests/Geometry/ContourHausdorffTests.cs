using System;
using System.Collections.Generic;
using USZ_ARTEMIS.Core.Geometry;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Geometry;

public sealed class ContourHausdorffTests
{
    [Fact]
    public void CalculateSymmetric_ReturnsZeroForIdenticalContours()
    {
        var contour = CreateSquare(0, 0, 10, 0);

        double result = ContourHausdorff.CalculateSymmetric(contour, contour, 0.5);

        Assert.Equal(0.0, result, 10);
    }

    [Fact]
    public void CalculateSymmetric_ReturnsTranslationDistance()
    {
        var first = CreateSquare(0, 0, 10, 0);
        var second = CreateSquare(3, 0, 10, 0);

        double result = ContourHausdorff.CalculateSymmetric(first, second, 0.5);

        Assert.Equal(3.0, result, 10);
    }

    [Fact]
    public void CalculateSymmetric_IncludesDistanceBetweenImagePlanes()
    {
        var first = CreateSquare(0, 0, 10, 0);
        var second = CreateSquare(0, 0, 10, 4);

        double result = ContourHausdorff.CalculateSymmetric(first, second, 0.5);

        Assert.Equal(4.0, result, 10);
    }

    [Fact]
    public void CalculateSymmetric_ReturnsInfinityWhenOnlyOneContourIsEmpty()
    {
        var contour = CreateSquare(0, 0, 10, 0);

        double result = ContourHausdorff.CalculateSymmetric(
            contour,
            Array.Empty<ContourSlice>(),
            0.5);

        Assert.True(double.IsPositiveInfinity(result));
    }

    private static IReadOnlyList<ContourSlice> CreateSquare(double x, double y, double size, double z)
    {
        IReadOnlyList<ContourPoint2D> points = new[]
        {
            new ContourPoint2D(x, y),
            new ContourPoint2D(x + size, y),
            new ContourPoint2D(x + size, y + size),
            new ContourPoint2D(x, y + size)
        };

        return new[]
        {
            new ContourSlice(z, new[] { points })
        };
    }
}
