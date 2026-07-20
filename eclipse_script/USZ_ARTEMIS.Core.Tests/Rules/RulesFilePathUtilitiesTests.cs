using USZ_ARTEMIS.Core.Rules;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests.Rules;

public sealed class RulesFilePathUtilitiesTests
{
    [Fact]
    public void CreateFileName_uses_rules_source_plan_id()
    {
        string basePlanFile = RulesFilePathUtilities.CreateFileName("Patient", "Course", "BasePlan");
        string copiedPlanFile = RulesFilePathUtilities.CreateFileName("Patient", "Course", "BasePlanA");

        Assert.Equal("Patient_Course_BasePlan.json", basePlanFile);
        Assert.Equal("Patient_Course_BasePlanA.json", copiedPlanFile);
    }
}
