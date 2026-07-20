using USZ_ARTEMIS.Core.Rules;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Rules;

public sealed class PlanIdUtilitiesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("A", "A")]
    [InlineData("Plan", "Plan")]
    [InlineData("PlanaA", "Plana")]
    [InlineData("PlanB", "Plan")]
    [InlineData("PlanAA", "PlanA")]
    public void GuessBasePlanId_preserves_existing_copy_heuristic(string? planId, string? expected)
    {
        Assert.Equal(expected, PlanIdUtilities.GuessBasePlanId(planId!));
    }
}
