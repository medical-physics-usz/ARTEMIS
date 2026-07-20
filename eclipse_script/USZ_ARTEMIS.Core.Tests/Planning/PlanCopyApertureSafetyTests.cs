using USZ_ARTEMIS.Core.Planning;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Planning;

public sealed class PlanCopyApertureSafetyTests
{
    [Fact]
    public void Missing_containment_result_does_not_fail_open()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            PlanCopyApertureSafety.FromContainmentCheck("PTV_V1+2cm_Ph", null!));
    }

    [Fact]
    public void ContainedPtvs_allow_automatic_optimization()
    {
        var result = PlanCopyApertureSafety.FromContainmentCheck("PTV_V1+2cm_Ph", new string[0]);

        Assert.True(result.AllowAutomaticOptimization);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public void Ptvs_outside_ring_require_review_and_manual_adjustment()
    {
        var result = PlanCopyApertureSafety.FromContainmentCheck(
            "PTV_V1+2cm_Ph",
            new[] { "PTV2", "PTV1", "PTV2" });

        Assert.False(result.AllowAutomaticOptimization);
        Assert.Contains("PTV1, PTV2", result.WarningMessage);
        Assert.Contains("reviewed again by the MD", result.WarningMessage);
        Assert.Contains("manually adjust the jaws and aperture", result.WarningMessage);
    }

    [Fact]
    public void Failed_check_disables_automatic_optimization()
    {
        var result = PlanCopyApertureSafety.FromFailedCheck(
            "PTV_V1+2cm_Ph",
            "No non-empty PTV structures were found.");

        Assert.False(result.AllowAutomaticOptimization);
        Assert.Contains("could not verify", result.WarningMessage);
        Assert.Contains("manually adjust the jaws and aperture", result.WarningMessage);
        Assert.Contains("No non-empty PTV structures were found.", result.WarningMessage);
    }
}
